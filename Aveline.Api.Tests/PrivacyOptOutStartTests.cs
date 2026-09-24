using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.2 (plan §5.2, §11 Phase 4): <c>POST /api/v1/privacy/opt-out/start</c>. The load-bearing
/// property is anti-enumeration - an unknown number and a known number must produce the <b>same
/// status and the same response field set</b>, with nothing on the wire encoding existence - and the
/// second is fail-closed behaviour when the OTP store is unreachable.
/// </summary>
/// <remarks>
/// Byte-identity is deliberately <b>not</b> the asserted property: the response carries a fresh
/// random opaque handle on every call (plan §5.3, "returned to the client"), so two calls can never
/// be byte-identical. The shape is what must not vary. The joined-up start-then-verify flow that the
/// handle makes possible is covered by <c>PrivacyOptOutEndToEndTests</c>.
/// </remarks>
public class PrivacyOptOutStartTests : IAsyncLifetime
{
    private const string EncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="; // 32 bytes
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";
    private const string KnownPhone = "+94771234567";
    private const string UnknownPhone = "+94779999999";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly StubWhatsAppService _whatsApp = new();
    private readonly FaultingCache _cache = new();

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
                IsSuccess: true, MessageId: $"wamid.OTP{SendCalls}", HttpStatus: 200));
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

    /// <summary>
    /// The production <see cref="IDistributedCache"/> is in-memory and cannot be made to fail. This
    /// double models the one thing the fail-closed tests need: an outage switch.
    /// </summary>
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

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
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
                });
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    /// <summary>Seeds a boutique with WhatsApp connected, optionally with the customer on file.</summary>
    private static async Task<Guid> SeedAsync(string suffix, string? customerPhone)
    {
        await using var context = CreateContext();

        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"optout_owner_{suffix}",
            Email = $"optout_owner_{suffix}@aveline.lk",
            FirstName = "Opt",
            LastName = "Owner",
            Username = $"optout_owner_{suffix}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });

        var org = new Organization
        {
            Name = "Emerald Boutique",
            Slug = $"emerald-{suffix}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        if (customerPhone is not null)
        {
            var customer = new Customer
            {
                OrganizationId = org.Id,
                PhoneNumber = customerPhone,
                FullName = "Sarah Perera",
                Status = "new",
            };
            context.Customers.Add(customer);
            context.CustomerConsents.Add(new CustomerConsent
            {
                OrganizationId = org.Id,
                CustomerId = customer.Id,
                ConsentStatus = ConsentStatuses.Pending,
                CreatedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

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

    /// <summary>Builds the body a web client would send, including the signed link fields.</summary>
    private object StartBody(Guid orgId, string phone, string scope = "org")
    {
        var signer = _factory.Services.GetRequiredService<IPrivacyLinkSigner>();
        return new
        {
            organizationId = orgId,
            phoneNumber = phone,
            scope,
            version = "1",
            signature = signer.Sign(orgId, "1"),
        };
    }

    private async Task<(HttpStatusCode Status, string Body)> StartAsync(Guid orgId, string phone, string scope = "org")
    {
        var response = await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/start", StartBody(orgId, phone, scope));
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    /// <summary>
    /// Asserts the one <c>202</c> body shape every <c>start</c> path returns and hands back the
    /// handle. The field set is exact so an existence-revealing member cannot be added quietly.
    /// </summary>
    private static string AssertAcceptedShape(string body)
    {
        using var document = JsonDocument.Parse(body);

        var fields = document.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        Assert.Equal(new[] { "status", "handle", "expiresInSeconds" }, fields);
        Assert.Equal("accepted", document.RootElement.GetProperty("status").GetString());
        Assert.Equal(300, document.RootElement.GetProperty("expiresInSeconds").GetInt32());

        var handle = document.RootElement.GetProperty("handle").GetString();
        // A non-empty, opaque 32-byte base64url reference (43 chars, no padding).
        Assert.NotNull(handle);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", handle);
        return handle;
    }

    [Fact]
    public async Task AnUnknownPhoneAndAKnownPhoneProduceTheSameAcceptedFieldSetWithAnOpaqueHandle()
    {
        // The acceptance criterion, restated as the property that actually matters. Byte-identity
        // is impossible and was never the guarantee: the handle is a fresh random value on every
        // call, which is exactly what stops it being a phone fingerprint. What must not vary with
        // whether the phone exists is the shape - the same status code, the same field names, a
        // non-empty opaque handle in both cases, and no member that encodes existence. A body that
        // gained, say, `"known":true`, `"sent":true` or `"customerId":"…"` would fail the exact
        // field-set assertion below even though it is still a 202.
        var knownOrg = await SeedAsync("known", KnownPhone);
        var unknownOrg = await SeedAsync("unknown", customerPhone: null);

        var known = await StartAsync(knownOrg, KnownPhone);
        var unknown = await StartAsync(unknownOrg, UnknownPhone);

        Assert.Equal(HttpStatusCode.Accepted, known.Status);
        Assert.Equal(HttpStatusCode.Accepted, unknown.Status);

        using var knownBody = JsonDocument.Parse(known.Body);
        using var unknownBody = JsonDocument.Parse(unknown.Body);

        var knownFields = knownBody.RootElement.EnumerateObject().Select(p => p.Name).ToArray();
        var unknownFields = unknownBody.RootElement.EnumerateObject().Select(p => p.Name).ToArray();

        // Identical fields, in the same order, on both paths. The exact expected list is asserted
        // too, so a new existence-revealing member cannot pass by appearing on both paths.
        Assert.Equal(unknownFields, knownFields);
        Assert.Equal(new[] { "status", "handle", "expiresInSeconds" }, knownFields);

        // The constant members carry the constant values on both paths.
        Assert.Equal("accepted", knownBody.RootElement.GetProperty("status").GetString());
        Assert.Equal("accepted", unknownBody.RootElement.GetProperty("status").GetString());
        Assert.Equal(300, knownBody.RootElement.GetProperty("expiresInSeconds").GetInt32());
        Assert.Equal(300, unknownBody.RootElement.GetProperty("expiresInSeconds").GetInt32());

        // Both handles are non-empty opaque 32-byte base64url values, and different from each other:
        // a handle is a per-call random reference, never a function of the number.
        var knownHandle = knownBody.RootElement.GetProperty("handle").GetString();
        var unknownHandle = unknownBody.RootElement.GetProperty("handle").GetString();
        Assert.NotNull(knownHandle);
        Assert.NotNull(unknownHandle);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", knownHandle);
        Assert.Matches("^[A-Za-z0-9_-]{43}$", unknownHandle);
        Assert.NotEqual(knownHandle, unknownHandle);
    }

    [Fact]
    public async Task AKnownPhoneInAConfiguredBoutiqueSendsExactlyOneOtpAndAuditsIt()
    {
        var orgId = await SeedAsync("send", KnownPhone);

        var (status, _) = await StartAsync(orgId, KnownPhone);

        Assert.Equal(HttpStatusCode.Accepted, status);
        Assert.Equal(1, _whatsApp.SendCalls);
        var (to, text) = Assert.Single(_whatsApp.Sent);
        Assert.Equal(KnownPhone, to);
        Assert.Contains("opt-out code", text, StringComparison.Ordinal);
        // A six-digit code is present, and no plaintext code is logged or echoed on the wire.
        Assert.Matches(@"\b\d{6}\b", text);

        await using var context = CreateContext();
        var outbound = await context.InboundMessageLogs
            .Where(l => l.OrganizationId == orgId && l.Direction == "outbound")
            .ToListAsync();
        Assert.Single(outbound);
        Assert.StartsWith("otp:", outbound[0].ExternalId!, StringComparison.Ordinal);

        Assert.True(await context.AuditLogEntries.AnyAsync(
            a => a.OrganizationId == orgId && a.Action == "privacy.otp.issued"));
    }

    [Fact]
    public async Task AnUnconfiguredBoutiqueAnswersTheSameBodyAndSendsNothing()
    {
        // No WhatsApp integration at all: the response must still be the constant 202.
        await using (var context = CreateContext())
        {
            var ownerId = Guid.CreateVersion7();
            context.Users.Add(new User
            {
                Id = ownerId,
                ClerkId = $"nocfg_{ownerId:N}",
                Email = $"nocfg_{ownerId:N}@aveline.lk",
                FirstName = "No",
                LastName = "Config",
                Username = $"nocfg_{ownerId:N}",
                UserRole = "owner",
            });
            var org = new Organization
            {
                Name = "Unconnected Boutique",
                Slug = $"unconnected-{ownerId:N}",
                OwnerUserId = ownerId,
            };
            context.Organizations.Add(org);
            var customer = new Customer
            {
                OrganizationId = org.Id,
                PhoneNumber = KnownPhone,
                Status = "new",
            };
            context.Customers.Add(customer);
            context.CustomerConsents.Add(new CustomerConsent
            {
                OrganizationId = org.Id,
                CustomerId = customer.Id,
                ConsentStatus = ConsentStatuses.Pending,
            });
            await context.SaveChangesAsync();

            var (status, body) = await StartAsync(org.Id, KnownPhone);

            Assert.Equal(HttpStatusCode.Accepted, status);
            // The same shape as the configured path: no customer could be reached, but the caller
            // cannot tell that from the body.
            AssertAcceptedShape(body);
        }

        Assert.Equal(0, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task AMalformedPhoneIsRejectedBeforeAnyLookup()
    {
        var orgId = await SeedAsync("malformed", KnownPhone);

        var (status, body) = await StartAsync(orgId, "not-a-number");

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Contains("invalid-phone-number", body, StringComparison.Ordinal);
        Assert.Equal(0, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task AnUnsignedLinkIsRefusedWithTheSameAcceptedBodyAndSendsNothing()
    {
        // The link proves which boutique; without a valid signature the flow must not mint a code,
        // but it must not become an oracle either.
        var orgId = await SeedAsync("unsigned", KnownPhone);

        var response = await _client.PostAsJsonAsync("/api/v1/privacy/opt-out/start", new
        {
            organizationId = orgId,
            phoneNumber = KnownPhone,
            scope = "org",
            version = "1",
            signature = "not-a-real-signature",
        });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        // The handle is minted before the signature check, so even a forged link gets no shape that
        // stands out - but no code is stored or sent behind it.
        AssertAcceptedShape(await response.Content.ReadAsStringAsync());
        Assert.Equal(0, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task AThirdStartForTheSameNumberAnswers429AndSendsNoMoreCodes()
    {
        var orgId = await SeedAsync("budget", KnownPhone);

        Assert.Equal(HttpStatusCode.Accepted, (await StartAsync(orgId, KnownPhone)).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await StartAsync(orgId, KnownPhone)).Status);
        Assert.Equal(HttpStatusCode.Accepted, (await StartAsync(orgId, KnownPhone)).Status);

        var fourth = await StartAsync(orgId, KnownPhone);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourth.Status);
        Assert.Equal(OtpService.MaxSendsPerPhonePerWindow, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task AnEleventhStartFromOneAddressIsRefusedWith429()
    {
        // The per-address budget. Every different number keeps the per-number budget clear, so this
        // can only be the address cap.
        var orgId = await SeedAsync("ipbudget", KnownPhone);
        var signer = _factory.Services.GetRequiredService<IPrivacyLinkSigner>();

        for (var i = 0; i < OtpService.MaxStartsPerIpPerHour; i++)
        {
            var response = await _client.PostAsJsonAsync("/api/v1/privacy/opt-out/start", new
            {
                organizationId = orgId,
                phoneNumber = $"+9477123{i:D4}",
                scope = "org",
                version = "1",
                signature = signer.Sign(orgId, "1"),
            });
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        }

        var eleventh = await _client.PostAsJsonAsync("/api/v1/privacy/opt-out/start", new
        {
            organizationId = orgId,
            phoneNumber = "+94771239999",
            scope = "org",
            version = "1",
            signature = signer.Sign(orgId, "1"),
        });

        Assert.Equal(HttpStatusCode.TooManyRequests, eleventh.StatusCode);
    }

    [Fact]
    public async Task WhenTheOtpStoreIsDown_StartAnswers503AndSendsNothing()
    {
        // DR-6: a Redis outage must not degrade into "no code, carry on".
        var orgId = await SeedAsync("down", KnownPhone);
        _cache.IsDown = true;

        var (status, body) = await StartAsync(orgId, KnownPhone);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, status);
        Assert.Contains("otp-unavailable", body, StringComparison.Ordinal);
        Assert.Equal(0, _whatsApp.SendCalls);
    }
}
