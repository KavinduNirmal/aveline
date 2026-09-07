using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for the #51 onboarding endpoints: organization creation,
/// invitation acceptance, and the account-state gate.
/// </summary>
public class OrganizationEndpointsIntegrationTests : IAsyncLifetime
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
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? email = null, string? firstName = null, string? lastName = null, string? userRole = null, string? orgId = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null) claims.Add(new Claim("email", email));
        if (firstName != null) claims.Add(new Claim("first_name", firstName));
        if (lastName != null) claims.Add(new Claim("last_name", lastName));
        if (userRole != null) claims.Add(new Claim("user_role", userRole));
        if (orgId != null) claims.Add(new Claim("org_id", orgId));

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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task OnboardingPendingUser_BusinessEndpoint_Returns403OnboardingRequired()
    {
        var token = CreateToken("user_pending", "pending@aveline.lk", "Pending", "User", "staff");
        var request = Authorized(HttpMethod.Get, "/api/v1/policies/associate", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("onboarding-required", body);
    }

    [Fact]
    public async Task OnboardingPendingUser_CannotCreateOrganization_Returns403()
    {
        // A pending (not-yet-onboarded) account must not be able to create an organization
        // to short-circuit onboarding into an Active state.
        var token = CreateToken("user_owner_flow", "owner.flow@aveline.lk", "Nimal", "Perera", "staff");

        var onboard = await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/users/onboarding", token,
            new { displayName = "Nimal Perera", phoneNumber = "+94771234567", address = "1 Galle Road, Colombo 03" }));
        Assert.Equal(HttpStatusCode.OK, onboard.StatusCode);

        var createResponse = await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/orgs", token,
            new { name = "Aveline Boutique Colombo" }));

        Assert.Equal(HttpStatusCode.Forbidden, createResponse.StatusCode);
        Assert.Contains("onboarding-required", await createResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ActiveOwner_CreatesAdditionalBoutique_StaysActive_AndDerivesClerkOrgIdFromClaim()
    {
        // Seed an already-Active owner (multi-boutique scenario) whose JWT carries an org claim.
        await using var seed = CreateSeedContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "user_active_multi",
            Email = "active.multi@aveline.lk",
            FirstName = "Active",
            LastName = "Owner",
            Username = "user_active_multi",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        seed.Users.Add(owner);
        await seed.SaveChangesAsync();

        var token = CreateToken("user_active_multi", "active.multi@aveline.lk", "Active", "Owner", "staff", orgId: "org_claim_x");

        var createResponse = await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/orgs", token,
            new { name = "Aveline Boutique Colombo" }));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var createBody = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Aveline Boutique Colombo", createBody.GetProperty("organization").GetProperty("name").GetString());
        Assert.Equal("Active", createBody.GetProperty("accountState").GetString());
        // ClerkOrgId must come from the JWT org claim, never the client body.
        Assert.Equal("org_claim_x", createBody.GetProperty("organization").GetProperty("clerkOrgId").GetString());

        // /orgs/my lists the owner membership for the newly created boutique.
        var my = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/orgs/my", token));
        Assert.Equal(HttpStatusCode.OK, my.StatusCode);
        var myDoc = JsonDocument.Parse(await my.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, myDoc.GetArrayLength());
        Assert.Equal(Roles.BoutiqueOwner, myDoc[0].GetProperty("boutiqueRole").GetString());
    }

    [Fact]
    public async Task ActiveOwner_CreatesBoutique_WithoutOrgClaim_LeavesClerkOrgIdNull()
    {
        await using var seed = CreateSeedContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "user_active_noclaim",
            Email = "active.noclaim@aveline.lk",
            FirstName = "No",
            LastName = "Claim",
            Username = "user_active_noclaim",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        seed.Users.Add(owner);
        await seed.SaveChangesAsync();

        // Even if the body tried to smuggle a clerkOrgId, the DTO no longer accepts it.
        var token = CreateToken("user_active_noclaim", "active.noclaim@aveline.lk", "No", "Claim", "staff");

        var createResponse = await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/orgs", token,
            new { name = "Second Boutique", clerkOrgId = "org_should_be_ignored" }));

        Assert.Equal(HttpStatusCode.OK, createResponse.StatusCode);
        var createBody = JsonDocument.Parse(await createResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Null, createBody.GetProperty("organization").GetProperty("clerkOrgId").ValueKind);
    }

    [Fact]
    public async Task Staff_AcceptsInvite_BecomesActive_AndHasMembership()
    {
        // Seed an organization + invitation for a staff member (via the shared InMemory store).
        await using var context = CreateSeedContext();
        var owner = new User { Id = Guid.CreateVersion7(), ClerkId = "seed_owner", Email = "seed.owner@aveline.lk", FirstName = "Seed", LastName = "Owner", Username = "seed_owner", UserRole = Roles.Staff, OrganizationRole = string.Empty };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var organization = new Organization { Name = "Seed Boutique", Slug = "seed-boutique", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var code = InvitationTokens.GenerateCode();
        context.OrganizationInvitations.Add(new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            InvitedByUserId = owner.Id,
            RecipientEmail = "staff.flow@aveline.lk",
            TokenHash = InvitationTokens.Hash(code),
            BoutiqueRole = Roles.BoutiqueManager,
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        // Staff joins using the invitation code.
        var token = CreateToken("user_staff_flow", "staff.flow@aveline.lk", "Kamal", "Silva", "staff");
        var accept = await _client.SendAsync(AuthorizedJson(
            HttpMethod.Post, "/api/v1/invitations/accept", token,
            new { code }));

        Assert.Equal(HttpStatusCode.OK, accept.StatusCode);
        var acceptBody = JsonDocument.Parse(await accept.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(Roles.BoutiqueManager, acceptBody.GetProperty("boutiqueRole").GetString());
        Assert.Equal("Active", acceptBody.GetProperty("accountState").GetString());

        var my = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/orgs/my", token));
        var myDoc = JsonDocument.Parse(await my.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, myDoc.GetArrayLength());
        Assert.Equal(Roles.BoutiqueManager, myDoc[0].GetProperty("boutiqueRole").GetString());
    }

    [Fact]
    public async Task Staff_ReplaysInviteCode_SecondAccept_Returns400_AndDoesNotDuplicateMembership()
    {
        // Seed an organization + invitation.
        await using var context = CreateSeedContext();
        var owner = new User { Id = Guid.CreateVersion7(), ClerkId = "seed_owner_replay", Email = "seed.owner.replay@aveline.lk", FirstName = "Seed", LastName = "Owner", Username = "seed_owner_replay", UserRole = Roles.Staff, OrganizationRole = string.Empty };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var organization = new Organization { Name = "Replay Boutique", Slug = "replay-boutique", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var code = InvitationTokens.GenerateCode();
        context.OrganizationInvitations.Add(new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            InvitedByUserId = owner.Id,
            RecipientEmail = "staff.replay@aveline.lk",
            TokenHash = InvitationTokens.Hash(code),
            BoutiqueRole = Roles.BoutiqueStaff,
            ExpiresAt = DateTime.UtcNow.AddDays(3),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        var token = CreateToken("user_staff_replay", "staff.replay@aveline.lk", "Replay", "Staff", "staff");

        var first = await _client.SendAsync(AuthorizedJson(HttpMethod.Post, "/api/v1/invitations/accept", token, new { code }));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        // Replaying the same code must fail and must not create a second membership.
        var second = await _client.SendAsync(AuthorizedJson(HttpMethod.Post, "/api/v1/invitations/accept", token, new { code }));
        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        Assert.Contains("already accepted", await second.Content.ReadAsStringAsync());

        var my = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/orgs/my", token));
        var myDoc = JsonDocument.Parse(await my.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, myDoc.GetArrayLength());
    }

    [Fact]
    public async Task Staff_AcceptsExpiredInvite_Returns400_AndStaysOnboardingPending()
    {
        await using var context = CreateSeedContext();
        var owner = new User { Id = Guid.CreateVersion7(), ClerkId = "seed_owner_expired", Email = "seed.owner.expired@aveline.lk", FirstName = "Seed", LastName = "Owner", Username = "seed_owner_expired", UserRole = Roles.Staff, OrganizationRole = string.Empty };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var organization = new Organization { Name = "Expired Boutique", Slug = "expired-boutique", OwnerUserId = owner.Id, CreatedAt = DateTime.UtcNow, UpdatedAt = DateTime.UtcNow };
        context.Organizations.Add(organization);
        await context.SaveChangesAsync();

        var code = InvitationTokens.GenerateCode();
        context.OrganizationInvitations.Add(new OrganizationInvitation
        {
            OrganizationId = organization.Id,
            InvitedByUserId = owner.Id,
            RecipientEmail = "staff.expired@aveline.lk",
            TokenHash = InvitationTokens.Hash(code),
            BoutiqueRole = Roles.BoutiqueStaff,
            ExpiresAt = DateTime.UtcNow.AddMinutes(-1),
            CreatedAt = DateTime.UtcNow.AddDays(-2),
            UpdatedAt = DateTime.UtcNow.AddDays(-2),
        });
        await context.SaveChangesAsync();

        var token = CreateToken("user_staff_expired", "staff.expired@aveline.lk", "Late", "Staff", "staff");

        var accept = await _client.SendAsync(AuthorizedJson(HttpMethod.Post, "/api/v1/invitations/accept", token, new { code }));
        Assert.Equal(HttpStatusCode.BadRequest, accept.StatusCode);
        Assert.Contains("expired", await accept.Content.ReadAsStringAsync());

        // Account remains pending: the raw /orgs endpoints are now gated off for pending
        // accounts (so they cannot create an org to bypass onboarding).
        var my = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/orgs/my", token));
        Assert.Equal(HttpStatusCode.Forbidden, my.StatusCode);

        var business = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/policies/associate", token));
        Assert.Equal(HttpStatusCode.Forbidden, business.StatusCode);
        Assert.Contains("onboarding-required", await business.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SuspendedAccount_AllEndpoints_Return403AccountSuspended()
    {
        await using var context = CreateSeedContext();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = "user_suspended_flow",
            Email = "suspended.flow@aveline.lk",
            FirstName = "Suspended",
            LastName = "User",
            Username = "user_suspended_flow",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Suspended,
            IsActive = false,
        });
        await context.SaveChangesAsync();

        var token = CreateToken("user_suspended_flow", "suspended.flow@aveline.lk", "Suspended", "User", "staff");

        // Even profile/allowed paths are blocked once the account is suspended.
        var business = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/policies/associate", token));
        Assert.Equal(HttpStatusCode.Forbidden, business.StatusCode);
        Assert.Contains("account-suspended", await business.Content.ReadAsStringAsync());

        var me = await _client.SendAsync(Authorized(HttpMethod.Get, "/api/v1/users/me", token));
        Assert.Equal(HttpStatusCode.Forbidden, me.StatusCode);
        Assert.Contains("account-suspended", await me.Content.ReadAsStringAsync());
    }

    private static AppDbContext CreateSeedContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }
}
