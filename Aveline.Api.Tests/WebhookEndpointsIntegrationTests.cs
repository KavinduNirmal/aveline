using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for the public WhatsApp webhook endpoints
/// (<c>GET/POST /api/v1/webhooks/whatsapp/&#123;organizationId&#125;</c>).
/// </summary>
public class WebhookEndpointsIntegrationTests : IAsyncLifetime
{
    private const string Base64Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
    private const string AppSigningKey = "test-app-secret";
    private const string VerifyToken = "test-verify-token";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private readonly RecordingRateLimiter _rateLimiter = new();
    private readonly RecordingBroadcaster _broadcaster = new();

    private readonly StubWhatsAppService _whatsApp = new();

    private sealed class RecordingRateLimiter : IRateLimiter
    {
        private int _calls;

        public int Calls => Volatile.Read(ref _calls);

        public void Reset() => Volatile.Write(ref _calls, 0);

        public Task<bool> TryAllowAsync(
            string scopeKey, int limit, TimeSpan window, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _calls);
            return Task.FromResult(true);
        }
    }

    /// <summary>Records the inbox tiles the API broadcasts, so a new thread's arrival is visible
    /// to a test without a live SignalR client.</summary>
    /// <summary>A real 1x1 PNG, so a stored image is a plausible payload.</summary>
    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
        0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
        0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
        0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
        0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
        0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
    ];

    private sealed class StubWhatsAppService : IWhatsAppService
    {
        /// <summary>What the media fetch should answer; null means a failure.</summary>
        public WhatsAppMediaResult? Media { get; set; }

        public string? LastMediaId { get; private set; }

        public Task<WhatsAppTestResult> TestConnectionAsync(
            string accessToken, string phoneNumberId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppTestResult(IsValid: true));

        public Task<WhatsAppMediaResult> GetMediaAsync(
            string accessToken, string mediaId, CancellationToken cancellationToken = default)
        {
            LastMediaId = mediaId;
            return Task.FromResult(Media ?? new WhatsAppMediaResult(IsSuccess: false, Error: "no media"));
        }

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text,
            CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppSendResult(IsSuccess: true));
    }

    private sealed class RecordingBroadcaster : IMessageBroadcaster
    {
        public List<ConversationTile> ConversationChanged { get; } = [];

        public Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task BroadcastConversationChangedAsync(ConversationTile tile, CancellationToken cancellationToken = default)
        {
            ConversationChanged.Add(tile);
            return Task.CompletedTask;
        }
    }

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Credentials:EncryptionKey", Base64Key);
                builder.ConfigureTestServices(services =>
                {
                    // Observe whether a request ever reaches the rate limiter, and capture the
                    // inbox tiles the API broadcasts.
                    services.AddSingleton<IRateLimiter>(_rateLimiter);
                    services.AddSingleton<IMessageBroadcaster>(_broadcaster);
                    // The Meta provider is a typed HttpClient; a test never calls Meta, so the
                    // media fetch is stubbed and made configurable per test.
                    services.RemoveAll<IWhatsAppService>();
                    services.AddSingleton<IWhatsAppService>(_whatsApp);
                });
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private static string Sign(byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSigningKey));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private static string MetaMessagePayload(string messageId = "wamid.ABC123", string from = "+94771234567") =>
        $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "WABA_ID",
              "changes": [
                {
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "15551234567", "phone_number_id": "111" },
                    "contacts": [ { "profile": { "name": "Customer" }, "wa_id": "{{from}}" } ],
                    "messages": [
                      { "from": "{{from}}", "id": "{{messageId}}", "timestamp": "1720000000",
                        "text": { "body": "Hi, do you have this in red?" }, "type": "text" }
                    ]
                  },
                  "field": "messages"
                }
              ]
            }
          ]
        }
        """;

    /// <summary>A Meta inbound payload carrying an image or a document.</summary>
    private static string MetaMediaPayload(
        string kind,
        string mediaId,
        string? caption,
        string mimeType,
        string messageId = "wamid.MEDIA1",
        string from = "+94771112222")
    {
        var captionJson = caption is null ? string.Empty : $""", "caption": "{caption}" """;
        return $$"""
        {
          "object": "whatsapp_business_account",
          "entry": [
            {
              "id": "WABA_ID",
              "changes": [
                {
                  "value": {
                    "messaging_product": "whatsapp",
                    "metadata": { "display_phone_number": "15551234567", "phone_number_id": "111" },
                    "contacts": [ { "profile": { "name": "Customer" }, "wa_id": "{{from}}" } ],
                    "messages": [
                      { "from": "{{from}}", "id": "{{messageId}}", "timestamp": "1720000000",
                        "type": "{{kind}}",
                        "{{kind}}": { "id": "{{mediaId}}", "mime_type": "{{mimeType}}", "sha256": "abc"{{captionJson}} } }
                    ]
                  },
                  "field": "messages"
                }
              ]
            }
          ]
        }
        """;
    }

    private async Task<HttpResponseMessage> PostAsync(Guid orgId, string body)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));
        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task Post_WithAnImage_RecordsTheClientMessageAndTheImage()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_img", "webhook-img");
        _whatsApp.Media = new WhatsAppMediaResult(
            IsSuccess: true, Bytes: TinyPng, ContentType: "image/png", SizeBytes: TinyPng.LongLength);

        var response = await PostAsync(orgId, MetaMediaPayload(
            "image", "media-1", "Look at this", "image/png", messageId: "wamid.IMG1", from: "+94771110001"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("media-1", _whatsApp.LastMediaId);

        await using var context = CreateContext();
        var conversation = await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == "+94771110001");
        Assert.NotNull(conversation);

        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == conversation!.Id);
        Assert.NotNull(message);
        // The caption is the client's words, and the file is appended as its own block.
        Assert.Contains("client_message", message!.ContentBlocksJson);
        Assert.Contains("Look at this", message.ContentBlocksJson);
        Assert.Contains("attachment", message.ContentBlocksJson);

        var attachment = await context.MessageAttachments
            .FirstOrDefaultAsync(a => a.ConversationId == conversation.Id);
        Assert.NotNull(attachment);
        Assert.Equal(message.Id, attachment!.MessageId);
        Assert.Equal("image/png", attachment.ContentType);
        Assert.Equal(new string('a', 0) + "database", attachment.StorageProvider);
        Assert.Equal(TinyPng, attachment.ImageData);
    }

    [Fact]
    public async Task Post_WithAnImageAndNoCaption_StillRecordsIt()
    {
        // Today this produced `{status: "ignored"}` and nothing in the thread.
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_img2", "webhook-img2");
        _whatsApp.Media = new WhatsAppMediaResult(
            IsSuccess: true, Bytes: TinyPng, ContentType: "image/png", SizeBytes: TinyPng.LongLength);

        var response = await PostAsync(orgId, MetaMediaPayload(
            "image", "media-2", null, "image/png", messageId: "wamid.IMG2", from: "+94771110002"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        var conversation = await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == "+94771110002");
        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == conversation!.Id);
        Assert.NotNull(message);
        Assert.Contains("attachment", message!.ContentBlocksJson);
    }

    [Fact]
    public async Task Post_WithAVideo_SkipsItAndStillAnswers200()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_vid", "webhook-vid");
        _whatsApp.Media = new WhatsAppMediaResult(
            IsSuccess: true, Bytes: [1, 2, 3], ContentType: "video/mp4", SizeBytes: 3);

        var response = await PostAsync(orgId, MetaMediaPayload(
            "video", "media-3", "watch", "video/mp4", messageId: "wamid.VID1", from: "+94771110003"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        // A type this build does not model has no media descriptor, so it is skipped rather
        // than half-recorded: nothing stored, nothing surfaced, and the webhook still answers
        // 200 so Meta does not retry a payload nothing will ever accept.
        Assert.Empty(await context.MessageAttachments
            .Where(a => a.OrganizationId == orgId)
            .ToListAsync());
        Assert.Null(await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == "+94771110003"));
    }

    [Fact]
    public async Task Post_WhenTheMediaDownloadFails_RecordsTheCaptionAndAnswers200()
    {
        // Meta's media URL expires; a retry would not fix it, so the webhook must not fail.
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_mediafail", "webhook-mediafail");
        _whatsApp.Media = null;

        var response = await PostAsync(orgId, MetaMediaPayload(
            "image", "media-4", "gone", "image/png", messageId: "wamid.IMG4", from: "+94771110004"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        var conversation = await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == "+94771110004");
        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == conversation!.Id);
        Assert.NotNull(message);
        Assert.Contains("gone", message!.ContentBlocksJson);
        Assert.DoesNotContain("attachment", message.ContentBlocksJson);
    }

    private async Task<Guid> SeedOrgWithWhatsAppAsync(string clerkId, string slug)
    {
        await using var context = CreateContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Owner",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = slug.Replace('-', ' '),
            Slug = slug,
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        // Store encrypted WhatsApp credentials (appSecret + verifyToken needed by the webhook).
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [CredentialEncryptionService.ConfigKey] = Base64Key,
            })
            .Build();
        var encryption = new CredentialEncryptionService(config);
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["accessToken"] = "wa-token",
            ["phoneNumberId"] = "111",
            ["appSecret"] = AppSigningKey,
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

    [Fact]
    public async Task Get_ValidVerifyToken_ReturnsChallenge()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_a", "webhook-a");

        var response = await _client.GetAsync(
            $"/api/v1/webhooks/whatsapp/{orgId}?hub.mode=subscribe&hub.verify_token={VerifyToken}&hub.challenge=1234567890");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("1234567890", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Get_InvalidVerifyToken_Returns403()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_b", "webhook-b");

        var response = await _client.GetAsync(
            $"/api/v1/webhooks/whatsapp/{orgId}?hub.mode=subscribe&hub.verify_token=wrong&hub.challenge=123");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Get_InvalidMode_Returns400()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_c", "webhook-c");

        var response = await _client.GetAsync(
            $"/api/v1/webhooks/whatsapp/{orgId}?hub.mode=unsubscribe&hub.verify_token={VerifyToken}&hub.challenge=123");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidSignature_Returns401()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_d", "webhook-d");
        var body = MetaMessagePayload();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=deadbeef");

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Post_InvalidSignature_DoesNotConsumeTheRateLimit()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_rl1", "webhook-rl1");
        _rateLimiter.Reset();

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new StringContent(MetaMessagePayload("wamid.RL1"), Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Hub-Signature-256", "sha256=deadbeef");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, _rateLimiter.Calls);
    }

    [Fact]
    public async Task Post_ValidSignature_ChargesTheRateLimitOnce()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_rl2", "webhook-rl2");
        _rateLimiter.Reset();
        var bytes = Encoding.UTF8.GetBytes(MetaMessagePayload("wamid.RL2"));

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, _rateLimiter.Calls);
    }

    [Fact]
    public async Task Post_ValidSignature_PersistsAuditLog()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_e", "webhook-e");
        var body = MetaMessagePayload(messageId: "wamid.PERSIST1");
        var bytes = Encoding.UTF8.GetBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains("received", responseBody);

        await using var context = CreateContext();
        var log = await context.InboundMessageLogs
            .FirstOrDefaultAsync(m => m.OrganizationId == orgId && m.ExternalId == "wamid.PERSIST1");
        Assert.NotNull(log);
        Assert.Equal("whatsapp", log.Channel);
        Assert.Equal("inbound", log.Direction);
        Assert.Equal("+94771234567", log.From);
        Assert.Contains("red", log.Content);
    }

    [Fact]
    public async Task Post_ValidSignature_CreatesClientMessageInSalon()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_g", "webhook-g");
        var body = MetaMessagePayload(messageId: "wamid.SALON1", from: "+94779998888");
        var bytes = Encoding.UTF8.GetBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        var conversation = await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == "+94779998888");
        Assert.NotNull(conversation);

        // D1: the phone is not on file, so the thread stays unbound but identifiable as a channel
        // thread - the client renders it as an unnamed client rather than pinning it.
        Assert.Null(conversation.CustomerId);

        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == conversation.Id);
        Assert.NotNull(message);
        Assert.Equal("ClientMessage", message.Kind.ToString());
        Assert.Equal("System", message.AuthorKind.ToString());
        Assert.Contains("red", message.ContentBlocksJson);

        // The new thread reaches an already-open inbox without a re-list: the API broadcasts its
        // tile from the creation site, because `message.created` alone carries a message for a
        // conversation the client may never have seen.
        var tile = Assert.Single(_broadcaster.ConversationChanged);
        Assert.Equal(orgId, tile.OrganizationId);
        Assert.Null(tile.OwnerUserId);
        Assert.Equal("+94779998888", tile.Tile.ExternalRef);
        Assert.Null(tile.Tile.CustomerId);
        Assert.Equal("client_message", tile.Tile.LastMessageBlock);
        Assert.Contains("red", tile.Tile.LastMessagePreview);
    }

    [Fact]
    public async Task Post_ValidSignature_ForAPhoneOnFile_BindsTheCustomerToTheThread()
    {
        // D1: a thread created by the inbound path exists for the identified customer, so the
        // server resolves the phone through the book and binds the context at creation.
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_h", "webhook-h");
        const string phone = "+94771112222";
        var customerId = await SeedCustomerAsync(orgId, phone, "Nadeesha Perera");

        var body = MetaMessagePayload(messageId: "wamid.BIND1", from: phone);
        var bytes = Encoding.UTF8.GetBytes(body);
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        var conversation = await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == phone);
        Assert.NotNull(conversation);
        Assert.Equal(customerId, conversation.CustomerId);

        // The broadcast tile carries the resolved client, so the list shows a name and not an
        // unnamed channel thread.
        var tile = Assert.Single(_broadcaster.ConversationChanged);
        Assert.Equal(customerId, tile.Tile.CustomerId);
        Assert.Equal("Nadeesha Perera", tile.Tile.CustomerName);
    }

    private static async Task<Guid> SeedCustomerAsync(Guid orgId, string phone, string fullName)
    {
        await using var context = CreateContext();
        var customer = new Customer
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = orgId,
            PhoneNumber = phone,
            FullName = fullName,
            Status = "returning",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();
        return customer.Id;
    }

    [Fact]
    public async Task Post_NonMessageEvent_ReturnsIgnored()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("wh_owner_f", "webhook-f");
        var body = """
            {
              "object": "whatsapp_business_account",
              "entry": [
                { "id": "WABA_ID", "changes": [ { "value": { "messaging_product": "whatsapp",
                  "statuses": [ { "id": "wamid.STATUS", "status": "read" } ] }, "field": "messages" } ] }
              ]
            }
            """;
        var bytes = Encoding.UTF8.GetBytes(body);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/v1/webhooks/whatsapp/{orgId}")
        {
            Content = new ByteArrayContent(bytes),
        };
        request.Content.Headers.ContentType = new("application/json");
        request.Headers.Add("X-Hub-Signature-256", Sign(bytes));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("ignored", await response.Content.ReadAsStringAsync());
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }
}
