using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #205 — organization settings, member listing and the settings entitlement gate
/// (FR-3.1, FR-4.1, FR-4.2).
/// </summary>
public class OrganizationSettingsTests : IAsyncLifetime
{
    private const string DatabaseName = "AvelineInMemoryDb";

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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: DatabaseName)
            .Options);

    private static async Task<(Guid OrgId, string OwnerClerkId, string StaffClerkId)> SeedBoutiqueAsync(
        string suffix, PlanTier tier = PlanTier.Rose)
    {
        await using var context = CreateContext();
        var owner = NewUser($"org_owner_{suffix}", Roles.BoutiqueOwner);
        var staff = NewUser($"org_staff_{suffix}", Roles.BoutiqueStaff);
        context.Users.AddRange(owner, staff);

        var org = new Organization
        {
            Name = $"Settings Org {suffix}",
            Slug = $"settings-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = tier,
        };
        context.Organizations.Add(org);
        context.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = owner.Id,
                BoutiqueRole = Roles.BoutiqueOwner, Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = staff.Id,
                BoutiqueRole = Roles.BoutiqueStaff, Status = MembershipStatus.Active,
            });
        await context.SaveChangesAsync();
        return (org.Id, owner.ClerkId, staff.ClerkId);
    }

    private static User NewUser(string clerkId, string role)
    {
        var id = Guid.CreateVersion7();
        return new User
        {
            Id = id,
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Settings",
            LastName = "User",
            Username = clerkId,
            UserRole = role,
            OrganizationRole = role,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
    }

    private string CreateToken(string clerkId, string? orgRole = null, string? userRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));

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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Patch_AsOwner_UpdatesProfileFields()
    {
        var (orgId, ownerClerk, _) = await SeedBoutiqueAsync("update");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/orgs/{orgId}", token,
            new { name = "Renamed Boutique", phoneNumber = "+94770000000", description = "Silk specialists" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("Renamed Boutique", body.GetProperty("name").GetString());
        Assert.Equal("+94770000000", body.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task Patch_AsStaff_Returns403()
    {
        var (orgId, _, staffClerk) = await SeedBoutiqueAsync("staff");
        var token = CreateToken(staffClerk, Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/orgs/{orgId}", token, new { name = "Nope" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Patch_AiContextFieldsOnSeedPlan_Returns400()
    {
        var (orgId, ownerClerk, _) = await SeedBoutiqueAsync("seed-ai", PlanTier.Seed);
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/orgs/{orgId}", token, new { brandVoice = "Warm and formal" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Patch_SlugCollision_Returns409()
    {
        var (orgA, _, _) = await SeedBoutiqueAsync("collide-a");
        var (orgB, ownerB, _) = await SeedBoutiqueAsync("collide-b");
        var token = CreateToken(ownerB, Roles.BoutiqueOwner);
        string slugOfA;
        await using (var context = CreateContext())
        {
            slugOfA = await context.Organizations.Where(o => o.Id == orgA).Select(o => o.Slug).SingleAsync();
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/orgs/{orgB}", token, new { slug = slugOfA }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task GetSettings_ReturnsResolvedEntitlements()
    {
        var (orgId, ownerClerk, _) = await SeedBoutiqueAsync("settings");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/settings", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        // The response stays `{ settings, entitlements }`: the web's `settings-api.ts` reads
        // `response.settings`, so the branch's flattened `organization` shape would leave the
        // tenant Settings page reading undefined.
        Assert.True(body.TryGetProperty("settings", out var settings));
        Assert.True(settings.TryGetProperty("brandVoice", out _));
        Assert.True(body.TryGetProperty("entitlements", out var entitlements));
        Assert.True(entitlements.GetArrayLength() > 0);
    }

    [Fact]
    public async Task GetSettings_ReturnsForbidden_ForTeamAdminWithoutOwnerMembership()
    {
        var (orgId, _, _) = await SeedBoutiqueAsync("admin-forbidden");
        // A user holding team admin user role, but with non-owner boutique membership
        var nonOwnerToken = CreateToken("clerk_admin_without_owner", Roles.BoutiqueStaff, userRole: Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/settings", nonOwnerToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task ListMembers_ReturnsMembersAndSupportsRoleFilter()
    {
        var (orgId, ownerClerk, _) = await SeedBoutiqueAsync("members");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        var all = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/members", token));
        Assert.Equal(HttpStatusCode.OK, all.StatusCode);
        var allBody = await BodyAsync(all);
        Assert.Equal(2, allBody.GetProperty("total").GetInt32());

        var filtered = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/members?role={Uri.EscapeDataString(Roles.BoutiqueStaff)}",
            token));
        var filteredBody = await BodyAsync(filtered);
        Assert.Equal(1, filteredBody.GetProperty("total").GetInt32());
        Assert.Equal(
            Roles.BoutiqueStaff,
            filteredBody.GetProperty("items")[0].GetProperty("boutiqueRole").GetString());
    }

    [Fact]
    public async Task ChangeMemberRole_Endpoint_PromotesAMember()
    {
        var (orgId, ownerClerk, _) = await SeedBoutiqueAsync("role-endpoint");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        Guid staffUserId;
        await using (var context = CreateContext())
        {
            staffUserId = (await context.Users.SingleAsync(u => u.ClerkId == "org_staff_role-endpoint")).Id;
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{staffUserId}", token,
            new { boutiqueRole = Roles.BoutiqueSupervisor }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(Roles.BoutiqueSupervisor, body.GetProperty("boutiqueRole").GetString());
    }

    [Fact]
    public async Task ChangeMemberRole_ToAnUnknownRole_Returns400()
    {
        var (orgId, ownerClerk, _) = await SeedBoutiqueAsync("role-invalid");
        var token = CreateToken(ownerClerk, Roles.BoutiqueOwner);

        Guid staffUserId;
        await using (var context = CreateContext())
        {
            staffUserId = (await context.Users.SingleAsync(u => u.ClerkId == "org_staff_role-invalid")).Id;
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/orgs/{orgId}/members/{staffUserId}", token,
            new { boutiqueRole = "org:root" }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
