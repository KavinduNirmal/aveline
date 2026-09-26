using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Aveline.Api.Modules.Audit.Models;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.3 (plan §5.2, §5.4, §11 Phase 4): <c>POST /api/v1/privacy/opt-out/verify</c>. Two properties
/// carry the compliance weight:
///
/// <list type="bullet">
///   <item><c>scope=org</c> revokes exactly one organisation's row and <c>scope=all</c> revokes every
///     organisation's row for the proven phone, each writing consent + generic audit rows;</item>
///   <item>every failed verification - unknown handle, expired code, replay, wrong code, spent
///     attempts - answers the <b>same</b> <c>400 otp-invalid</c>.</item>
/// </list>
///
/// Item 4.5 is covered here too: the acknowledgement is enqueued by <c>verify</c> and must be sent
/// <b>once per 24 hours</b>.
/// </summary>
public class PrivacyOptOutVerifyTests : IAsyncLifetime
{
    private const string EncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=";
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";
    private const string Phone = "+94771234567";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly StubWhatsAppService _whatsApp = new();
    private readonly FaultingCache _cache = new();
    private readonly InlinePrivacyQueue _queue = new();

    private sealed class StubWhatsAppService : IWhatsAppService
    {
        public int SendCalls { get; private set; }

        public List<(string To, string Text)> Sent { get; } = [];

        public Task<WhatsAppTestResult> TestConnectionAsync(
            string accessToken, string phoneNumberId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppTestResult(IsValid: true));

        public Task<WhatsAppMediaResult> GetMediaAsync(
            string accessToken, string mediaId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppMediaResult(IsSuccess: false, Error: "not used"));

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text,
            CancellationToken cancellationToken = default)
        {
            SendCalls++;
            Sent.Add((to, text));
            return Task.FromResult(new WhatsAppSendResult(
                IsSuccess: true, MessageId: $"wamid.PRIVACY{SendCalls}", HttpStatus: 200));
        }

