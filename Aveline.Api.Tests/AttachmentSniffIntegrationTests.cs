using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Authorization;
using Aveline.Api.Common.Media;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.RateLimiting;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.3 (strategy §3.6) — the magic-byte sniff on both write paths. <c>MediaContentTypes.Resolve</c>
/// rescues a lazy <c>application/octet-stream</c> header from the file extension, so a <c>.png</c>
/// claimed over a non-image body passed before this unit; the sniff is the gate that refuses it.
/// </summary>
/// <remarks>
/// Both refusals are asserted at the endpoint, not on a helper: the staff upload route answers
/// <c>400</c>, and the inbound webhook skips the attachment and still answers <c>200</c> so Meta
/// does not retry a payload a retry cannot fix.
/// </remarks>
public class AttachmentSniffIntegrationTests : IAsyncLifetime
{
    private const string Base64Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes
    private const string AppSigningKey = "test-app-secret";

    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
    ];

    /// <summary>The payload-smuggling case: an HTML body behind an image file name and header.</summary>
    private static readonly byte[] HtmlBody =
        Encoding.UTF8.GetBytes("<!doctype html><html><body>not an image</body></html>");

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private StubAgentServer _agentServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    private readonly StubWhatsAppService _whatsApp = new();

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _agentServer = new StubAgentServer();
        await _agentServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl);
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Credentials:EncryptionKey", Base64Key);
                builder.ConfigureTestServices(services =>
                {
                    services.AddSingleton<IRateLimiter>(new AllowRateLimiter());
                    services.AddSingleton<IMessageBroadcaster>(new NoopBroadcaster());
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
        await _agentServer.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    // ---------------------------------------------------------------------------------------
    // The staff-upload path (ConversationEndpoints)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task StaffUpload_WithAPngNameOverANonImageBody_IsRefused()
    {
        var (owner, org) = await SeedOwnerAsync("sniff_owner_upload", "sniff-upload");
        var token = CreateToken(owner.ClerkId);
        var conversation = await CreateConversationAsync(org.Id, token);

        var upload = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation}/attachments", token,
            new { imageData = Convert.ToBase64String(HtmlBody), fileName = "photo.png" }));

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
    }

    [Fact]
    public async Task StaffUpload_WithARealPngBody_IsStillAccepted()
    {
        // The sniff must not refuse the legitimate upload it exists to protect.
        var (owner, org) = await SeedOwnerAsync("sniff_owner_ok", "sniff-ok");
        var token = CreateToken(owner.ClerkId);
        var conversation = await CreateConversationAsync(org.Id, token);

        var upload = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation}/attachments", token,
            new { imageData = Convert.ToBase64String(TinyPng), fileName = "photo.png" }));

        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();
        Assert.Equal("image/png", attachment!.ContentType);
    }

    // ---------------------------------------------------------------------------------------
    // The inbound path (WebhookEndpoints)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task InboundWhatsAppMedia_ClaimingAnImageOverANonImageBody_IsSkipped()
    {
        var orgId = await SeedOrgWithWhatsAppAsync("sniff_owner_webhook", "sniff-webhook");
        _whatsApp.Media = new WhatsAppMediaResult(
            IsSuccess: true, Bytes: HtmlBody, ContentType: "image/png", SizeBytes: HtmlBody.LongLength);

        var response = await PostWebhookAsync(orgId, MetaMediaPayload(
            "image", "media-sniff", "look", "image/png", messageId: "wamid.SNIFF1", from: "+94771230001"));

        // The webhook still answers 200, and no attachment is stored (salon §7.5 item 14's
        // fail-closed rule, which the webhook already applies to every media failure).
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("media-sniff", _whatsApp.LastMediaId);

        await using var context = CreateContext();
        var conversation = await context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.ExternalRef == "+94771230001");
        Assert.NotNull(conversation);
        Assert.False(await context.MessageAttachments.AnyAsync(a => a.ConversationId == conversation!.Id));

        var message = await context.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == conversation!.Id);
        Assert.NotNull(message);
        Assert.DoesNotContain("attachment", message!.ContentBlocksJson);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private string CreateToken(string userId)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity([new Claim("sub", userId)]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    private async Task<Guid> CreateConversationAsync(Guid organizationId, string token)
    {
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{organizationId}/conversations", token, new { customerId = (Guid?)null }));
        var conversation = await create.Content.ReadFromJsonAsync<ConversationDto>();
        return conversation!.Id;
    }

    private static HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };

    private static string Sign(byte[] body)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSigningKey));
        return "sha256=" + Convert.ToHexString(hmac.ComputeHash(body)).ToLowerInvariant();
    }

    private async Task<HttpResponseMessage> PostWebhookAsync(Guid orgId, string body)
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

    private static string MetaMediaPayload(
        string kind, string mediaId, string? caption, string mimeType,
        string messageId = "wamid.MEDIA1", string from = "+94771112222")
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

    private async Task<(User Owner, Organization Org)> SeedOwnerAsync(string clerkId, string slug)
    {
        await using var context = CreateContext();
        var owner = NewOwner(clerkId);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = NewOrganization(slug, owner.Id);
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
        await context.SaveChangesAsync();

        return (owner, org);
    }

    private async Task<Guid> SeedOrgWithWhatsAppAsync(string clerkId, string slug)
    {
        await using var context = CreateContext();
        var owner = NewOwner(clerkId);
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = NewOrganization(slug, owner.Id);
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
            ["webhookVerifyToken"] = "test-verify-token",
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

    private static User NewOwner(string clerkId) => new()
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

    private static Organization NewOrganization(string slug, Guid ownerId) => new()
    {
        Name = slug.Replace('-', ' '),
        Slug = slug,
        OwnerUserId = ownerId,
        CreatedAt = DateTime.UtcNow,
        UpdatedAt = DateTime.UtcNow,
    };

    private sealed record ConversationDto(Guid Id);

    private sealed record AttachmentResponse(Guid AttachmentId, string Url, string ContentType, string FileName, long SizeBytes, int? Width, int? Height);

    private sealed class StubWhatsAppService : IWhatsAppService
    {
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

    private sealed class AllowRateLimiter : IRateLimiter
    {
        public Task<bool> TryAllowAsync(
            string scopeKey, int limit, TimeSpan window, CancellationToken cancellationToken = default)
            => Task.FromResult(true);
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
}

/// <summary>
/// The one content-type decision both write paths call (strategy §3.6, salon §7.2): the allow-list
/// resolution plus the magic-byte sniff. The sniffed value is authoritative for storage — a body is
/// stored as what its bytes are, never as what the caller declared, and a PDF claim is now confirmed
/// by the bytes like every other arm.
/// </summary>
public class AttachmentContentPolicyTests
{
    private static readonly byte[] HtmlBody =
        Encoding.UTF8.GetBytes("<!doctype html><html><body>not an image</body></html>");

    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00];

    private static readonly byte[] Pdf = "%PDF-1.7\n1 0 obj\n"u8.ToArray();

    /// <summary>The five-byte marker with no version byte after it: not a document.</summary>
    private static readonly byte[] BarePdfMarker = "%PDF-"u8.ToArray();

    private static readonly byte[] LowercasePdfMarker = "%pdf-1.7\n"u8.ToArray();

    /// <summary>A marker starting at byte 1100, past the sniff's bounded leading window.</summary>
    private static readonly byte[] BuriedPdfMarker =
        [.. Enumerable.Repeat((byte)'A', 1100), .. "%PDF-1.7\n"u8.ToArray()];

    // The four analysable provider formats, then the storable-but-unanalysable set.
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46];

    private static readonly byte[] Gif = "GIF89a"u8.ToArray();

    private static readonly byte[] Webp = [.. "RIFF"u8.ToArray(), 0x1A, 0x00, 0x00, 0x00, .. "WEBPVP8 "u8.ToArray()];

    private static readonly byte[] Bmp = [0x42, 0x4D, 0x36, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00];

    private static readonly byte[] Tiff = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00];

    private static readonly byte[] Avif =
    [
        0x00, 0x00, 0x00, 0x20, .. "ftyp"u8.ToArray(), .. "avif"u8.ToArray(),
        0x00, 0x00, 0x00, 0x00, .. "avif"u8.ToArray(), .. "mif1"u8.ToArray(),
    ];

    private static readonly byte[] Heic =
    [
        0x00, 0x00, 0x00, 0x18, .. "ftyp"u8.ToArray(), .. "heic"u8.ToArray(),
        0x00, 0x00, 0x00, 0x00, .. "heic"u8.ToArray(), .. "mif1"u8.ToArray(),
    ];

    private static readonly byte[] Heif =
    [
        0x00, 0x00, 0x00, 0x18, .. "ftyp"u8.ToArray(), .. "mif1"u8.ToArray(),
        0x00, 0x00, 0x00, 0x00, .. "mif1"u8.ToArray(),
    ];

    [Fact]
    public void ResolveForStorage_RefusesAPngClaimOverANonImageBody()
        => Assert.Null(AttachmentContentPolicy.ResolveForStorage("image/png", "photo.png", HtmlBody));

    [Fact]
    public void ResolveForStorage_RefusesAnOctetStreamClaimRescuedFromTheExtensionOverANonImageBody()
    {
        // `Resolve` alone would answer `image/png` here; the sniff is what refuses it.
        Assert.Equal("image/png", MediaContentTypes.Resolve("application/octet-stream", "photo.png"));
        Assert.Null(AttachmentContentPolicy.ResolveForStorage("application/octet-stream", "photo.png", HtmlBody));
    }

    [Fact]
    public void ResolveForStorage_AcceptsARealImage_AndUsesTheSniffedType()
    {
        Assert.Equal("image/png", AttachmentContentPolicy.ResolveForStorage("image/png", "photo.png", Png));
        // The bytes win over a lying header.
        Assert.Equal("image/png", AttachmentContentPolicy.ResolveForStorage("image/jpeg", "photo.jpg", Png));
    }

    [Fact]
    public void ResolveForStorage_AcceptsARealPdf_AndUsesTheSniffedType()
    {
        // U1.3b: the upstream helper now confirms a PDF by its `%PDF-` header.
        Assert.Equal("application/pdf", MediaContentTypes.Sniff(Pdf));
        Assert.Equal("application/pdf", AttachmentContentPolicy.ResolveForStorage("application/pdf", "report.pdf", Pdf));
    }

    [Fact]
    public void ResolveForStorage_RefusesAPdfClaimOverANonPdfBody()
    {
        // The PDF arm is no longer the branch that trusts a declaration: the bytes decide.
        Assert.Null(AttachmentContentPolicy.ResolveForStorage("application/pdf", "report.pdf", HtmlBody));
    }

    [Fact]
    public void ResolveForStorage_StoresARealImageBehindAPdfClaim_AsTheImage()
    {
        // The point of the change: a real PNG body declared `application/pdf` sniffs as `image/png`
        // and is stored as that, never as the declared type.
        Assert.Equal("image/png", AttachmentContentPolicy.ResolveForStorage("application/pdf", "report.pdf", Png));
    }

    [Fact]
    public void ResolveForStorage_RefusesATruncatedOrMislabelledPdfMarker()
    {
        Assert.Null(AttachmentContentPolicy.ResolveForStorage("application/pdf", "report.pdf", BarePdfMarker));
        Assert.Null(AttachmentContentPolicy.ResolveForStorage("application/pdf", "report.pdf", LowercasePdfMarker));
        Assert.Null(AttachmentContentPolicy.ResolveForStorage("application/pdf", "report.pdf", BuriedPdfMarker));
    }

    [Fact]
    public void ResolveForStorage_SniffsEveryStorableImageFormat_AsBefore()
    {
        // The four analysable provider formats...
        Assert.Equal("image/jpeg", AttachmentContentPolicy.ResolveForStorage("image/jpeg", "photo.jpg", Jpeg));
        Assert.Equal("image/png", AttachmentContentPolicy.ResolveForStorage("image/png", "photo.png", Png));
        Assert.Equal("image/gif", AttachmentContentPolicy.ResolveForStorage("image/gif", "photo.gif", Gif));
        Assert.Equal("image/webp", AttachmentContentPolicy.ResolveForStorage("image/webp", "photo.webp", Webp));
        Assert.True(VisionContentTypes.IsAnalysable("image/jpeg"));
        Assert.True(VisionContentTypes.IsAnalysable("image/png"));
        Assert.True(VisionContentTypes.IsAnalysable("image/gif"));
        Assert.True(VisionContentTypes.IsAnalysable("image/webp"));

        // ...and the storable-but-unanalysable set.
        Assert.Equal("image/avif", AttachmentContentPolicy.ResolveForStorage("image/avif", "photo.avif", Avif));
        Assert.Equal("image/bmp", AttachmentContentPolicy.ResolveForStorage("image/bmp", "photo.bmp", Bmp));
        Assert.Equal("image/tiff", AttachmentContentPolicy.ResolveForStorage("image/tiff", "photo.tiff", Tiff));
        Assert.Equal("image/heic", AttachmentContentPolicy.ResolveForStorage("image/heic", "photo.heic", Heic));
        Assert.Equal("image/heif", AttachmentContentPolicy.ResolveForStorage("image/heif", "photo.heif", Heif));
        Assert.False(VisionContentTypes.IsAnalysable("image/avif"));
        Assert.False(VisionContentTypes.IsAnalysable("image/bmp"));
        Assert.False(VisionContentTypes.IsAnalysable("image/tiff"));
        Assert.False(VisionContentTypes.IsAnalysable("image/heic"));
        Assert.False(VisionContentTypes.IsAnalysable("image/heif"));
    }

    [Fact]
    public void ResolveForStorage_StillRefusesADisallowedType()
        => Assert.Null(AttachmentContentPolicy.ResolveForStorage("text/html", "notes.txt", HtmlBody));
}
