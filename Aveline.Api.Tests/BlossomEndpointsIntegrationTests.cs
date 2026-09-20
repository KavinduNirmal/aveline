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
/// Issues #194 and #195 — organisation Blossom endpoints and administrator operations
/// (docs/api/README.md §C.2).
/// </summary>
public class BlossomEndpointsIntegrationTests : IAsyncLifetime
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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private static async Task<(Guid OrgId, string OwnerClerkId)> SeedBoutiqueAsync(string suffix)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"blossom_owner_{suffix}",
            Email = $"blossom_{suffix}@aveline.lk",
            FirstName = "Blossom",
            LastName = "Owner",
            Username = $"blossom_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Blossom Org {suffix}",
            Slug = $"blossom-{suffix}",
            OwnerUserId = owner.Id,
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

        return (org.Id, owner.ClerkId);
    }

    private static async Task SeedAdminAsync(string clerkId)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Admin",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a boutique whose only member holds <paramref name="boutiqueRole"/>, so a
    /// role's own-org read can be exercised without an owner in the picture.
    /// </summary>
    private static async Task<(Guid OrgId, string ClerkId)> SeedMemberAsync(
        string suffix, string boutiqueRole)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var member = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"blossom_member_{suffix}",
            Email = $"blossom_member_{suffix}@aveline.lk",
            FirstName = "Blossom",
            LastName = "Member",
            Username = $"blossom_member_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Blossom Member Org {suffix}",
            Slug = $"blossom-member-{suffix}",
            OwnerUserId = member.Id,
        };
        context.Organizations.Add(org);

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = member.Id,
            BoutiqueRole = boutiqueRole,
            Status = MembershipStatus.Active,
        });
        await context.SaveChangesAsync();

        return (org.Id, member.ClerkId);
    }

    [Fact]
    public async Task GetBalance_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/orgs/{Guid.CreateVersion7()}/blossoms/balance");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(Roles.BoutiqueStaff)]
    [InlineData(Roles.BoutiqueManager)]
    [InlineData(Roles.BoutiqueSupervisor)]
    [InlineData(Roles.BoutiqueOwner)]
    public async Task GetBalance_AsOrgMember_ReturnsOk(string boutiqueRole)
    {
        // Decision D1 (b): every org role holds the self-service read, so an
        // associate at the counter can see the shop's position. This was 403 for
        // `boutique_staff` before the permission existed.
        var suffix = "self_" + boutiqueRole.Replace(":", "_");
        var (orgId, clerkId) = await SeedMemberAsync(suffix, boutiqueRole);
        var token = CreateToken(clerkId, orgRole: boutiqueRole);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgId}/blossoms/balance", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetBalance_AsOrgMemberOfAnotherOrg_ReturnsForbidden()
    {
        // The decisive proof that the balance route is org-scoped rather than
        // authorized by the JWT's role claim.
        var (_, staffClerk) = await SeedMemberAsync("wrongorg_a", Roles.BoutiqueStaff);
        var (otherOrgId, _) = await SeedMemberAsync("wrongorg_b", Roles.BoutiqueOwner);
        var token = CreateToken(staffClerk, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{otherOrgId}/blossoms/balance", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetBalance_AsOwner_ReturnsProjection()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("balance");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgId}/blossoms/balance", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(150m, body.GetProperty("monthlyBlossomLimit").GetDecimal());
        Assert.Equal(150m, body.GetProperty("blossomRemaining").GetDecimal());
        Assert.True(body.TryGetProperty("lowBalanceThresholdPercent", out _));
    }

    [Fact]
    public async Task GetBalance_AsOutsider_ReturnsForbidden()
    {
        var (orgId, _) = await SeedBoutiqueAsync("outsider");
        await SeedAdminAsync("blossom_outsider");
        var token = CreateToken("blossom_outsider", orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{orgId}/blossoms/balance", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCredit_AsBoutiqueOwner_ReturnsForbidden()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("adminforbidden");

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit",
            CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner),
            new { amount = 100m, reason = "A valid credit reason." }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AdminCredit_ThenDebit_AdjustsBalance()
    {
        var (orgId, _) = await SeedBoutiqueAsync("adjust");
        await SeedAdminAsync("blossom_admin");
        var token = CreateToken("blossom_admin", userRole: Roles.Admin);

        var credit = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit", token,
            new { amount = 250m, reason = "Goodwill credit for the outage." },
            idempotencyKey: "credit-1"));

        Assert.Equal(HttpStatusCode.Created, credit.StatusCode);

        var debit = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/debit", token,
            new { amount = 100m, reason = "Correction of an earlier credit." },
            idempotencyKey: "debit-1"));

        Assert.Equal(HttpStatusCode.Created, debit.StatusCode);
        var debitBody = JsonDocument.Parse(await debit.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(-100m, debitBody.GetProperty("blossomDelta").GetDecimal());
        Assert.Equal(300m, debitBody.GetProperty("blossomBalanceAfter").GetDecimal());
    }

    [Fact]
    public async Task AdminDebit_BeyondAvailable_Returns409WithAmounts()
    {
        var (orgId, _) = await SeedBoutiqueAsync("overdraft");
        await SeedAdminAsync("blossom_admin_overdraft");
        var token = CreateToken("blossom_admin_overdraft", userRole: Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/debit", token,
            new { amount = 500m, reason = "Overdraft attempt for test." },
            idempotencyKey: "debit-overdraft"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("insufficient-balance", body.GetProperty("code").GetString());
        Assert.Equal(150m, body.GetProperty("available").GetDecimal());
    }

    [Fact]
    public async Task AdminCredit_WithoutIdempotencyKey_Returns400WithCode()
    {
        var (orgId, _) = await SeedBoutiqueAsync("nokey");
        await SeedAdminAsync("blossom_admin_nokey");
        var token = CreateToken("blossom_admin_nokey", userRole: Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit", token,
            new { amount = 100m, reason = "Missing idempotency key." }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("idempotency-key-required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task AdminDebit_FailedOperation_IsNotReplayedOnRetry()
    {
        var (orgId, _) = await SeedBoutiqueAsync("failedreplay");
        await SeedAdminAsync("blossom_admin_failedreplay");
        var token = CreateToken("blossom_admin_failedreplay", userRole: Roles.Admin);
        var body = new { amount = 500m, reason = "Debit before any top-up exists." };

        var first = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/debit", token, body, "failed-replay-1"));
        Assert.Equal(HttpStatusCode.Conflict, first.StatusCode);

        // Make the same logical operation possible, then retry the same key.
        var credit = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit", token,
            new { amount = 500m, reason = "Top-up after the failed debit." }, "failed-replay-credit"));
        Assert.Equal(HttpStatusCode.Created, credit.StatusCode);

        var retry = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/debit", token, body, "failed-replay-1"));

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.False(retry.Headers.Contains("Idempotency-Replayed"));
    }

    [Fact]
    public async Task AdminCredit_ReplayedWithSameKey_ReturnsReplayHeader()
    {
        var (orgId, _) = await SeedBoutiqueAsync("replay");
        await SeedAdminAsync("blossom_admin_replay");
        var token = CreateToken("blossom_admin_replay", userRole: Roles.Admin);
        var body = new { amount = 200m, reason = "Idempotent credit for the test." };

        var first = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit", token, body, "replay-1"));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var second = await _client.SendAsync(Authorized(
            HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit", token, body, "replay-1"));

        Assert.Equal(HttpStatusCode.Created, second.StatusCode);
        Assert.True(second.Headers.TryGetValues("Idempotency-Replayed", out var values));
        Assert.Equal("true", values!.First());
        var secondId = JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();
        Assert.Equal(firstId, secondId);
    }

    [Fact]
    public async Task ConcurrentCredits_WithTheSameKey_ExecuteExactlyOnce()
    {
        var (orgId, _) = await SeedBoutiqueAsync("concurrent");
        await SeedAdminAsync("blossom_admin_concurrent");
        var token = CreateToken("blossom_admin_concurrent", userRole: Roles.Admin);
        var body = new { amount = 300m, reason = "Concurrent idempotent credit." };

        var requests = Enumerable.Range(0, 2)
            .Select(_ => Authorized(
                HttpMethod.Post, $"/api/v1/admin/orgs/{orgId}/blossoms/credit", token, body, "concurrent-1"))
            .ToArray();

        var responses = await Task.WhenAll(requests.Select(request => _client.SendAsync(request)));

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Created, response.StatusCode));
        // Exactly one request executed; the other must have replayed its stored response.
        Assert.Single(responses, response => response.Headers.Contains("Idempotency-Replayed"));

        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        // The per-key lease serialises the pair, so the second request replays the first
        // response instead of crediting twice (§3.2).
        var credits = await context.BlossomLedgerEntries
            .Where(entry => entry.OrganizationId == orgId
                            && entry.EntryType != BlossomLedgerEntryType.PeriodAllocation)
            .ToListAsync();
        var credit = Assert.Single(credits);
        Assert.Equal(300m, credit.BlossomDelta);
    }

    [Fact]
    public async Task Statement_AsOwner_ReportsConsistentReconciliation()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("statement");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/blossoms/statement", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("reconciliation").GetProperty("isConsistent").GetBoolean());
        Assert.Equal(0m, body.GetProperty("reconciliation").GetProperty("drift").GetDecimal());

        // R4 (issue #345): the response now states what it rests on, and the effective window cap, so
        // a client can say "reconciliation status unknown" rather than assuming consistency.
        var quality = body.GetProperty("dataQuality");
        Assert.True(quality.GetProperty("openingBalanceFromProjection").GetBoolean());
        Assert.True(quality.GetProperty("reconciliationChecked").GetBoolean());
        Assert.Equal(400, body.GetProperty("maxWindowDays").GetInt32());
        Assert.Equal(400, quality.GetProperty("maxWindowDays").GetInt32());
    }

    /// <summary>
    /// The statement's window now matches the 400-day retention S-3 claims, rather than the 92 the
    /// endpoint used to cap at, and a request beyond it is rejected **naming the effective limit**.
    /// </summary>
    [Fact]
    public async Task Statement_WindowBeyondTheCap_Is400NamingTheEffectiveLimit()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("statement-window");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{orgId}/blossoms/statement"
            + "?from=2025-01-01T00:00:00Z&to=2026-12-31T00:00:00Z",
            token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("400", body.GetProperty("message").GetString());
    }

    /// <summary>
    /// A page size outside the range is rejected naming it. A caller who asked for 500 rows and
    /// received 200 would page on a false assumption, so this is not silently clamped.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(500)]
    public async Task Statement_PageSizeOutsideTheRange_Is400NamingTheRange(int pageSize)
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync($"statement-page-{pageSize}");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{orgId}/blossoms/statement?pageSize={pageSize}",
            token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var message = body.GetProperty("message").GetString()!;
        Assert.Contains("1", message);
        Assert.Contains("200", message);
    }

    [Fact]
    public async Task Statement_UnrecognisedEntryType_Is400RatherThanIgnored()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("statement-entrytype");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{orgId}/blossoms/statement?entryType=NotARealType",
            token));

        // Silently dropping the filter would return the whole window under a request that asked for
        // a subset, which is worse than refusing it.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Statement_UnrecognisedKind_Is400()
    {
        var (orgId, ownerClerk) = await SeedBoutiqueAsync("statement-kind");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{orgId}/blossoms/statement?kind=everything",
            token));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// The new filters are accepted together and narrow the merged source, and a consumption row now
    /// carries the detail that explains it.
    /// </summary>
    [Fact]
    public async Task Statement_FiltersCombine_AndConsumptionCarriesItsDetail()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, ownerClerk) = await SeedBoutiqueAsync($"statement-filters-{suffix}");
        var token = CreateToken(ownerClerk, orgRole: Roles.BoutiqueOwner);

        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb").Options))
        {
            context.AiUsageRecords.Add(new Modules.Billing.Models.AiUsageRecord
            {
                OrganizationId = orgId,
                RequestId = $"req-{suffix}",
                WorkflowId = $"wf-{suffix}",
                Provider = "openai",
                Model = "gpt-4o",
                InputTokens = 900,
                OutputTokens = 100,
                BlossomUnits = 4m,
                ActualCostUsd = 0.0075m,
                CreatedAt = DateTime.UtcNow.AddMinutes(-5),
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{orgId}/blossoms/statement"
            + "?kind=consumption&q=gpt-4o&page=1&pageSize=25",
            token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, body.GetProperty("total").GetInt32());

        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.Equal("Consumption", item.GetProperty("kind").GetString());
        Assert.Equal("openai", item.GetProperty("provider").GetString());
        Assert.Equal("gpt-4o", item.GetProperty("model").GetString());
        Assert.Equal(1000, item.GetProperty("normalizedUnits").GetInt64());
        Assert.Equal(0.0075m, item.GetProperty("actualCostUsd").GetDecimal());
    }
}
