using System.Net;
using System.Security.Cryptography;
using System.Text;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Webhooks;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #207 — Clerk webhook signature verification, idempotency and read-model sync.
/// </summary>
public class ClerkWebhookTests : IAsyncLifetime
{
    private static readonly string Secret =
        "whsec_" + Convert.ToBase64String(Encoding.UTF8.GetBytes("aveline-webhook-test-signing-key"));

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = CreateFactory(Secret);
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private WebApplicationFactory<Program> CreateFactory(string? webhookSecret) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                if (webhookSecret is not null)
                {
                    builder.UseSetting("Clerk:WebhookSecret", webhookSecret);
                }
            });

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private static HttpRequestMessage SignedRequest(string body, bool valid = true)
    {
        var bytes = Encoding.UTF8.GetBytes(body);
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var id = $"msg_{Guid.NewGuid():N}";

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/webhooks/clerk")
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("svix-id", id);
        request.Headers.Add("svix-timestamp", timestamp);
        request.Headers.Add(
            "svix-signature",
            valid ? ClerkWebhookVerifier.Sign(Secret, id, timestamp, bytes) : "v1,invalid");
        return request;
    }

    [Fact]
    public async Task UserCreated_SyncsTheReadModel()
    {
        var clerkId = $"wh_user_{Guid.NewGuid():N}";
        var body = $$"""
            {
              "type": "user.created",
              "data": {
                "id": "{{clerkId}}",
                "first_name": "Web",
                "last_name": "Hook",
                "email_addresses": [ { "email_address": "Web.Hook@Aveline.lk" } ],
                "public_metadata": { "role": "admin" }
              }
            }
            """;

        var response = await _client.SendAsync(SignedRequest(body));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        var user = await context.Users.SingleAsync(u => u.ClerkId == clerkId);
        Assert.Equal("Web", user.FirstName);
        Assert.Equal("web.hook@aveline.lk", user.Email);
        Assert.Equal("admin", user.UserRole);
    }

    [Fact]
    public async Task RedeliveredEvent_IsIdempotent()
    {
        var clerkId = $"wh_idem_{Guid.NewGuid():N}";
        var body = $$"""
            {
              "type": "user.updated",
              "data": { "id": "{{clerkId}}", "first_name": "Again", "last_name": "Same",
                "email_addresses": [ { "email_address": "again@aveline.lk" } ] }
            }
            """;

        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(SignedRequest(body))).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await _client.SendAsync(SignedRequest(body))).StatusCode);

        await using var context = CreateContext();
        Assert.Equal(1, await context.Users.CountAsync(u => u.ClerkId == clerkId));
    }

    [Fact]
    public async Task InvalidSignature_Returns401()
    {
        var body = """{ "type": "user.created", "data": { "id": "nope" } }""";

        var response = await _client.SendAsync(SignedRequest(body, valid: false));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MissingSecret_Returns503()
    {
        await using var factory = CreateFactory(webhookSecret: null);
        using var client = factory.CreateClient();

        var response = await client.SendAsync(SignedRequest("""{ "type": "user.created", "data": { "id": "x" } }"""));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task UserDeleted_SoftDeletesAndRevokesMemberships()
    {
        var clerkId = $"wh_delete_{Guid.NewGuid():N}";
        Guid userId;
        await using (var context = CreateContext())
        {
            var user = new User
            {
                Id = Guid.CreateVersion7(), ClerkId = clerkId, Email = "del@aveline.lk",
                FirstName = "Del", LastName = "User", Username = clerkId,
                AccountState = AccountState.Active,
            };
            context.Users.Add(user);
            var org = new Organization
            {
                Name = "Delete Org", Slug = $"whdel-{Guid.NewGuid():N}", OwnerUserId = user.Id,
            };
            context.Organizations.Add(org);
            context.OrganizationMemberships.Add(new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = user.Id,
                BoutiqueRole = Roles.BoutiqueOwner, Status = MembershipStatus.Active,
            });
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        var body = $$"""{ "type": "user.deleted", "data": { "id": "{{clerkId}}" } }""";
        var response = await _client.SendAsync(SignedRequest(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var context = CreateContext())
        {
            var stored = await context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == userId);
            Assert.NotNull(stored.DeletedAt);
            Assert.Equal(AccountState.Suspended, stored.AccountState);
            Assert.Empty(await context.OrganizationMemberships.Where(m => m.UserId == userId).ToListAsync());
        }
    }

    [Fact]
    public async Task MembershipCreated_UpsertsTheMembership()
    {
        var clerkOrgId = $"org_{Guid.NewGuid():N}";
        var clerkUserId = $"wh_member_{Guid.NewGuid():N}";
        await using (var context = CreateContext())
        {
            var owner = new User
            {
                Id = Guid.CreateVersion7(), ClerkId = $"owner_{Guid.NewGuid():N}", Email = "o@aveline.lk",
                FirstName = "O", LastName = "W", Username = "owner", AccountState = AccountState.Active,
            };
            var member = new User
            {
                Id = Guid.CreateVersion7(), ClerkId = clerkUserId, Email = "m@aveline.lk",
                FirstName = "M", LastName = "E", Username = "member", AccountState = AccountState.Active,
            };
            context.Users.AddRange(owner, member);
            context.Organizations.Add(new Organization
            {
                Name = "Membership Org", Slug = $"whmem-{Guid.NewGuid():N}",
                OwnerUserId = owner.Id, ClerkOrgId = clerkOrgId,
            });
            await context.SaveChangesAsync();
        }

        var body = $$"""
            {
              "type": "organizationMembership.created",
              "data": {
                "role": "org:boutique_manager",
                "organization": { "id": "{{clerkOrgId}}" },
                "public_user_data": { "user_id": "{{clerkUserId}}" }
              }
            }
            """;

        var response = await _client.SendAsync(SignedRequest(body));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var context = CreateContext())
        {
            var membership = await context.OrganizationMemberships
                .SingleAsync(m => m.UserId == context.Users.Single(u => u.ClerkId == clerkUserId).Id);
            Assert.Equal(Roles.BoutiqueManager, membership.BoutiqueRole);
        }
    }

    [Fact]
    public async Task UnsupportedEvent_IsAcceptedAndIgnored()
    {
        var response = await _client.SendAsync(SignedRequest(
            """{ "type": "session.created", "data": { "id": "sess_x" } }"""));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("org:boutique_owner", Roles.BoutiqueOwner)]
    [InlineData("org:boutique_supervisor", Roles.BoutiqueSupervisor)]
    [InlineData("org:boutique_manager", Roles.BoutiqueManager)]
    [InlineData("org:boutique_staff", Roles.BoutiqueStaff)]
    [InlineData("org:not_the_owner", Roles.BoutiqueStaff)]
    [InlineData("org:manager", Roles.BoutiqueStaff)]
    [InlineData("owner", Roles.BoutiqueStaff)]
    [InlineData("", Roles.BoutiqueStaff)]
    [InlineData(null, Roles.BoutiqueStaff)]
    public void MapBoutiqueRole_UsesExactCanonicalClerkRoles(string? clerkRole, string expected)
    {
        Assert.Equal(expected, ClerkWebhookSyncService.MapBoutiqueRole(clerkRole));
    }
}
