using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
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
/// Issue #208 — the tenant isolation matrix (R-4, acceptance criterion M3). A valid token
/// for organization A must never get a 2xx for organization B's resources on any new
/// tenant endpoint. There is no EF global tenant filter, so this is the highest-value
/// control against a cross-tenant leak.
/// </summary>
public class TenantIsolationTests : IAsyncLifetime
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
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
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
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private static async Task<(Guid OrgId, Guid UserId, string ClerkId)> SeedAsync(string suffix)
    {
        await using var context = CreateContext();
        var id = Guid.CreateVersion7();
        var owner = new User
        {
            Id = id,
            ClerkId = $"tenant_owner_{suffix}",
            Email = $"tenant_{suffix}@aveline.lk",
            FirstName = "Tenant",
            LastName = "Owner",
            Username = $"tenant_{suffix}",
            UserRole = Roles.BoutiqueOwner,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Tenant Org {suffix}",
            Slug = $"tenant-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = PlanTier.Rose,
        };
        context.Organizations.Add(org);
        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();
        return (org.Id, owner.Id, owner.ClerkId);
    }

    private string CreateToken(string clerkId) =>
        CreateToken(clerkId, Roles.BoutiqueOwner);

    private string CreateToken(string clerkId, string orgRole)
    {
        var claims = new List<Claim> { new("sub", clerkId), new("org_role", orgRole) };
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

    private HttpRequestMessage Request(HttpMethod method, string path, string token, object? body = null)
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

    public static IEnumerable<object[]> TenantEndpoints()
    {
        yield return [HttpMethod.Get, "/settings", null!];
        yield return [HttpMethod.Patch, "", new { name = "Hijacked" }];
        yield return [HttpMethod.Get, "/members", null!];
        yield return [HttpMethod.Get, "/api-keys", null!];
        yield return [HttpMethod.Post, "/api-keys", new { name = "x", scopes = new[] { "catalog:view" } }];
        yield return [HttpMethod.Get, "/blossoms/balance", null!];
        yield return [HttpMethod.Get, "/blossoms/statement", null!];
        yield return [HttpMethod.Get, "/blossoms/usage", null!];
        yield return [HttpMethod.Get, "/subscription", null!];
        yield return [HttpMethod.Get, "/entitlements", null!];
        yield return [HttpMethod.Get, "/entitlements/usage", null!];
    }

    [Theory]
    [MemberData(nameof(TenantEndpoints))]
    public async Task TokenForOrgA_NeverReachesOrgB(HttpMethod method, string suffix, object? body)
    {
        var (orgA, _, clerkA) = await SeedAsync($"a-{Guid.NewGuid():N}");
        var (orgB, _, _) = await SeedAsync($"b-{Guid.NewGuid():N}");
        _ = orgA;
        var token = CreateToken(clerkA);

        var response = await _client.SendAsync(
            Request(method, $"/api/v1/orgs/{orgB}{suffix}", token, body));

        Assert.True(
            response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Forbidden,
            $"{method} /orgs/{{orgB}}{suffix} returned {(int)response.StatusCode}; expected 403/404.");
    }

    [Theory]
    [MemberData(nameof(TenantEndpoints))]
    public async Task TokenForOrgA_StillReachesOrgA(HttpMethod method, string suffix, object? body)
    {
        var (orgA, _, clerkA) = await SeedAsync($"own-{Guid.NewGuid():N}");
        var token = CreateToken(clerkA);

        var response = await _client.SendAsync(
            Request(method, $"/api/v1/orgs/{orgA}{suffix}", token, body));

        // The point of this half of the matrix is that the isolation above is not because
        // the route is simply broken for everyone: a legitimate call must not 403/404.
        Assert.DoesNotContain(
            response.StatusCode,
            new[] { HttpStatusCode.Forbidden, HttpStatusCode.NotFound });
    }

    /// <summary>
    /// The privacy data-subject routes are a different isolation shape: they are <b>anonymous by
    /// design</b> (the OTP is the authentication, plan §7.1), so the authenticated-token matrix above
    /// cannot cover them. What it can cover, and what the matrix is extended to assert, is that the
    /// <c>organizationId</c> in the body is not a way to reach another tenant's data: no caller,
    /// token or not, gets a 2xx without a verified code. The positive cross-org proof (a valid OTP
    /// for org A returns zero org B rows and deletes nothing in org B) lives in
    /// <c>PrivacyDataSubjectRightsTests</c> and <c>PrivacyErasurePostgresTests</c> (R-17).
    /// </summary>
    public static IEnumerable<object[]> AnonymousDataSubjectRoutes()
    {
        yield return ["/api/v1/privacy/data/export"];
        yield return ["/api/v1/privacy/data/delete"];
    }

    [Theory]
    [MemberData(nameof(AnonymousDataSubjectRoutes))]
    public async Task AnUnverifiedDataSubjectCallNeverReachesATenant(string route)
    {
        var (orgB, _, clerkB) = await SeedAsync($"privacy-b-{Guid.NewGuid():N}");
        var token = CreateToken(clerkB);

        // No token at all: the route is anonymous, so the answer is a validation failure, not 401.
        var anonymous = await _client.PostAsJsonAsync(route, new
        {
            organizationId = orgB,
            phoneNumber = "+94771234567",
            handle = "bm90LWEtaGFuZGxl",
            otp = "000000",
            confirm = "DELETE",
            scope = "org",
            idempotencyKey = "matrix-key",
        });

        Assert.NotEqual(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, anonymous.StatusCode);

        // The same body with a token must not be treated as an authenticated tenant call either:
        // a token does not prove the phone, so the OTP gate still refuses.
        var authenticated = await _client.SendAsync(
            Request(HttpMethod.Post, route, token, new
            {
                organizationId = orgB,
                phoneNumber = "+94771234567",
                handle = "bm90LWEtaGFuZGxl",
                otp = "000000",
                confirm = "DELETE",
                scope = "org",
                idempotencyKey = "matrix-key",
            }));

        Assert.Equal(HttpStatusCode.BadRequest, authenticated.StatusCode);
    }
}
