using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.ApiAccess.Domain;
using Aveline.Api.Modules.ApiAccess.Models;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #206 — user profile, account deletion, Clerk sessions and the administrator
/// account-state matrix (FR-3.2, FR-3.5–FR-3.9).
/// </summary>
public class UserAccountStateTests : IAsyncLifetime
{
    private const string DatabaseName = "AvelineInMemoryDb";

    private sealed class FakeClerkAdminClient : IClerkAdminClient
    {
        public int RevokeCalls { get; private set; }

        public Task GrantAdminRoleAsync(string clerkUserId, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ClerkSession>> ListSessionsAsync(
            string clerkUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClerkSession>>(
            [
                new ClerkSession("sess_1", "active", DateTime.UtcNow, DateTime.UtcNow, null),
            ]);

        public Task<int> RevokeAllSessionsAsync(
            string clerkUserId, CancellationToken cancellationToken = default)
        {
            RevokeCalls++;
            return Task.FromResult(1);
        }
    }

    private readonly FakeClerkAdminClient _clerk = new();

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
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IClerkAdminClient>();
                    services.AddSingleton<IClerkAdminClient>(_clerk);
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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: DatabaseName)
            .Options);

    private static async Task<User> SeedUserAsync(string clerkId, AccountState state = AccountState.Active)
    {
        await using var context = CreateContext();
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "State",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.BoutiqueOwner,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = state,
        };
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }

    private static async Task<Guid> SeedMembershipAsync(Guid userId)
    {
        await using var context = CreateContext();
        var org = new Organization
        {
            Name = $"State Org {Guid.NewGuid():N}",
            Slug = $"state-{Guid.NewGuid():N}",
            OwnerUserId = userId,
        };
        context.Organizations.Add(org);
        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = userId,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();
        return org.Id;
    }

    private string CreateToken(string clerkId, string? userRole = null, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));

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
    public async Task PatchProfile_UpdatesTheEditableFields()
    {
        var clerkId = $"profile_{Guid.NewGuid():N}";
        await SeedUserAsync(clerkId);
        var token = CreateToken(clerkId, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, "/api/v1/users/me", token,
            new { displayName = "Kavindu", phoneNumber = "+94771111111", pushNotificationsEnabled = true }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal("Kavindu", body.GetProperty("displayName").GetString());
        Assert.Equal("+94771111111", body.GetProperty("phoneNumber").GetString());
        Assert.True(body.GetProperty("pushNotificationsEnabled").GetBoolean());
    }

    [Fact]
    public async Task DeleteAccount_SoftDeletesAndRevokesMembershipsAndKeys()
    {
        var clerkId = $"delete_{Guid.NewGuid():N}";
        var user = await SeedUserAsync(clerkId);
        _ = await SeedMembershipAsync(user.Id);
        var token = CreateToken(clerkId, orgRole: Roles.BoutiqueOwner);

        Guid keyId;
        await using (var context = CreateContext())
        {
            var generated = ApiKeyCredentials.Generate(ApiKeyEnvironment.Live);
            var key = new ApiKey
            {
                OrganizationId = (await context.Organizations.FirstAsync()).Id,
                Name = "Delete Key",
                Prefix = generated.Prefix,
                KeyHash = generated.Hash,
                Scopes = [Permissions.CatalogView],
                CreatedByUserId = user.Id,
            };
            context.ApiKeys.Add(key);
            await context.SaveChangesAsync();
            keyId = key.Id;
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Delete, "/api/v1/users/me", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using (var context = CreateContext())
        {
            // The User model carries a soft-delete query filter; bypass it to assert the row.
            var stored = await context.Users.IgnoreQueryFilters().SingleAsync(u => u.Id == user.Id);
            Assert.NotNull(stored.DeletedAt);
            Assert.False(stored.IsActive);
            Assert.Equal(AccountState.Suspended, stored.AccountState);

            Assert.Empty(await context.OrganizationMemberships.Where(m => m.UserId == user.Id).ToListAsync());
            Assert.Equal(ApiKeyStatus.Revoked, (await context.ApiKeys.SingleAsync(k => k.Id == keyId)).Status);
        }
    }

    [Fact]
    public async Task AdminUserSearch_RequiresTheAdminRole()
    {
        var clerkId = $"search_owner_{Guid.NewGuid():N}";
        await SeedUserAsync(clerkId);
        var token = CreateToken(clerkId, userRole: Roles.BoutiqueOwner, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/users", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminUserSearch_AsAdmin_ReturnsUsers()
    {
        var adminClerk = $"admin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        await SeedUserAsync($"target_{Guid.NewGuid():N}");
        var token = CreateToken(adminClerk, userRole: Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/users?page=1&pageSize=50", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.True(body.GetProperty("total").GetInt32() >= 2);
    }

    [Fact]
    public async Task AdminStateChange_AppliesTheTransitionMatrix()
    {
        var adminClerk = $"stateadmin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        var target = await SeedUserAsync($"statetarget_{Guid.NewGuid():N}", AccountState.OnboardingPending);
        var token = CreateToken(adminClerk, userRole: Roles.Admin);

        var activate = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/admin/users/{target.Id}/state", token,
            new { accountState = "Active" }));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);

        var suspend = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/admin/users/{target.Id}/state", token,
            new { accountState = "Suspended" }));
        Assert.Equal(HttpStatusCode.OK, suspend.StatusCode);

        // Suspended -> OnboardingPending is not a permitted transition.
        var invalid = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/admin/users/{target.Id}/state", token,
            new { accountState = "OnboardingPending" }));
        Assert.Equal(HttpStatusCode.Conflict, invalid.StatusCode);
    }

    [Fact]
    public async Task AdminUserSearch_BindsAccountState_PreferentiallyOverTheStateAlias()
    {
        var adminClerk = $"filteradmin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        var marker = Guid.NewGuid().ToString("N");
        await SeedUserAsync($"filter_suspended_{marker}", AccountState.Suspended);
        await SeedUserAsync($"filter_active_{marker}", AccountState.Active);
        var token = CreateToken(adminClerk, userRole: Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/users?accountState=Suspended&q={marker}", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal("Suspended", body.GetProperty("items")[0].GetProperty("accountState").GetString());
    }

    [Fact]
    public async Task AdminUserSearch_StillAcceptsTheLegacyStateAlias()
    {
        var adminClerk = $"aliasadmin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        var marker = Guid.NewGuid().ToString("N");
        await SeedUserAsync($"alias_suspended_{marker}", AccountState.Suspended);
        await SeedUserAsync($"alias_active_{marker}", AccountState.Active);
        var token = CreateToken(adminClerk, userRole: Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/users?state=Suspended&q={marker}", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal("Suspended", body.GetProperty("items")[0].GetProperty("accountState").GetString());
    }

    [Fact]
    public async Task AdminUserSearch_FiltersByOrganizationThroughTheMembershipTable()
    {
        var adminClerk = $"orgfilteradmin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        var marker = Guid.NewGuid().ToString("N");
        var member = await SeedUserAsync($"org_member_{marker}", AccountState.Active);
        await SeedUserAsync($"org_other_{marker}", AccountState.Active);
        var organizationId = await SeedMembershipAsync(member.Id);

        var token = CreateToken(adminClerk, userRole: Roles.Admin);
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/users?organizationId={organizationId}&q={marker}",
            token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal(member.Id, body.GetProperty("items")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task AdminUserSearch_DefaultsToPageSize50_AndClampsTo200()
    {
        var adminClerk = $"pageadmin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        await SeedUserAsync($"page_target_{Guid.NewGuid():N}");
        var token = CreateToken(adminClerk, userRole: Roles.Admin);

        var defaults = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/users", token));
        Assert.Equal(HttpStatusCode.OK, defaults.StatusCode);
        var defaultBody = await BodyAsync(defaults);
        Assert.Equal(50, defaultBody.GetProperty("pageSize").GetInt32());

        var clamped = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/users?pageSize=1000", token));
        var clampedBody = await BodyAsync(clamped);
        Assert.Equal(200, clampedBody.GetProperty("pageSize").GetInt32());
    }

    [Fact]
    public async Task AdminStateChange_PersistsTheReasonOnTheAuditRow()
    {
        var adminClerk = $"auditadmin_{Guid.NewGuid():N}";
        await SeedUserAsync(adminClerk);
        var target = await SeedUserAsync($"audittarget_{Guid.NewGuid():N}", AccountState.OnboardingPending);
        var token = CreateToken(adminClerk, userRole: Roles.Admin);
        const string reason = "Support verified the account and restored access.";

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/admin/users/{target.Id}/state", token,
            new { accountState = "Active", reason }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        await using var context = CreateContext();
        var audit = await context.AuditLogEntries
            .Where(entry => entry.EntityId == target.Id.ToString())
            .OrderByDescending(entry => entry.CreatedAt)
            .FirstAsync();
        Assert.Equal(AuditAction.UserStateChanged, audit.Action);
        Assert.Equal(reason, audit.Reason);
    }

    [Fact]
    public async Task Sessions_AreProxiedToClerk()
    {
        var clerkId = $"sessions_{Guid.NewGuid():N}";
        await SeedUserAsync(clerkId);
        var token = CreateToken(clerkId, orgRole: Roles.BoutiqueOwner);

        var list = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/users/me/sessions", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = await BodyAsync(list);
        Assert.Equal(1, listBody.GetArrayLength());
        Assert.Equal("sess_1", listBody[0].GetProperty("id").GetString());

        var revoke = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/users/me/sessions/revoke-all", token));
        Assert.Equal(HttpStatusCode.OK, revoke.StatusCode);
        Assert.Equal(1, _clerk.RevokeCalls);
    }
}
