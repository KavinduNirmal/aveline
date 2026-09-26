using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Privacy.Metrics;
using Aveline.Api.Modules.Privacy.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.5 (plan §11 Phase 4, §15 Q-9): the single, non-personalised opt-out acknowledgement. Its
/// whole contract is "once per 24 hours, never through the agent, and never personal": two messages
/// inside the window must produce exactly one outbound message and one consent-history row.
/// </summary>
public class OptOutAcknowledgementServiceTests
{
    private const string Phone = "+94771234567";
    private static readonly Guid OrgId = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid CustomerId = Guid.Parse("33333333-3333-3333-3333-333333333333");

    private readonly FakeGate _gate = new();
    private readonly FakeOutbound _outbound = new();
    private readonly AppDbContext _context;

    public OptOutAcknowledgementServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OptOutAck_{Guid.NewGuid()}")
            .Options);
    }

    private OptOutAcknowledgementService CreateService(IOptOutAcknowledgementBodyBuilder? builder = null)
        => new(
            _gate,
            builder ?? new OptOutAcknowledgementBodyBuilder(),
            _outbound,
            _context,
            new PrivacyDeliveryMetrics(),
            NullLogger<OptOutAcknowledgementService>.Instance);

    [Fact]
    public async Task TwoSendsInsideTwentyFourHoursProduceExactlyOneAcknowledgement()
    {
        var service = CreateService();

        var first = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");
        var second = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        Assert.Equal(OptOutAcknowledgementOutcome.Sent, first.Outcome);
        Assert.Equal(OptOutAcknowledgementOutcome.AlreadyAcknowledged, second.Outcome);
        Assert.Single(_outbound.Sent);

        // One claim, and the window the gate enforces is 24 hours.
        Assert.Equal(1, _gate.Claims);
        Assert.Equal(TimeSpan.FromHours(24), DistributedOptOutAcknowledgementGate.Window);
    }

    [Fact]
    public async Task AfterTheWindowElapses_ASecondAcknowledgementIsSent()
    {
        var service = CreateService();

        await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");
        _gate.Advance(DistributedOptOutAcknowledgementGate.Window + TimeSpan.FromSeconds(1));
        var later = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        Assert.Equal(OptOutAcknowledgementOutcome.Sent, later.Outcome);
        Assert.Equal(2, _outbound.Sent.Count);
    }

    [Fact]
    public async Task AFailedSendReleasesTheGateSoARetryIsPossible()
    {
        _outbound.Next = OutboundMessageResult.Failed("Meta said no");
        var service = CreateService();

        var failed = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");
        Assert.Equal(OptOutAcknowledgementOutcome.Failed, failed.Outcome);
        Assert.Equal(1, _gate.Releases);

        // The retry succeeds inside the same window because the failed attempt did not keep the
        // claim: a provider outage delays the confirmation rather than losing it.
        _outbound.Next = OutboundMessageResult.Sent("wamid.ACK1");
        var retry = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        Assert.Equal(OptOutAcknowledgementOutcome.Sent, retry.Outcome);
        Assert.Single(_context.ConsentAuditEntries);
    }

    [Fact]
    public async Task AnUnconfiguredChannelIsReportedAsSuchAndReleasesTheGate()
    {
        _outbound.Next = OutboundMessageResult.NotConfigured("no credentials");
        var service = CreateService();

        var result = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        Assert.Equal(OptOutAcknowledgementOutcome.NotConfigured, result.Outcome);
        Assert.Equal(1, _gate.Releases);
        Assert.Empty(_context.ConsentAuditEntries);
    }

    [Fact]
    public async Task WhenTheGateStoreIsDownNothingIsSent()
    {
        // Fail closed: an outage must not turn "one acknowledgement" into "one per attempt".
        _gate.Unavailable = true;
        var service = CreateService();

        var result = await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        Assert.Equal(OptOutAcknowledgementOutcome.GateUnavailable, result.Outcome);
        Assert.Empty(_outbound.Sent);
    }

    [Fact]
    public async Task TheBodyIsNonPersonalisedAndTheAuditRowCarriesIdentifiersOnly()
    {
        var service = CreateService();

        await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        var sent = Assert.Single(_outbound.Sent);
        Assert.Equal(Phone, sent.ToE164);
        Assert.Contains("Emerald Boutique", sent.Text, StringComparison.Ordinal);
        Assert.Contains("opted out", sent.Text, StringComparison.OrdinalIgnoreCase);
        // No customer id, no name, no history, no phone number in the body.
        Assert.DoesNotContain(Phone, sent.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(CustomerId.ToString(), sent.Text, StringComparison.Ordinal);

        var audit = Assert.Single(_context.ConsentAuditEntries);
        Assert.Equal(AuditAction.ConsentRevokedAcknowledged, audit.Action);
        Assert.Equal("opt_out_ack", audit.Source);
        Assert.Equal(ConsentActorKinds.System, audit.ActorKind);
        Assert.DoesNotContain(Phone, audit.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("opted out", audit.EvidenceJson, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIdempotencyKeyNamesNoPhoneNumber()
    {
        var service = CreateService();

        await service.SendOnceAsync(OrgId, CustomerId, Phone, "Emerald Boutique");

        var sent = Assert.Single(_outbound.Sent);
        Assert.Equal(
            OptOutAcknowledgementService.BuildIdempotencyKey(OrgId, Phone), sent.IdempotencyKey);
        Assert.DoesNotContain(Phone, sent.IdempotencyKey, StringComparison.Ordinal);
    }

    private sealed class FakeGate : IOptOutAcknowledgementGate
    {
        private readonly HashSet<string> _claimed = [];
        private DateTimeOffset _now = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        private readonly Dictionary<string, DateTimeOffset> _claimedAt = [];

        public bool Unavailable { get; set; }

        public int Claims { get; private set; }

        public int Releases { get; private set; }

        public void Advance(TimeSpan delta) => _now += delta;

        private string Key(Guid organizationId, string phone) =>
            $"{organizationId:D}:{phone}";

        public Task<bool> TryClaimAsync(
            Guid organizationId, string phoneE164, CancellationToken cancellationToken = default)
        {
            if (Unavailable)
            {
                throw new OtpStoreUnavailableException(new InvalidOperationException("down"));
            }

            var key = Key(organizationId, phoneE164);
            if (_claimedAt.TryGetValue(key, out var at)
                && _now - at < DistributedOptOutAcknowledgementGate.Window)
            {
                return Task.FromResult(false);
            }

            Claims++;
            _claimedAt[key] = _now;
            _claimed.Add(key);
            return Task.FromResult(true);
        }

        public Task ReleaseAsync(
            Guid organizationId, string phoneE164, CancellationToken cancellationToken = default)
        {
            Releases++;
            _claimedAt.Remove(Key(organizationId, phoneE164));
            return Task.CompletedTask;
        }
    }

    private sealed class FakeOutbound : IOutboundMessagingService
    {
        public List<(Guid OrganizationId, string ToE164, string Text, string IdempotencyKey)> Sent { get; } = [];

        public OutboundMessageResult Next { get; set; } = OutboundMessageResult.Sent("wamid.ACK1");

        public Task<OutboundMessageResult> SendWhatsAppTextAsync(
            Guid organizationId,
            string toE164,
            string text,
            string idempotencyKey,
            CancellationToken cancellationToken = default)
        {
            Sent.Add((organizationId, toE164, text, idempotencyKey));
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
            => throw new NotSupportedException("The acknowledgement must never use the template path (Q-2).");

        public Task<bool> IsChannelConfiguredAsync(
            Guid organizationId, string channelKey, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }
}
