using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
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
    private const string AppSecret = "test-app-secret";
    private const string VerifyToken = "test-verify-token";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

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
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(AppSecret));
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
