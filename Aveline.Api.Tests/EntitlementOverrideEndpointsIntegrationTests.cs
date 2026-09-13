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
/// Issue #241 / FR-4.9 — per-organization entitlement overrides for Enterprise
/// contracts, behind <c>billing:adjust</c>, returning the resolved entitlements.
/// </summary>
public class EntitlementOverrideEndpointsIntegrationTests : IAsyncLifetime
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

    private static async Task<(Guid OrgId, string ClerkId)> SeedOrganizationAsync(
        string suffix, PlanTier tier = PlanTier.Enterprise)
    {
        await using var context = CreateContext();
        var clerkId = $"ent_override_{suffix}_{Guid.NewGuid():N}";
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"ent_override_{suffix}@aveline.lk",
            FirstName = "Entitlement",
            LastName = "Admin",
            Username = $"ent_override_{suffix}",
            UserRole = Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Entitlement Override {suffix}",
            Slug = $"ent-override-{suffix}",
            OwnerUserId = owner.Id,
            PlanTier = tier,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return (org.Id, clerkId);
    }

    private string CreateToken(string clerkId, string? userRole)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
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
    public async Task Admin_SetsOverride_AndResolvedEntitlementsReflectIt()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, clerkId) = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/admin/orgs/{orgId}/entitlement-overrides",
            token,
            new
            {
                overrides = new[]
                {
                    new
                    {
                        key = "blossoms.monthly",
                        valueType = "Decimal",
                        value = 25000m,
                        reason = "Negotiated Enterprise contract.",
                    },
                },
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        var entitlements = body.GetProperty("entitlements").EnumerateArray().ToArray();
        var resolved = entitlements.Single(e => e.GetProperty("key").GetString() == "blossoms.monthly");
        Assert.Equal("Override", resolved.GetProperty("source").GetString());
        Assert.Equal(25000m, resolved.GetProperty("value").GetDecimal());

        await using var context = CreateContext();
        var stored = await context.PlanEntitlementOverrides
            .Where(o => o.OrganizationId == orgId)
            .ToListAsync();
        var row = Assert.Single(stored);
        Assert.Equal("blossoms.monthly", row.Key);
        Assert.Equal(EntitlementValueType.Decimal, row.ValueType);
        Assert.Equal(25000m, row.ValueDecimal);

        var audited = await context.AuditLogEntries
            .Where(e => e.OrganizationId == orgId)
            .ToListAsync();
        var auditEntry = Assert.Single(audited);
        Assert.Equal("entitlement.override.updated", auditEntry.Action);
        Assert.Equal("PlanEntitlementOverride", auditEntry.EntityType);
    }

    [Fact]
    public async Task Admin_OverridesAreUpserted_OnTheSameEffectiveKey()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, clerkId) = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Admin);
        var effectiveFrom = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        object Body(decimal value) => new
        {
            overrides = new[]
            {
                new
                {
                    key = "staff.max",
                    valueType = "Integer",
                    value,
                    reason = "Contract amendment.",
                    effectiveFrom,
                },
            },
        };

        var first = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/admin/orgs/{orgId}/entitlement-overrides", token, Body(50m)));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.SendAsync(Authorized(
            HttpMethod.Patch, $"/api/v1/admin/orgs/{orgId}/entitlement-overrides", token, Body(75m)));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);

        await using var context = CreateContext();
        var row = Assert.Single(await context.PlanEntitlementOverrides
            .Where(o => o.OrganizationId == orgId)
            .ToListAsync());
        Assert.Equal(75m, row.ValueDecimal);
    }

    [Fact]
    public async Task BoutiqueOwner_IsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, _) = await SeedOrganizationAsync(suffix);
        var token = CreateToken($"boutique_owner_{suffix}", Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/admin/orgs/{orgId}/entitlement-overrides",
            token,
            new
            {
                overrides = new[]
                {
                    new { key = "staff.max", valueType = "Integer", value = 5m, reason = "No." },
                },
            }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task InvalidKey_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, clerkId) = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/admin/orgs/{orgId}/entitlement-overrides",
            token,
            new
            {
                overrides = new[]
                {
                    new { key = "not.a.real.key", valueType = "Decimal", value = 1m, reason = "Bad key." },
                },
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task InvalidValueType_Returns400()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgId, clerkId) = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/admin/orgs/{orgId}/entitlement-overrides",
            token,
            new
            {
                overrides = new[]
                {
                    new { key = "staff.max", valueType = "Nope", value = 1m, reason = "Bad type." },
                },
            }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownOrganization_Returns404()
    {
        var token = CreateToken($"ent_override_missing_{Guid.NewGuid():N}", Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/admin/orgs/{Guid.CreateVersion7()}/entitlement-overrides",
            token,
            new
            {
                overrides = new[]
                {
                    new { key = "staff.max", valueType = "Integer", value = 5m, reason = "Missing org." },
                },
            }));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }
}
