using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for owner staff-invitation management added in #72:
/// create/list/revoke invitations under <c>/api/v1/orgs/&#123;organizationId&#125;/invitations</c>.
/// </summary>
public class InvitationManagementEndpointsIntegrationTests : IAsyncLifetime
{
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
                builder.UseSetting("App:BaseUrl", "https://app.aveline.lk");
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null) claims.Add(new Claim("email", email));

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    private async Task<(User owner, Organization org)> SeedActiveOwnerAsync(string clerkId, string slug)
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
        await context.SaveChangesAsync();

        return (owner, org);
    }

    [Fact]
    public async Task Owner_CreatesInvitation_ReturnsCodeAndLink_AndAppearsInList()
    {
        var (owner, org) = await SeedActiveOwnerAsync("inv_mgr_create", "invite-create");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/invitations", token,
            new { boutiqueRole = Roles.BoutiqueManager, recipientEmail = "mgr@aveline.lk" }));
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);

        var body = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var code = body.GetProperty("code").GetString();
        Assert.False(string.IsNullOrEmpty(code));
        Assert.Contains("/invite?code=", body.GetProperty("link").GetString());
        Assert.StartsWith("aveline://invite?code=", body.GetProperty("mobileLink").GetString());

        var list = await _client.SendAsync(Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/invitations", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listDoc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, listDoc.GetArrayLength());
        Assert.Equal(Roles.BoutiqueManager, listDoc[0].GetProperty("boutiqueRole").GetString());
    }

    [Fact]
    public async Task Owner_InvalidRole_Returns400()
    {
        var (owner, org) = await SeedActiveOwnerAsync("inv_mgr_badrole", "invite-badrole");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/invitations", token,
            new { boutiqueRole = "org:boutique_owner", recipientEmail = "x@aveline.lk" }));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task Owner_InvalidEmail_ReturnsValidationProblem()
    {
        var (owner, org) = await SeedActiveOwnerAsync("inv_mgr_bademail", "invite-bademail");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/invitations", token,
            new { boutiqueRole = Roles.BoutiqueStaff, recipientEmail = "not-an-email" }));
        Assert.Equal(HttpStatusCode.BadRequest, create.StatusCode);
    }

    [Fact]
    public async Task Owner_RevokesInvitation_AndCodeNoLongerAccepts()
    {
        var (owner, org) = await SeedActiveOwnerAsync("inv_mgr_revoke", "invite-revoke");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/invitations", token,
            new { boutiqueRole = Roles.BoutiqueStaff, recipientEmail = "staff@aveline.lk" }));
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var invitationId = created.GetProperty("invitationId").GetGuid();
        var code = created.GetProperty("code").GetString();

        var revoke = await _client.SendAsync(Authorized(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/invitations/{invitationId}/revoke", token));
        Assert.Equal(HttpStatusCode.NoContent, revoke.StatusCode);

        // A staff user exists so the accept endpoint reaches the revocation check.
        await using (var seed = CreateContext())
        {
            seed.Users.Add(new User
            {
                Id = Guid.CreateVersion7(),
                ClerkId = "revoked_staff",
                Email = "staff@aveline.lk",
                FirstName = "Staff",
                LastName = "User",
                Username = "revoked_staff",
                UserRole = Roles.Staff,
                OrganizationRole = string.Empty,
                HasCompletedOnboarding = false,
                AccountState = AccountState.OnboardingPending,
            });
            await seed.SaveChangesAsync();
        }

        // Staff cannot accept the revoked code.
        var staffToken = CreateToken("revoked_staff", "staff@aveline.lk");
        var accept = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            "/api/v1/invitations/accept", staffToken, new { code }));
        Assert.Equal(HttpStatusCode.BadRequest, accept.StatusCode);
    }

    [Fact]
    public async Task Owner_CannotManageAnotherOrgsInvitations_Returns403()
    {
        var (ownerA, orgA) = await SeedActiveOwnerAsync("inv_mgr_a", "invite-a");
        var (_, orgB) = await SeedActiveOwnerAsync("inv_mgr_b", "invite-b");
        var tokenA = CreateToken(ownerA.ClerkId, ownerA.Email);

        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{orgB.Id}/invitations", tokenA,
            new { boutiqueRole = Roles.BoutiqueStaff, recipientEmail = "s@aveline.lk" }));
        Assert.Equal(HttpStatusCode.Forbidden, create.StatusCode);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }
}
