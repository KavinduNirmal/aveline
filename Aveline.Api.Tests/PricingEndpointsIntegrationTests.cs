using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #186 — admin pricing endpoints (docs/api/README.md §C.1).
/// </summary>
public class PricingEndpointsIntegrationTests : IAsyncLifetime
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

    private static async Task SeedUserAsync(string clerkId, string userRole)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Pricing",
            LastName = "Tester",
            Username = clerkId,
            UserRole = userRole,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
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

    private static object CreateRuleBody(string scopeKind = "Global", string? provider = null, string? model = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["scopeKind"] = scopeKind,
            ["provider"] = provider,
            ["model"] = model,
            ["unitsPerBlossom"] = 1200,
            ["minimumChargeBlossoms"] = 0.1m,
            ["roundingMode"] = "Ceiling",
            ["roundingDecimals"] = 1,
            ["effectiveFrom"] = DateTime.UtcNow.AddDays(1).ToString("O"),
            ["changeReason"] = "Pricing endpoint integration test rule.",
        };
        return body;
    }

    [Fact]
    public async Task GetRules_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync("/api/v1/admin/pricing/rules");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateRule_AsBoutiqueOwner_Returns403()
    {
        await SeedUserAsync("pricing_boutique_owner", Roles.Staff);
        var token = CreateToken("pricing_boutique_owner", orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, CreateRuleBody()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateRule_AsAdmin_ReturnsCreatedAndDraft()
    {
        await SeedUserAsync("pricing_admin", Roles.Admin);
        var token = CreateToken("pricing_admin", userRole: Roles.Admin);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, CreateRuleBody()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Draft", body.GetProperty("status").GetString());
        Assert.Equal(1200, body.GetProperty("unitsPerBlossom").GetInt32());
    }

    [Fact]
    public async Task CreateRule_GlobalScopeWithProvider_Returns400()
    {
        await SeedUserAsync("pricing_admin_scope", Roles.Admin);
        var token = CreateToken("pricing_admin_scope", userRole: Roles.Admin);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
                CreateRuleBody("Global", provider: "openai")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("scope-inconsistent", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateRule_OmittedOptionalValues_BindTheDocumentedDefaults()
    {
        await SeedUserAsync("pricing_admin_defaults", Roles.Admin);
        var token = CreateToken("pricing_admin_defaults", userRole: Roles.Admin);

        var body = new Dictionary<string, object?>
        {
            ["scopeKind"] = "Global",
            ["unitsPerBlossom"] = 1200,
            ["effectiveFrom"] = DateTime.UtcNow.AddDays(1).ToString("O"),
            ["changeReason"] = "Rule that relies on the documented DTO defaults.",
        };

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(0.1m, created.GetProperty("minimumChargeBlossoms").GetDecimal());
        Assert.Equal(1, created.GetProperty("roundingDecimals").GetInt32());
    }

    [Fact]
    public async Task CreateRule_InvalidRoundingDecimals_Returns400ValidationCode()
    {
        await SeedUserAsync("pricing_admin_validation", Roles.Admin);
        var token = CreateToken("pricing_admin_validation", userRole: Roles.Admin);

        var body = CreateRuleBody();
        ((Dictionary<string, object?>)body)["roundingDecimals"] = 9;

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, body));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var error = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("validation", error.GetProperty("code").GetString());
        Assert.False(string.IsNullOrWhiteSpace(error.GetProperty("message").GetString()));
    }

    [Fact]
    public async Task ActivateRule_OnNonDraft_Returns400()
    {
        await SeedUserAsync("pricing_activate_non_draft", Roles.Admin);
        var token = CreateToken("pricing_activate_non_draft", userRole: Roles.Admin);
        var provider = $"non-draft-{Guid.CreateVersion7():N}";

        var create = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
            CreateRuleBody("Provider", provider: provider)));
        var ruleId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var first = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/pricing/rules/{ruleId}/activate", token));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/pricing/rules/{ruleId}/activate", token));

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        var error = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("rule-not-draft", error.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CancelRule_OnDraft_Returns200()
    {
        await SeedUserAsync("pricing_cancel_draft", Roles.Admin);
        var token = CreateToken("pricing_cancel_draft", userRole: Roles.Admin);
        var provider = $"cancel-draft-{Guid.CreateVersion7():N}";

        var create = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
            CreateRuleBody("Provider", provider: provider)));
        var ruleId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var cancel = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/pricing/rules/{ruleId}/cancel", token,
            new { reason = "Abandoned before activation." }));

        Assert.Equal(HttpStatusCode.OK, cancel.StatusCode);
        var body = JsonDocument.Parse(await cancel.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Cancelled", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateRule_Backdated_AsAdmin_Returns403()
    {
        await SeedUserAsync("pricing_admin_backdate", Roles.Admin);
        var token = CreateToken("pricing_admin_backdate", userRole: Roles.Admin);

        var body = new Dictionary<string, object?>
        {
            ["scopeKind"] = "Global",
            ["unitsPerBlossom"] = 1200,
            ["minimumChargeBlossoms"] = 0.1m,
            ["roundingMode"] = "Ceiling",
            ["roundingDecimals"] = 1,
            ["effectiveFrom"] = DateTime.UtcNow.AddDays(-1).ToString("O"),
            ["changeReason"] = "Backdated rule without the backdate permission.",
        };

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, body));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateRule_Backdated_AsOwner_ReturnsCreated()
    {
        await SeedUserAsync("pricing_owner_backdate", Roles.Owner);
        var token = CreateToken("pricing_owner_backdate", userRole: Roles.Owner);

        var body = new Dictionary<string, object?>
        {
            ["scopeKind"] = "Global",
            ["unitsPerBlossom"] = 1200,
            ["minimumChargeBlossoms"] = 0.1m,
            ["roundingMode"] = "Ceiling",
            ["roundingDecimals"] = 1,
            ["effectiveFrom"] = DateTime.UtcNow.AddDays(-1).ToString("O"),
            ["changeReason"] = "Backdated rule with the backdate permission.",
        };

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ListAndActivateRule_AsAdmin_Succeeds()
    {
        await SeedUserAsync("pricing_admin_lifecycle", Roles.Admin);
        var token = CreateToken("pricing_admin_lifecycle", userRole: Roles.Admin);

        var create = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/rules", token, CreateRuleBody()));
        var created = JsonDocument.Parse(await create.Content.ReadAsStringAsync()).RootElement;
        var ruleId = created.GetProperty("id").GetString();

        var list = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/admin/pricing/rules", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var listBody = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.True(listBody.GetProperty("total").GetInt32() >= 1);

        var activate = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/admin/pricing/rules/{ruleId}/activate", token));
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var activated = JsonDocument.Parse(await activate.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Active", activated.GetProperty("status").GetString());
    }

    [Fact]
    public async Task GetRule_Unknown_Returns404()
    {
        await SeedUserAsync("pricing_admin_missing", Roles.Admin);
        var token = CreateToken("pricing_admin_missing", userRole: Roles.Admin);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/admin/pricing/rules/{Guid.CreateVersion7()}", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PriceBook_CreateAndList_AsAdmin()
    {
        await SeedUserAsync("pricing_admin_pricebook", Roles.Admin);
        var token = CreateToken("pricing_admin_pricebook", userRole: Roles.Admin);

        var body = new Dictionary<string, object?>
        {
            ["skuKind"] = "TopUpPack",
            ["skuCode"] = "blossom_pack_500",
            ["blossomQuantity"] = 500,
            ["priceLkr"] = 2500.00m,
            ["effectiveFrom"] = DateTime.UtcNow.AddDays(1).ToString("O"),
            ["changeReason"] = "Initial top-up pack price.",
        };

        var create = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/price-book", token, body));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);

        var list = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/admin/pricing/price-book", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var items = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        Assert.True(items.GetArrayLength() >= 1);
    }

    [Fact]
    public async Task ActivateRule_WithEffectiveFromBody_HonoursTheOverride()
    {
        await SeedUserAsync("pricing_activate_override", Roles.Admin);
        var token = CreateToken("pricing_activate_override", userRole: Roles.Admin);
        var provider = $"effective-from-{Guid.CreateVersion7():N}";

        var create = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
            CreateRuleBody("Provider", provider: provider)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var ruleId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var effectiveFrom = new DateTime(2030, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var activate = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/admin/pricing/rules/{ruleId}/activate",
            token,
            new { effectiveFrom }));

        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var activated = JsonDocument.Parse(await activate.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(effectiveFrom, activated.GetProperty("effectiveFrom").GetDateTime());
    }

    [Theory]
    [InlineData(Roles.BoutiqueOwner)]
    [InlineData(Roles.BoutiqueManager)]
    public async Task PricingReads_AsBoutiqueRoles_Return403(string orgRole)
    {
        var clerkId = $"pricing_read_{orgRole.Replace(':', '_')}";
        await SeedUserAsync(clerkId, Roles.Staff);
        var token = CreateToken(clerkId, orgRole: orgRole);

        var paths = new[]
        {
            "/api/v1/admin/pricing/rules",
            $"/api/v1/admin/pricing/rules/{Guid.CreateVersion7()}",
            "/api/v1/admin/pricing/price-book",
            $"/api/v1/admin/pricing/price-book/{Guid.CreateVersion7()}",
        };

        foreach (var path in paths)
        {
            var response = await _client.SendAsync(Authorized(HttpMethod.Get, path, token));
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task PricingReads_AsTeamAdmin_Return200()
    {
        await SeedUserAsync("pricing_read_team_admin", Roles.Admin);
        var token = CreateToken("pricing_read_team_admin", userRole: Roles.Admin);

        var rules = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/admin/pricing/rules", token));
        Assert.Equal(HttpStatusCode.OK, rules.StatusCode);

        var priceBook = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/admin/pricing/price-book", token));
        Assert.Equal(HttpStatusCode.OK, priceBook.StatusCode);
    }

    [Fact]
    public async Task GetPriceEntry_IsScopedToTheOwningOrganization()
    {
        await SeedUserAsync("pricing_read_org_admin", Roles.Admin);
        var token = CreateToken("pricing_read_org_admin", userRole: Roles.Admin);
        var owningOrganization = Guid.CreateVersion7();
        var otherOrganization = Guid.CreateVersion7();

        var body = new Dictionary<string, object?>
        {
            ["organizationId"] = owningOrganization,
            ["skuKind"] = "PlanAllowance",
            ["blossomQuantity"] = 100,
            ["priceLkr"] = 990.00m,
            ["effectiveFrom"] = DateTime.UtcNow.AddDays(1).ToString("O"),
            ["changeReason"] = "Per-organization price entry for tenant isolation.",
        };

        var create = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/pricing/price-book", token, body));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var entryId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var crossOrganization = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/pricing/price-book/{entryId}?organizationId={otherOrganization}",
            token));
        Assert.Equal(HttpStatusCode.NotFound, crossOrganization.StatusCode);

        var owning = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/pricing/price-book/{entryId}?organizationId={owningOrganization}",
            token));
        Assert.Equal(HttpStatusCode.OK, owning.StatusCode);
    }

    [Fact]
    public async Task ActivateRule_WithPastEffectiveFrom_AsAdmin_Returns403()
    {
        await SeedUserAsync("pricing_activate_backdate_admin", Roles.Admin);
        var token = CreateToken("pricing_activate_backdate_admin", userRole: Roles.Admin);
        var provider = $"activate-backdate-{Guid.CreateVersion7():N}";

        var create = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
            CreateRuleBody("Provider", provider: provider)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var ruleId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        // An Admin is deliberately denied pricing:backdate; activating with a past date must
        // not silently re-price a historical window (BR-1.6, reconciliation §3.0).
        var activate = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/admin/pricing/rules/{ruleId}/activate",
            token,
            new { effectiveFrom = DateTime.UtcNow.AddDays(-1) }));

        Assert.Equal(HttpStatusCode.Forbidden, activate.StatusCode);
    }

    [Fact]
    public async Task ActivateRule_WithPastEffectiveFrom_AsOwner_Returns200()
    {
        await SeedUserAsync("pricing_activate_backdate_owner", Roles.Owner);
        var token = CreateToken("pricing_activate_backdate_owner", userRole: Roles.Owner);
        var provider = $"activate-backdate-owner-{Guid.CreateVersion7():N}";

        var create = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
            CreateRuleBody("Provider", provider: provider)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var ruleId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var effectiveFrom = DateTime.UtcNow.AddDays(-1);
        var activate = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/admin/pricing/rules/{ruleId}/activate",
            token,
            new { effectiveFrom }));

        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        var activated = JsonDocument.Parse(await activate.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(
            effectiveFrom,
            activated.GetProperty("effectiveFrom").GetDateTime(),
            TimeSpan.FromSeconds(1));
    }

    [Fact]
    public async Task UpdateRule_WithPastEffectiveFrom_AsAdmin_Returns403()
    {
        await SeedUserAsync("pricing_patch_backdate_admin", Roles.Admin);
        var token = CreateToken("pricing_patch_backdate_admin", userRole: Roles.Admin);
        var provider = $"patch-backdate-{Guid.CreateVersion7():N}";

        var create = await _client.SendAsync(Authorized(
            HttpMethod.Post, "/api/v1/admin/pricing/rules", token,
            CreateRuleBody("Provider", provider: provider)));
        Assert.Equal(HttpStatusCode.Created, create.StatusCode);
        var ruleId = JsonDocument.Parse(await create.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var patch = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/admin/pricing/rules/{ruleId}",
            token,
            new Dictionary<string, object?>
            {
                ["effectiveFrom"] = DateTime.UtcNow.AddDays(-1).ToString("O"),
            }));

        Assert.Equal(HttpStatusCode.Forbidden, patch.StatusCode);
    }
}
