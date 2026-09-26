using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
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
/// The joined-up <c>start → verify</c> test the phase was missing (privacy plan §5.2/§5.3).
///
/// <para>
/// The bug this file exists to prevent: <c>start</c> minted the OTP handle and then threw it away,
/// returning only <c>{"status":"accepted"}</c>. Every verify test recovered the handle out of the
/// outbound-message log's idempotency key (<c>otp:{org}:{handle}</c>) - a seam no browser or mobile
/// client has - so start and verify each passed their own suite while the flow was impossible to
/// complete. This test takes the handle from the one place a real client can: the <c>start</c>
/// response, and it takes the code from the one place a real client can: the WhatsApp message. If
/// either value is not on that wire, the test fails before it can claim success.
/// </para>
/// </summary>
public class PrivacyOptOutEndToEndTests : IAsyncLifetime
{
    private const string EncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="; // 32 bytes
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";
    private const string Phone = "+94771234567";
    private const string UnknownPhone = "+94779999999";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly StubWhatsAppService _whatsApp = new();

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
                IsSuccess: true, MessageId: $"wamid.E2E{SendCalls}", HttpStatus: 200));
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
    /// The acknowledgement dispatch is irrelevant to this test (it is covered by
    /// <c>PrivacyOptOutVerifyTests</c>) and the production queue would need the worker's backing
    /// store, so it is accepted and dropped.
    /// </summary>
    private sealed class DroppingQueue : IDisclosureDispatchQueue
    {
        public ValueTask<bool> EnqueueAsync(
            DisclosureIntent intent, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);

        public ValueTask<bool> EnqueueAcknowledgementAsync(
            OptOutAcknowledgementIntent intent, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(true);
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
                    services.AddDistributedMemoryCache();
                    services.RemoveAll<IDisclosureDispatchQueue>();
                    services.AddSingleton<IDisclosureDispatchQueue>(new DroppingQueue());
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
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    /// <summary>Seeds a boutique with WhatsApp connected.</summary>
    private static async Task<Guid> SeedOrganizationAsync(string suffix)
    {
        await using var context = CreateContext();

        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"e2e_owner_{suffix}",
            Email = $"e2e_owner_{suffix}@aveline.lk",
            FirstName = "End",
            LastName = "ToEnd",
            Username = $"e2e_owner_{suffix}",
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

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = EncryptionKey,
            })
            .Build();
        var encryption = new CredentialEncryptionService(config);
        var json = JsonSerializer.Serialize(new Dictionary<string, string>
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

    /// <summary>Puts the customer on file with a pending consent row.</summary>
    private static async Task<Guid> SeedCustomerAsync(Guid orgId, string phone)
    {
        await using var context = CreateContext();
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phone,
            FullName = "Sarah Perera",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            ConsentStatus = ConsentStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
        return customer.Id;
    }

    /// <summary>Builds the body a client sends, including the signed-link fields.</summary>
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

    [Fact]
    public async Task AClientCanCompleteTheOptOutUsingOnlyTheStartResponseAndTheDeliveredCode()
    {
        // The acceptance criterion. Everything this test uses to verify is something a real browser
        // or mobile client can actually see: the signed link, the phone it typed, the handle in the
        // start response, and the code in the WhatsApp message. If start discards the handle, the
        // reply has no `handle` member and this fails here, not silently in production.
        var orgId = await SeedOrganizationAsync("complete");
        var customerId = await SeedCustomerAsync(orgId, Phone);

        var start = await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/start",
            SignedBody(orgId, new { phoneNumber = Phone, scope = "org" }));

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(
            startBody.TryGetProperty("handle", out var handleProperty)
            && handleProperty.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(handleProperty.GetString()),
            $"the start response must carry the opaque handle a client returns on verify; "
            + $"the body was {startBody.GetRawText()}");
        var handle = handleProperty.GetString()!;

        // The plaintext code exists only in the delivered message; no other client-visible source.
        var message = Assert.Single(_whatsApp.Sent);
        Assert.Equal(Phone, message.To);
        var code = Regex.Match(message.Text, @"\b(\d{6})\b");
        Assert.True(code.Success, $"no six-digit code in the OTP body: {message.Text}");

        var verify = await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/verify",
            SignedBody(orgId, new { otp = code.Groups[1].Value, handle, scope = "org" }));

        Assert.Equal(HttpStatusCode.OK, verify.StatusCode);
        var verifyBody = await verify.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("revoked", verifyBody.GetProperty("status").GetString());

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Revoked,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId)).ConsentStatus);
    }

    [Fact]
    public async Task AnUnknownPhonesHandleHasNoStoredCodeAndCannotBeVerifiedIntoARevocation()
    {
        // The other half of the contract: a handle is returned on every path, but for a number the
        // boutique does not know it leads to no stored code. Nothing is minted or sent, and the six
        // digits cannot succeed because there is no digest under this handle at all.
        var orgId = await SeedOrganizationAsync("unknown");
        var onFileCustomer = await SeedCustomerAsync(orgId, Phone);

        var start = await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/start",
            SignedBody(orgId, new { phoneNumber = UnknownPhone, scope = "org" }));

        Assert.Equal(HttpStatusCode.Accepted, start.StatusCode);
        var startBody = await start.Content.ReadFromJsonAsync<JsonElement>();
        var handle = startBody.GetProperty("handle").GetString();
        Assert.False(string.IsNullOrWhiteSpace(handle));

        Assert.Equal(0, _whatsApp.SendCalls);
        Assert.Empty(_whatsApp.Sent);

        var verify = await _client.PostAsJsonAsync(
            "/api/v1/privacy/opt-out/verify",
            SignedBody(orgId, new { otp = "000000", handle, scope = "org" }));

        Assert.Equal(HttpStatusCode.BadRequest, verify.StatusCode);
        Assert.Contains(
            "otp-invalid", await verify.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        await using var context = CreateContext();
        Assert.Equal(
            ConsentStatuses.Pending,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == onFileCustomer)).ConsentStatus);
    }
}
