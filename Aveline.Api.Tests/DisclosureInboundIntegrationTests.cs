using System.Net;
using System.Security.Cryptography;
using System.Text;
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
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.5 (plan §4.1/§4.4): the inbound webhook is where the first message is known. The first
/// inbound message for an identified customer produces exactly one outbound disclosure; the second
/// produces none; a revoked customer produces none and is still recorded. The webhook must keep
/// answering 200 throughout.
/// </summary>
public class DisclosureInboundIntegrationTests : IAsyncLifetime
{
    private const string EncryptionKey = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA="; // 32 bytes
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly StubWhatsAppService _whatsApp = new();
    private readonly InlineDisclosureQueue _disclosureQueue = new();

    private sealed class StubWhatsAppService : IWhatsAppService
    {
        public int SendCalls { get; private set; }

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
            return Task.FromResult(new WhatsAppSendResult(
                IsSuccess: true, MessageId: $"wamid.OUT{SendCalls}", HttpStatus: 200));
        }

        public Task<WhatsAppSendResult> SendTemplateAsync(
            string accessToken, string phoneNumberId, string to, string templateName,
            string languageCode, IReadOnlyList<object>? components,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// The production queue is a channel drained by a background worker; a test cannot wait on that
    /// without polling. This double keeps the same enqueue contract but runs the real scoped
    /// disclosure service inline, so asserting on the outbound row is deterministic while still
    /// exercising the handler → queue → scoped-service path (the worker itself is covered by
    /// <see cref="DisclosureDispatchWorkerTests"/>).
    /// </summary>
    private sealed class InlineDisclosureQueue : IDisclosureDispatchQueue
    {
        private readonly List<DisclosureIntent> _enqueued = [];

        public Func<DisclosureIntent, Task<DisclosureDispatchResult>>? Dispatch { get; set; }

        public IReadOnlyList<DisclosureIntent> Enqueued => _enqueued;

        public async ValueTask<bool> EnqueueAsync(
            DisclosureIntent intent, CancellationToken cancellationToken = default)
        {
            _enqueued.Add(intent);
            if (Dispatch is not null)
            {
                await Dispatch(intent);
            }

            return true;
        }

        /// <summary>
        /// Not exercised by the disclosure tests. The acknowledgement intent is drained by the same
        /// worker, so the double accepts it and dispatches inline when a handler is configured.
        /// </summary>
        public async ValueTask<bool> EnqueueAcknowledgementAsync(
            OptOutAcknowledgementIntent intent, CancellationToken cancellationToken = default)
        {
            if (AcknowledgementDispatch is not null)
            {
                await AcknowledgementDispatch(intent);
            }

            return true;
        }

        public Func<OptOutAcknowledgementIntent, Task<OptOutAcknowledgementResult>>?
            AcknowledgementDispatch { get; set; }
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
                    services.RemoveAll<IDisclosureDispatchQueue>();
                    services.AddSingleton<IDisclosureDispatchQueue>(_disclosureQueue);
                });
            });

        _client = _factory.CreateClient();

        // The double dispatches through the same DI scope the worker would create, so the exact
        // production service is exercised rather than a fake.
        var scopeFactory = _factory.Services.GetRequiredService<IServiceScopeFactory>();
        _disclosureQueue.Dispatch = intent =>
        {
            using var scope = scopeFactory.CreateScope();
            var service = scope.ServiceProvider.GetRequiredService<IDisclosureDispatchService>();
            return service.DispatchAsync(intent.OrganizationId, intent.CustomerId, intent.ToE164);
        };
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private static string Sign(byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static string MetaMessage(string messageId, string from) =>
        $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            { "id": "WABA_ID", "changes": [ { "value": {
              "messaging_product": "whatsapp",
              "metadata": { "display_phone_number": "15551234567", "phone_number_id": "111" },
              "contacts": [ { "profile": { "name": "Customer" }, "wa_id": "{{from}}" } ],
              "messages": [ { "from": "{{from}}", "id": "{{messageId}}", "timestamp": "1720000000",
                "text": { "body": "Hi, do you have this in red?" }, "type": "text" } ]
            }, "field": "messages" } ] }
          ]
        }
        """;

    private async Task<HttpResponseMessage> PostAsync(Guid orgId, string messageId, string from)
    {
        var bytes = Encoding.UTF8.GetBytes(MetaMessage(messageId, from));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));
        return await _client.SendAsync(request);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    private static async Task<(Guid OrgId, Guid CustomerId)> SeedAsync(string consentStatus, string phone)
    {
        await using var context = CreateContext();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"disclosure_{ownerId:N}",
            Email = $"disclosure_{ownerId:N}@aveline.lk",
            FirstName = "Disclosure",
            LastName = "Owner",
            Username = $"disclosure_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Emerald Boutique",
            Slug = $"emerald-{ownerId:N}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = phone,
            FullName = "Sarah Perera",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConsentStatus = consentStatus,
            CreatedAt = DateTime.UtcNow,
        });

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

        return (org.Id, customer.Id);
    }

    [Fact]
    public async Task TheFirstInboundMessageProducesExactlyOneOutboundDisclosureAndTheSecondProducesNone()
    {
        var (orgId, customerId) = await SeedAsync(ConsentStatuses.Pending, "+94771234567");

        var first = await PostAsync(orgId, "wamid.FIRST", "+94771234567");

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        // One intent, and therefore one provider send.
        Assert.Single(_disclosureQueue.Enqueued);
        Assert.Equal((orgId, customerId), (_disclosureQueue.Enqueued[0].OrganizationId, _disclosureQueue.Enqueued[0].CustomerId));
        Assert.Equal(1, _whatsApp.SendCalls);

        await using (var context = CreateContext())
        {
            var outbound = await context.InboundMessageLogs
                .Where(l => l.OrganizationId == orgId && l.Direction == "outbound")
                .ToListAsync();
            Assert.Single(outbound);
            Assert.StartsWith("disclosure:", outbound[0].ExternalId!, StringComparison.Ordinal);
            // The body is the disclosure, not the staff welcome.
            Assert.Contains("Aveline, an AI assistant", outbound[0].Content!, StringComparison.Ordinal);
            Assert.Contains("Reply STOP", outbound[0].Content!, StringComparison.Ordinal);

            var consent = await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId);
            Assert.NotNull(consent.DisclosureShownAt);
            Assert.Equal("v1", consent.DisclosureVersion);

            // The inbound message itself was still recorded (the webhook's own contract).
            Assert.True(await context.InboundMessageLogs.AnyAsync(
                l => l.OrganizationId == orgId && l.Direction == "inbound" && l.ExternalId == "wamid.FIRST"));
        }

        var second = await PostAsync(orgId, "wamid.SECOND", "+94771234567");

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        // The gate still enqueues per inbound message, but the claim refuses the second dispatch, so
        // the customer is not messaged twice.
        Assert.Equal(1, _whatsApp.SendCalls);

        await using (var context = CreateContext())
        {
            Assert.Equal(1, await context.InboundMessageLogs.CountAsync(
                l => l.OrganizationId == orgId && l.Direction == "outbound"));
            Assert.True(await context.InboundMessageLogs.AnyAsync(
                l => l.OrganizationId == orgId && l.Direction == "inbound" && l.ExternalId == "wamid.SECOND"));
        }
    }

    [Fact]
    public async Task ARevokedCustomerProducesNoDisclosureAndTheMessageIsStillRecorded()
    {
        var (orgId, customerId) = await SeedAsync(ConsentStatuses.Revoked, "+94771239999");

        var response = await PostAsync(orgId, "wamid.REVOKED", "+94771239999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_disclosureQueue.Enqueued);
        Assert.Equal(0, _whatsApp.SendCalls);

        await using var context = CreateContext();
        Assert.True(await context.InboundMessageLogs.AnyAsync(
            l => l.OrganizationId == orgId && l.Direction == "inbound" && l.ExternalId == "wamid.REVOKED"));
        Assert.False(await context.InboundMessageLogs.AnyAsync(
            l => l.OrganizationId == orgId && l.Direction == "outbound"));
        var consent = await context.CustomerConsents.SingleAsync(c => c.CustomerId == customerId);
        Assert.Null(consent.DisclosureShownAt);
    }

    [Fact]
    public async Task AnUnknownNumberProducesNoDisclosureButStillReturns200AndRecordsTheMessage()
    {
        // No customer row for this number, so there is no consent row to stamp. The message must
        // still be accepted and recorded; the disclosure follows once the number is identified.
        var (orgId, _) = await SeedAsync(ConsentStatuses.Pending, "+94770000000");

        var response = await PostAsync(orgId, "wamid.UNKNOWN", "+94771111111");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(_disclosureQueue.Enqueued);
        Assert.Equal(0, _whatsApp.SendCalls);

        await using var context = CreateContext();
        Assert.True(await context.InboundMessageLogs.AnyAsync(
            l => l.OrganizationId == orgId && l.Direction == "inbound" && l.ExternalId == "wamid.UNKNOWN"));
    }
}