        public Task<WhatsAppSendResult> SendTemplateAsync(
            string accessToken, string phoneNumberId, string to, string templateName,
            string languageCode, IReadOnlyList<object>? components,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class NoopAgentClient : IAgentServiceClient
    {
        public Task<HttpResponseMessage> PostAsync(
            string path, HttpContent content, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

        public Task<HttpResponseMessage> GetAsync(
            string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }

    private sealed class NoopBroadcaster : IMessageBroadcaster
    {
        public Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BroadcastConversationChangedAsync(ConversationTile tile, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class AllowAllRateLimiter : IRateLimiter
    {
        public Task<bool> TryAllowAsync(
            string scopeKey, int limit, TimeSpan window, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
    }

    private sealed class FaultingCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _store = new();

        public bool IsDown { get; set; }

        private void Guard()
        {
            if (IsDown)
            {
                throw new InvalidOperationException("Redis is unavailable.");
            }
        }

        public byte[]? Get(string key)
        {
            Guard();
            return _store.TryGetValue(key, out var value) ? value : null;
        }

        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
            => Task.FromResult(Get(key));

        public void Set(string key, byte[] value, DistributedCacheEntryOptions options)
        {
            Guard();
            _store[key] = value;
        }

        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            Set(key, value, options);
            return Task.CompletedTask;
        }

        public void Refresh(string key) => Guard();

        public Task RefreshAsync(string key, CancellationToken token = default)
        {
            Guard();
            return Task.CompletedTask;
        }

        public void Remove(string key)
        {
            Guard();
            _store.TryRemove(key, out _);
        }

        public Task RemoveAsync(string key, CancellationToken token = default)
        {
            Remove(key);
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Runs acknowledgement intents inline through the same scoped service the worker would resolve,
    /// so "two opt-outs within 24h produce one acknowledgement" is deterministic without polling the
    /// production channel.
    /// </summary>
    private sealed class InlinePrivacyQueue : IDisclosureDispatchQueue
    {
        public List<OptOutAcknowledgementIntent> Enqueued { get; } = [];

        public Func<OptOutAcknowledgementIntent, Task>? Dispatch { get; set; }

        public async ValueTask<bool> EnqueueAsync(
            DisclosureIntent intent, CancellationToken cancellationToken = default)
            => await ValueTask.FromResult(true);

        public async ValueTask<bool> EnqueueAcknowledgementAsync(
            OptOutAcknowledgementIntent intent, CancellationToken cancellationToken = default)
        {
            Enqueued.Add(intent);
            if (Dispatch is not null)
            {
                await Dispatch(intent);
            }

            return true;
        }
    }

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:59999");
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Observability:AgentIsCritical", "false");
                builder.UseSetting("Credentials:EncryptionKey", EncryptionKey);
                builder.UseSetting("Privacy:LinkSigningKey", PrivacyKey);
                builder.UseSetting("App:BaseUrl", "https://app.aveline.lk");
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IWhatsAppService>();
                    services.AddSingleton<IWhatsAppService>(_whatsApp);
                    services.RemoveAll<IAgentServiceClient>();
                    services.AddSingleton<IAgentServiceClient>(new NoopAgentClient());
                    services.RemoveAll<IMessageBroadcaster>();
                    services.AddSingleton<IMessageBroadcaster>(new NoopBroadcaster());
                    services.RemoveAll<IRateLimiter>();
                    services.AddSingleton<IRateLimiter>(new AllowAllRateLimiter());
                    services.RemoveAll<IDistributedCache>();
                    services.AddSingleton<IDistributedCache>(_cache);
                    services.RemoveAll<IDisclosureDispatchQueue>();
                    services.AddSingleton<IDisclosureDispatchQueue>(_queue);
                });
            });

        _client = _factory.CreateClient();

        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        _queue.Dispatch = intent =>
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IOptOutAcknowledgementService>();
            return service.SendOnceAsync(
                intent.OrganizationId, intent.CustomerId, intent.ToE164, intent.OrganizationName);
        };

        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private static async Task<Guid> SeedOrganizationAsync(string suffix)
    {
        await using var context = CreateContext();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"verify_owner_{suffix}",
            Email = $"verify_owner_{suffix}@aveline.lk",
            FirstName = "Verify",
            LastName = "Owner",
            Username = $"verify_owner_{suffix}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = $"Emerald {suffix}",
            Slug = $"emerald-{suffix}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = EncryptionKey,
            })
            .Build();
        var encryption = new CredentialEncryptionService(config);
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["accessToken"] = "wa-token",
            ["phoneNumberId"] = "111",
            ["appSecret"] = AppSecret,
            ["webhookVerifyToken"] = VerifyToken,
        });
        context.IntegrationCredentials.Add(new IntegrationCredential
        {
            OrganizationId = org.Id,
            IntegrationType = IntegrationType.WhatsApp,
            EncryptedValue = encryption.Encrypt(json, $"{org.Id}:{IntegrationType.WhatsApp}"),
            Status = IntegrationStatus.Connected,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return org.Id;
    }

    private static async Task<Guid> SeedCustomerAsync(
        Guid orgId, string phone, string status = ConsentStatuses.Pending, string? name = null)
    {
        await using var context = CreateContext();
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phone,
            FullName = name ?? "Sarah Perera",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            ConsentStatus = status,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private object SignedBody(Guid orgId, object extra)
    {
        var signer = _factory.Services.GetRequiredService<IPrivacyLinkSigner>();
        var properties = new Dictionary<string, object?>
        {
            ["organizationId"] = orgId,
            ["version"] = "1",
            ["signature"] = signer.Sign(orgId, "1"),
        };

        foreach (var property in extra.GetType().GetProperties())
        {
            properties[property.Name] = property.GetValue(extra);
        }

        return properties;
    }

    private object StartBody(Guid orgId, string phone, string scope = "org") => SignedBody(orgId, new
    {
        phoneNumber = phone,
        scope,
    });

    private async Task<HttpResponseMessage> StartAsync(Guid orgId, string phone, string scope = "org")
        => await _client.PostAsJsonAsync("/api/v1/privacy/opt-out/start", StartBody(orgId, phone, scope));

    private async Task<HttpResponseMessage> VerifyAsync(
        Guid orgId, string? handle, string? otp, string scope = "org")
        => await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/verify", SignedBody(orgId, new { otp, handle, scope }));

    /// <summary>
    /// Starts a flow and reads the code the provider was given. The provider is the only place the
    /// plaintext code exists in a test, which is itself the point of the storage design.
    /// </summary>
    private async Task<(string Handle, string Code)> StartAndReadOtpAsync(Guid orgId, string phone)
    {
        var before = _whatsApp.Sent.Count;
        var response = await StartAsync(orgId, phone);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.True(_whatsApp.Sent.Count > before, "no OTP message was sent");
        var text = _whatsApp.Sent[^1].Text;
        var match = System.Text.RegularExpressions.Regex.Match(text, @"\b(\d{6})\b");
        Assert.True(match.Success, $"no six-digit code in the OTP body: {text}");

        await using var context = CreateContext();
        var handle = await context.InboundMessageLogs
            .Where(l => l.OrganizationId == orgId && l.Direction == "outbound")
            .OrderByDescending(l => l.ReceivedAt)
            .Select(l => l.ExternalId!)
            .FirstAsync();
        // The idempotency key is `otp:{org}:{handle}`.
        return (handle.Split(':')[^1], match.Groups[1].Value);
    }

    [Fact]
    public async Task ScopeOrgRevokesExactlyOneOrganizationsRowAndWritesBothAuditRows()
    {
        var orgA = await SeedOrganizationAsync("orgA");
        var orgB = await SeedOrganizationAsync("orgB");
        var customerA = await SeedCustomerAsync(orgA, Phone);
        var customerB = await SeedCustomerAsync(orgB, Phone);

        var (handle, code) = await StartAndReadOtpAsync(orgA, Phone);
        var response = await VerifyAsync(orgA, handle, code, "org");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("revoked", body.GetProperty("status").GetString());
        Assert.Equal("org", body.GetProperty("scope").GetString());

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Revoked,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerA)).ConsentStatus);
        Assert.Equal(
            ConsentStatuses.Pending,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerB)).ConsentStatus);

        // The consent history for this customer carries the revocation row (and, once the
        // acknowledgement is dispatched, an acknowledgement row); pick the one under test.
        var consentAudit = await context.ConsentAuditEntries
            .FirstAsync(a => a.CustomerId == customerA && a.Action == AuditAction.ConsentRevoked);
        Assert.Equal(ConsentActorKinds.Customer, consentAudit.ActorKind);
        Assert.Equal("otp_link", consentAudit.Source);

        Assert.True(await context.AuditLogEntries.AnyAsync(
            a => a.OrganizationId == orgA && a.Action == AuditAction.OtpVerified));
        Assert.True(await context.AuditLogEntries.AnyAsync(
            a => a.OrganizationId == orgA && a.Action == AuditAction.ConsentRevoked));
    }

    [Fact]
    public async Task ScopeAllRevokesEveryOrganizationsRowForTheProvenPhone()
    {
        var orgA = await SeedOrganizationAsync("allA");
        var orgB = await SeedOrganizationAsync("allB");
        var customerA = await SeedCustomerAsync(orgA, Phone);
        var customerB = await SeedCustomerAsync(orgB, Phone);
        var unrelated = await SeedCustomerAsync(orgB, "+94770000000");

        var (handle, code) = await StartAndReadOtpAsync(orgA, Phone);
        var response = await VerifyAsync(orgA, handle, code, "all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        Assert.Equal("all", body.GetProperty("scope").GetString());

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Revoked,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerA)).ConsentStatus);
        Assert.Equal(
            ConsentStatuses.Revoked,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerB)).ConsentStatus);
        // A different phone in the same organisation is untouched.
        Assert.Equal(
            ConsentStatuses.Pending,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == unrelated)).ConsentStatus);

        Assert.True(await context.ConsentAuditEntries.AnyAsync(a => a.CustomerId == customerA));
        Assert.True(await context.ConsentAuditEntries.AnyAsync(a => a.CustomerId == customerB));
    }

    [Fact]
    public async Task AWrongCodeIsRejectedWithTheSameOtpInvalidBodyAndChangesNoConsent()
    {
        var orgId = await SeedOrganizationAsync("wrong");
        var customerId = await SeedCustomerAsync(orgId, Phone);

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var wrong = code == "000000" ? "111111" : "000000";

        var response = await VerifyAsync(orgId, handle, wrong, "org");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("""{"code":"otp-invalid","message":"The code is invalid, expired or was already used."}""",
            await response.Content.ReadAsStringAsync());

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Pending,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId)).ConsentStatus);
    }

    [Fact]
    public async Task AnUnknownHandleAndAReplayedCodeAreRejectedWithTheIdenticalBody()
    {
        var orgId = await SeedOrganizationAsync("replay");
        await SeedCustomerAsync(orgId, Phone);

        var unknown = await VerifyAsync(orgId, "bm90LWEtaGFuZGxl", "123456", "org");

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var first = await VerifyAsync(orgId, handle, code, "org");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var replay = await VerifyAsync(orgId, handle, code, "org");

        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, replay.StatusCode);
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await replay.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task TheSixthVerificationIsRejectedEvenWithTheCorrectCode()
    {
        var orgId = await SeedOrganizationAsync("attempts");
        var customerId = await SeedCustomerAsync(orgId, Phone);

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var wrong = code == "000000" ? "111111" : "000000";

        for (var attempt = 0; attempt < OtpService.MaxAttempts; attempt++)
        {
            var failed = await VerifyAsync(orgId, handle, wrong, "org");
            Assert.Equal(HttpStatusCode.BadRequest, failed.StatusCode);
        }

        var sixth = await VerifyAsync(orgId, handle, code, "org");

        Assert.Equal(HttpStatusCode.BadRequest, sixth.StatusCode);
        Assert.Contains("otp-invalid", await sixth.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Pending,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId)).ConsentStatus);
    }

    [Fact]
    public async Task WhenTheStoreIsDown_VerifyAnswers503AndDoesNotConsumeTheCode()
    {
        var orgId = await SeedOrganizationAsync("down");
        var customerId = await SeedCustomerAsync(orgId, Phone);

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);

        _cache.IsDown = true;
        var refused = await VerifyAsync(orgId, handle, code, "org");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, refused.StatusCode);

        // The refused call neither revoked nor burned the code.
        await using (var context = CreateContext())
        {
            Assert.Equal(
                ConsentStatuses.Pending,
                (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId)).ConsentStatus);
        }

        _cache.IsDown = false;
        var retry = await VerifyAsync(orgId, handle, code, "org");

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        await using (var context = CreateContext())
        {
            Assert.Equal(
                ConsentStatuses.Revoked,
                (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId)).ConsentStatus);
        }
    }

    [Fact]
    public async Task AnUnsignedVerifyRequestIsRejectedWithTheOtpInvalidBody()
    {
        var orgId = await SeedOrganizationAsync("nosig");
        var customerId = await SeedCustomerAsync(orgId, Phone);
        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);

        var response = await _client.PostAsJsonAsync("/api/v1/privacy/opt-out/verify", new
        {
            organizationId = orgId,
            handle,
            otp = code,
            scope = "org",
            version = "1",
            signature = "forged",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("otp-invalid", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Pending,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId)).ConsentStatus);
    }

    [Fact]
    public async Task ARolledBackAttemptStillSendsExactlyOneAcknowledgementPerTwentyFourHours()
    {
        // Item 4.5: two revocations inside the window produce one acknowledgement, and the message
        // never re-enters the agent path (it is built here, not by the agent service).
        var orgId = await SeedOrganizationAsync("ack");
        await SeedCustomerAsync(orgId, Phone);

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        var first = await VerifyAsync(orgId, handle, code, "org");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Single(_queue.Enqueued);
        Assert.Equal(1, _whatsApp.Sent.Count(m => m.Text.Contains("opted out", StringComparison.Ordinal)));

        // A second request inside the window: a fresh code, then the revocation again.
        var (handle2, code2) = await StartAndReadOtpAsync(orgId, Phone);
        var second = await VerifyAsync(orgId, handle2, code2, "org");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        Assert.Equal(
            1,
            _whatsApp.Sent.Count(m => m.Text.Contains("opted out", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TheAcknowledgementIsNonPersonalisedAndWrittenToTheConsentHistory()
    {
        var orgId = await SeedOrganizationAsync("ackaudit");
        var customerId = await SeedCustomerAsync(orgId, Phone, name: "Sarah Perera");

        var (handle, code) = await StartAndReadOtpAsync(orgId, Phone);
        await VerifyAsync(orgId, handle, code, "org");

        var acknowledgement = Assert.Single(
            _whatsApp.Sent.Where(m => m.Text.Contains("opted out", StringComparison.Ordinal)));
        Assert.DoesNotContain("Sarah", acknowledgement.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(customerId.ToString(), acknowledgement.Text, StringComparison.Ordinal);

        await using var context = CreateContext();
        var audit = await context.ConsentAuditEntries.SingleAsync(
            a => a.CustomerId == customerId && a.Action == AuditAction.ConsentRevokedAcknowledged);
        Assert.Equal("opt_out_ack", audit.Source);
        // Identifiers only: no phone number, no body text.
        Assert.DoesNotContain(Phone, audit.EvidenceJson, StringComparison.Ordinal);
        Assert.DoesNotContain("opted out", audit.EvidenceJson, StringComparison.Ordinal);
    }
}
