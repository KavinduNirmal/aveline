using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.3 (plan §4.4): the exactly-once disclosure sequence. The conditional stamp on the consent
/// row is the concurrency control, a failed or unconfigured send resets it so the next inbound
/// message retries, and a successful send writes the append-only audit row with identifiers only.
/// </summary>
public class DisclosureDispatchServiceTests
{
    private const string To = "+94771234567";
    private static readonly string SigningKey = Convert.ToBase64String(new byte[32]);

    private readonly AppDbContext _context;
    private readonly FakeConsentRepository _consent = new();
    private readonly FakeOutboundMessaging _outbound = new();
    private readonly DisclosureMetrics _metrics = new();
    private readonly Guid _orgId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    public DisclosureDispatchServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"DisclosureDispatch_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);

        _consent.Rows[(_orgId, _customerId)] = new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = _customerId,
            ConsentStatus = ConsentStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
        };
    }

    private DisclosureDispatchService CreateService(string? signingKey = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["App:BaseUrl"] = "https://app.aveline.lk",
        };
        if (signingKey is not null)
        {
            settings[PrivacyLinkSigner.ConfigKey] = signingKey;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var organizations = new Mock<IOrganizationRepository>();
        organizations
            .Setup(repo => repo.GetByIdAsync(_orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new Organization { Id = _orgId, Name = "Emerald Boutique", Slug = "emerald-boutique" });

        return new DisclosureDispatchService(
            _consent,
            organizations.Object,
            _context,
            _outbound,
            new DisclosureBodyBuilder(),
            new PrivacyLinkSigner(configuration),
            _metrics,
            NullLogger<DisclosureDispatchService>.Instance);
    }

    [Fact]
    public async Task Dispatch_WhenNotYetDisclosed_SendsOnceAndStampsShownAtAndVersion()
    {
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE1");
        var service = CreateService(SigningKey);

        var result = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.Sent, result.Outcome);
        Assert.Equal("wamid.DISCLOSURE1", result.ProviderMessageId);

        var sent = Assert.Single(_outbound.Sent);
        Assert.Equal(_orgId, sent.OrganizationId);
        Assert.Equal(To, sent.ToE164);

        var row = _consent.Rows[(_orgId, _customerId)];
        Assert.NotNull(row.DisclosureShownAt);
        Assert.Equal(DisclosureBodyBuilder.CurrentVersionValue, row.DisclosureVersion);
    }

    [Fact]
    public async Task Dispatch_WithoutAConfiguredSigningKey_SkipsAndLeavesShownAtNull()
    {
        var service = CreateService(signingKey: null);

        var result = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.SigningKeyMissing, result.Outcome);
        Assert.Empty(_outbound.Sent);
        Assert.Null(_consent.Rows[(_orgId, _customerId)].DisclosureShownAt);
    }

    [Fact]
    public async Task Dispatch_ASecondTimeAfterASuccessfulSend_SendsNothing()
    {
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE1");
        var service = CreateService(SigningKey);

        var first = await service.DispatchAsync(_orgId, _customerId, To);
        var second = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.Sent, first.Outcome);
        Assert.Equal(DisclosureDispatchOutcome.AlreadyDisclosed, second.Outcome);
        // Exactly one provider call across the two dispatches.
        Assert.Single(_outbound.Sent);
    }

    [Fact]
    public async Task Dispatch_WhenTheChannelIsNotConfigured_ResetsShownAtSoTheNextInboundRetries()
    {
        _outbound.Next = OutboundMessageResult.NotConfigured("no credentials");
        var service = CreateService(SigningKey);

        var result = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.NotConfigured, result.Outcome);
        Assert.Null(_consent.Rows[(_orgId, _customerId)].DisclosureShownAt);
        Assert.Equal(1, _consent.ReleaseCalls);
    }

    [Fact]
    public async Task Dispatch_WhenTheProviderFails_ResetsShownAtAndTheNextInboundRetries()
    {
        _outbound.Next = OutboundMessageResult.Failed("Meta said no");
        var service = CreateService(SigningKey);

        var failed = await service.DispatchAsync(_orgId, _customerId, To);
        Assert.Equal(DisclosureDispatchOutcome.Failed, failed.Outcome);
        Assert.Null(_consent.Rows[(_orgId, _customerId)].DisclosureShownAt);

        // The retry is the whole point of resetting: the next inbound message sends it.
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE2");
        var retried = await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Equal(DisclosureDispatchOutcome.Sent, retried.Outcome);
        Assert.Equal(2, _outbound.Sent.Count);
        Assert.NotNull(_consent.Rows[(_orgId, _customerId)].DisclosureShownAt);
    }

    [Fact]
    public async Task Dispatch_UsesAStableIdempotencyKeyDerivedFromTheConsentRow()
    {
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE1");
        var service = CreateService(SigningKey);

        await service.DispatchAsync(_orgId, _customerId, To);

        var sent = Assert.Single(_outbound.Sent);
        Assert.Equal(
            $"disclosure:{_orgId:D}:{_customerId:D}:{DisclosureBodyBuilder.CurrentVersionValue}",
            sent.IdempotencyKey);
    }

    [Fact]
    public async Task Dispatch_BuildsTheDisclosureWithTheBoutiqueNameAndTheSignedOptOutLink()
    {
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE1");
        var service = CreateService(SigningKey);

        await service.DispatchAsync(_orgId, _customerId, To);

        var sent = Assert.Single(_outbound.Sent);
        Assert.Contains("Emerald Boutique", sent.Text, StringComparison.Ordinal);
        Assert.Contains("Aveline, an AI assistant", sent.Text, StringComparison.Ordinal);
        Assert.Contains("A real member of our team reads every conversation", sent.Text, StringComparison.Ordinal);
        Assert.Contains("https://app.aveline.lk/privacy?org=emerald-boutique", sent.Text, StringComparison.Ordinal);
        Assert.Contains($"https://app.aveline.lk/privacy/opt-out?o={_orgId:D}&v=1&s=", sent.Text, StringComparison.Ordinal);
        Assert.Contains("Reply STOP", sent.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_OnSuccess_WritesAConsentAuditEntryWithIdentifiersOnly()
    {
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE1");
        var service = CreateService(SigningKey);

        await service.DispatchAsync(_orgId, _customerId, To);

        var entry = Assert.Single(_context.ConsentAuditEntries);
        Assert.Equal(_orgId, entry.OrganizationId);
        Assert.Equal(_customerId, entry.CustomerId);
        Assert.Equal(Aveline.Api.Modules.Audit.Models.AuditAction.DisclosureShown, entry.Action);
        Assert.Equal("welcome_message", entry.Source);
        Assert.Equal("System", entry.ActorKind);
        Assert.Equal(ConsentStatuses.Pending, entry.NewStatus);

        // Identifiers only: no phone number, no message text, no OTP/secret.
        Assert.DoesNotContain(To, entry.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Hello", entry.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("Reply STOP", entry.EvidenceJson, StringComparison.Ordinal);
        Assert.Contains("v1", entry.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dispatch_OnFailure_WritesNoAuditEntry()
    {
        _outbound.Next = OutboundMessageResult.Failed("Meta said no");
        var service = CreateService(SigningKey);

        await service.DispatchAsync(_orgId, _customerId, To);

        Assert.Empty(_context.ConsentAuditEntries);
    }

    [Fact]
    public async Task Dispatch_RecordsTheShownAndUnshownSignals()
    {
        _outbound.Next = OutboundMessageResult.Sent("wamid.DISCLOSURE1");
        var service = CreateService(SigningKey);
        await service.DispatchAsync(_orgId, _customerId, To);
        Assert.Equal(DisclosureSignals.Shown, _metrics.LastSignal);

        // A fresh customer whose send fails is the "unshown" signal.
        var otherCustomer = Guid.NewGuid();
        _consent.Rows[(_orgId, otherCustomer)] = new CustomerConsent
        {
            OrganizationId = _orgId,
            CustomerId = otherCustomer,
            ConsentStatus = ConsentStatuses.Pending,
        };
        _outbound.Next = OutboundMessageResult.Failed("Meta said no");
        await service.DispatchAsync(_orgId, otherCustomer, To);
        Assert.Equal(DisclosureSignals.Unshown, _metrics.LastSignal);
    }

    /// <summary>
    /// The consent store double. Its conditional claim mirrors the SQL contract: the first caller
    /// for a null <c>DisclosureShownAt</c> wins, every later caller gets <c>false</c>.
    /// </summary>
    private sealed class FakeConsentRepository : ICustomerConsentRepository
    {
        public Dictionary<(Guid OrganizationId, Guid CustomerId), CustomerConsent> Rows { get; } = [];

        public int ReleaseCalls { get; private set; }

        public Task<CustomerConsent?> GetForCustomerAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
            => Task.FromResult(Rows.GetValueOrDefault((orgId, customerId)));

        public Task<CustomerConsent> AddAsync(
            CustomerConsent consent, CancellationToken cancellationToken = default)
        {
            Rows[(consent.OrganizationId, consent.CustomerId)] = consent;
            return Task.FromResult(consent);
        }

        public Task SaveAsync(CustomerConsent consent, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task<bool> TryClaimDisclosureAsync(
            Guid orgId,
            Guid customerId,
            string disclosureVersion,
            DateTime shownAt,
            CancellationToken cancellationToken = default)
        {
            var row = Rows.GetValueOrDefault((orgId, customerId));
            if (row is null || row.DisclosureShownAt is not null)
            {
                return Task.FromResult(false);
            }

            row.DisclosureShownAt = shownAt;
            row.DisclosureVersion = disclosureVersion;
            return Task.FromResult(true);
        }

        public Task ReleaseDisclosureAsync(
            Guid orgId, Guid customerId, CancellationToken cancellationToken = default)
        {
            ReleaseCalls++;
            var row = Rows.GetValueOrDefault((orgId, customerId));
            if (row is not null)
            {
                row.DisclosureShownAt = null;
                row.DisclosureVersion = null;
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutboundMessaging : IOutboundMessagingService
    {
        public List<SentDisclosure> Sent { get; } = [];

        public OutboundMessageResult Next { get; set; } = OutboundMessageResult.Sent("wamid.1");

        public Task<OutboundMessageResult> SendWhatsAppTextAsync(
            Guid organizationId,
            string toE164,
            string text,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Sent.Add(new SentDisclosure(organizationId, toE164, text, idempotencyKey));
            return Task.FromResult(Next);
        }

        public Task<OutboundMessageResult> SendWhatsAppTemplateAsync(
            Guid organizationId,
            string toE164,
            string templateName,
            string languageCode,
            IReadOnlyList<object>? components,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<bool> IsChannelConfiguredAsync(
            Guid organizationId, string channelKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed record SentDisclosure(
        Guid OrganizationId, string ToE164, string Text, string IdempotencyKey);
}
