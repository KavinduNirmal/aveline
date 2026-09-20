using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R2 (issue #343) — the three administrative write routes, end to end.
///
/// The suite exists for the permission split as much as for the happy path: a <c>moderator</c>
/// reads revenue and must not move it, an <c>admin</c> corrects the ledger and must not refund it,
/// and only an <c>owner</c> does all three. Every route is idempotency-guarded, so the replay
/// behaviour is pinned against a directly-constructed <see cref="AppDbContext"/> rather than through
/// the API's own response.
/// </summary>
public class RevenueEndpointsIntegrationTests : IAsyncLifetime
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
                builder.UseSetting("Telemetry:Enabled", "false");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: DatabaseName)
            .Options);

    private string CreateToken(string clerkId, string? userRole = null, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));
        // The org-scoped policies resolve the caller's membership from the Clerk `org_role` claim,
        // so a boutique-owned route needs it alongside `user_role`.
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Audience = "aveline-api",
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        });
    }

    private HttpRequestMessage Authorized(
        HttpMethod method, string path, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null) request.Content = JsonContent.Create(body);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);

        return request;
    }

    /// <summary>Seeds a team user and returns their Clerk id, so the actor resolves to a real row.</summary>
    private static async Task<string> SeedTeamUserAsync(string suffix, string role)
    {
        var clerkId = $"revenue_{role}_{suffix}";
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Revenue",
            LastName = role,
            Username = clerkId,
            UserRole = role,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
        return clerkId;
    }

    private static async Task<Guid> SeedOrganizationAsync(string suffix)
    {
        await using var context = Context();
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"rev_org_owner_{suffix}", Email = $"rev_{suffix}@aveline.lk",
            FirstName = "Rev", LastName = "Owner", Username = $"rev_org_owner_{suffix}",
            UserRole = Roles.BoutiqueOwner, OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true, AccountState = AccountState.Active,
        });
        var org = new Organization
        {
            Name = $"Revenue Org {suffix}", Slug = $"revenue-{suffix}", OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private static object VerifyBody(Guid organizationId, string sourceRef, decimal amount = 5000m) => new
    {
        organizationId,
        sourceKind = "SubscriptionBilling",
        sourceRef,
        amount,
        reason = "Payment received against the September charge.",
    };

    private static object RefundBody(Guid organizationId, string sourceRef, decimal amount = 1500m) => new
    {
        organizationId,
        sourceKind = "SubscriptionBilling",
        sourceRef,
        amount,
        reason = "Refund issued after a service credit.",
    };

    private static object AdjustBody(Guid organizationId, decimal amount = 250m) => new
    {
        organizationId,
        amount,
        reason = "Rounding correction against the ledger.",
    };

    private const string VerifyPath = "/api/v1/admin/revenue/ledger/verify";
    private const string RefundPath = "/api/v1/admin/revenue/ledger/refund";
    private const string AdjustPath = "/api/v1/admin/revenue/ledger/adjust";

    // ── Authentication ──────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task WithoutAToken_AllThreeWritesReturn401()
    {
        foreach (var path in new[] { VerifyPath, RefundPath, AdjustPath })
        {
            var response = await _client.PostAsJsonAsync(path, new { });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>
    /// A `moderator` reads revenue (they hold `revenue:read`) and must not move it. This is the
    /// split the `MoneyRead` / `MoneyOperations` pair exists to express.
    /// </summary>
    [Fact]
    public async Task Moderator_IsRefusedOnAllThreeWrites()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Moderator);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Moderator);

        var responses = new[]
        {
            await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
                VerifyBody(orgId, $"ref-{suffix}"), $"k-{suffix}-1")),
            await _client.SendAsync(Authorized(HttpMethod.Post, RefundPath, token,
                RefundBody(orgId, $"ref-{suffix}"), $"k-{suffix}-2")),
            await _client.SendAsync(Authorized(HttpMethod.Post, AdjustPath, token,
                AdjustBody(orgId), $"k-{suffix}-3")),
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }
    }

    [Fact]
    public async Task Owner_MayVerifyAdjustAndRefund()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);
        var sourceRef = $"ref-{suffix}";

        var verify = await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
            VerifyBody(orgId, sourceRef), $"k-{suffix}-1"));
        Assert.Equal(HttpStatusCode.Created, verify.StatusCode);

        var adjust = await _client.SendAsync(Authorized(HttpMethod.Post, AdjustPath, token,
            AdjustBody(orgId), $"k-{suffix}-2"));
        Assert.Equal(HttpStatusCode.Created, adjust.StatusCode);

        var refund = await _client.SendAsync(Authorized(HttpMethod.Post, RefundPath, token,
            RefundBody(orgId, sourceRef), $"k-{suffix}-3"));
        Assert.Equal(HttpStatusCode.Created, refund.StatusCode);
    }

    /// <summary>
    /// `admin` holds the whole catalog minus the named denials, and `revenue:refund` is one of them:
    /// the authority to correct the ledger is not the authority to send money back.
    /// </summary>
    [Fact]
    public async Task Admin_MayVerifyAndAdjust_ButIsRefusedARefund()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Admin);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Admin);
        var sourceRef = $"ref-{suffix}";

        var verify = await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
            VerifyBody(orgId, sourceRef), $"k-{suffix}-1"));
        Assert.Equal(HttpStatusCode.Created, verify.StatusCode);

        var adjust = await _client.SendAsync(Authorized(HttpMethod.Post, AdjustPath, token,
            AdjustBody(orgId), $"k-{suffix}-2"));
        Assert.Equal(HttpStatusCode.Created, adjust.StatusCode);

        var refund = await _client.SendAsync(Authorized(HttpMethod.Post, RefundPath, token,
            RefundBody(orgId, sourceRef), $"k-{suffix}-3"));
        Assert.Equal(HttpStatusCode.Forbidden, refund.StatusCode);
    }

    // ── Idempotency ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task MissingIdempotencyKey_IsRefused()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);

        var response = await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
            VerifyBody(orgId, $"ref-{suffix}")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("idempotency-key-required", body.RootElement.GetProperty("code").GetString());
    }

    /// <summary>
    /// A replayed key must return the stored response and write **nothing** second time. Asserted
    /// against the store directly, because the API's own response would look identical either way.
    /// </summary>
    [Fact]
    public async Task ReplayedKey_WritesOneRowAndSignalsTheReplay()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);
        var key = $"replay-{suffix}";
        var body = VerifyBody(orgId, $"ref-{suffix}");

        var first = await _client.SendAsync(
            Authorized(HttpMethod.Post, VerifyPath, token, body, key));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.SendAsync(
            Authorized(HttpMethod.Post, VerifyPath, token, body, key));

        Assert.True(second.IsSuccessStatusCode);
        Assert.Equal("true", second.Headers.GetValues("Idempotency-Replayed").Single());

        await using var verify = Context();
        var rows = await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == orgId)
            .ToListAsync();
        Assert.Single(rows);
    }

    // ── Contracts ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Verify_ReturnsTheCreatedEntryWithItsWireShape()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);

        var response = await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
            VerifyBody(orgId, $"ref-{suffix}", amount: 7500m), $"k-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        // camelCase, because ASP.NET Core serialises with `JsonSerializerDefaults.Web`.
        Assert.Equal("SubscriptionCharge", root.GetProperty("kind").GetString());
        Assert.Equal("Verified", root.GetProperty("chargeBasis").GetString());
        Assert.Equal("Recorded", root.GetProperty("status").GetString());
        Assert.Equal(7500m, root.GetProperty("amount").GetDecimal());
        Assert.Equal("LKR", root.GetProperty("currency").GetString());
        // The actor is resolved from the Clerk subject, so an audit trail has a real user id.
        Assert.NotEqual(Guid.Empty, root.GetProperty("recordedByUserId").GetGuid());
    }

    /// <summary>
    /// A refund against a charge nobody confirmed is refused with a code, rather than recorded as a
    /// negative total: you cannot return money you never recorded receiving.
    /// </summary>
    [Fact]
    public async Task RefundAgainstADerivedOnlyCharge_Is409()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);
        var sourceRef = $"ref-{suffix}";

        // A derived charge with no verified counterpart, written directly as the rollover would.
        await using (var context = Context())
        {
            context.IncomeLedgerEntries.Add(new IncomeLedgerEntry
            {
                OrganizationId = orgId,
                Kind = IncomeEntryKind.SubscriptionCharge,
                SourceKind = IncomeSourceKind.SubscriptionBilling,
                SourceRef = sourceRef,
                ChargeBasis = IncomeChargeBasis.Derived,
                Status = IncomeEntryStatus.Recorded,
                Amount = 5000m,
                Reason = "Plan charge for 2026-09.",
                OccurredAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(Authorized(HttpMethod.Post, RefundPath, token,
            RefundBody(orgId, sourceRef), $"k-{suffix}"));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("refund-not-allowed", body.RootElement.GetProperty("code").GetString());

        // Scoped to this organization: the integration host shares one in-memory database across
        // tests, so an unscoped assertion would read another test's rows.
        await using var verify = Context();
        var rows = await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == orgId)
            .ToListAsync();
        Assert.DoesNotContain(rows, entry => entry.Kind == IncomeEntryKind.Refund);
        // And the derived charge is untouched, rather than voided by a failed refund.
        Assert.Single(rows, entry => entry.Status == IncomeEntryStatus.Recorded
            && entry.ChargeBasis == IncomeChargeBasis.Derived);
    }

    [Fact]
    public async Task Verify_TwiceForTheSameReference_NullsTheDerivedCharge()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);
        var sourceRef = $"ref-{suffix}";

        await using (var context = Context())
        {
            context.IncomeLedgerEntries.Add(new IncomeLedgerEntry
            {
                OrganizationId = orgId,
                Kind = IncomeEntryKind.SubscriptionCharge,
                SourceKind = IncomeSourceKind.SubscriptionBilling,
                SourceRef = sourceRef,
                ChargeBasis = IncomeChargeBasis.Derived,
                Status = IncomeEntryStatus.Recorded,
                Amount = 5000m,
                Reason = "Plan charge for 2026-09.",
                OccurredAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
            VerifyBody(orgId, sourceRef), $"k-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var verify = Context();
        var rows = await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == orgId)
            .ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Single(rows, entry => entry.ChargeBasis == IncomeChargeBasis.Derived
            && entry.Status == IncomeEntryStatus.Voided);
        Assert.Single(rows, entry => entry.ChargeBasis == IncomeChargeBasis.Verified
            && entry.Status == IncomeEntryStatus.Recorded);
    }

    [Fact]
    public async Task AValuelessBody_Is400()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);

        var response = await _client.SendAsync(Authorized(HttpMethod.Post, VerifyPath, token,
            new { organizationId = orgId, amount = 0m, reason = "short" }, $"k-{suffix}"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    /// <summary>
    /// Every money verb lands in the same Audit Explorer the entitlement journal writes to, and the
    /// actor on the row is a real user id rather than a placeholder.
    /// </summary>
    [Fact]
    public async Task EachWriteLeavesAnAuditRowWithARealActor()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var clerkId = await SeedTeamUserAsync(suffix, Roles.Owner);
        var orgId = await SeedOrganizationAsync(suffix);
        var token = CreateToken(clerkId, Roles.Owner);
        var sourceRef = $"ref-{suffix}";

        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(Authorized(
            HttpMethod.Post, VerifyPath, token, VerifyBody(orgId, sourceRef), $"k-{suffix}-v"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(Authorized(
            HttpMethod.Post, AdjustPath, token, AdjustBody(orgId), $"k-{suffix}-a"))).StatusCode);
        Assert.Equal(HttpStatusCode.Created, (await _client.SendAsync(Authorized(
            HttpMethod.Post, RefundPath, token, RefundBody(orgId, sourceRef), $"k-{suffix}-r"))).StatusCode);

        await using var verify = Context();
        var actions = await verify.AuditLogEntries
            .Where(row => row.OrganizationId == orgId)
            .Select(row => row.Action)
            .ToListAsync();

        Assert.Contains("revenue.ledger.verified", actions);
        Assert.Contains("revenue.ledger.adjusted", actions);
        Assert.Contains("revenue.ledger.refunded", actions);

        var rows = await verify.AuditLogEntries
            .Where(row => row.OrganizationId == orgId)
            .ToListAsync();
        Assert.All(rows, row =>
        {
            Assert.Equal(nameof(IncomeLedgerEntry), row.EntityType);
            Assert.NotNull(row.ActorUserId);
            Assert.NotEqual(Guid.Empty, row.ActorUserId);
        });
    }

    /// <summary>
    /// Seeds an Active top-up SKU, so the top-up route can resolve a price and a Blossom quantity.
    /// </summary>
    private static async Task SeedTopUpSkuAsync(string skuCode, decimal blossomQuantity, decimal priceLkr)
    {
        await using var context = Context();
        context.BlossomPriceEntries.Add(new Modules.Billing.Models.BlossomPriceEntry
        {
            SkuKind = Modules.Billing.Models.BlossomSkuKind.TopUpPack,
            SkuCode = skuCode,
            BlossomQuantity = blossomQuantity,
            PriceLkr = priceLkr,
            Status = Modules.Billing.Models.BlossomRuleStatus.Active,
            EffectiveFrom = DateTime.UtcNow.AddDays(-1),
        });
        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Seeds a boutique whose owner holds `org:boutique_owner`, so the **org-scoped** top-up route
    /// admits them. The org-scoped policies resolve their target from the route value
    /// `organizationId` and require an Active membership, so a team user cannot drive this route.
    /// </summary>
    private static async Task<(Guid OrgId, string ClerkId)> SeedTopUpBoutiqueAsync(string suffix)
    {
        var clerkId = $"revenue_boutique_owner_{suffix}";
        await using var context = Context();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Boutique",
            LastName = "Owner",
            Username = clerkId,
            UserRole = Roles.BoutiqueOwner,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"TopUp Org {suffix}", Slug = $"topup-{suffix}", OwnerUserId = owner.Id,
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
        return (org.Id, clerkId);
    }

    /// <summary>
    /// The top-up writer, and the rule that keeps it from inventing revenue.
    /// </summary>
    /// <remarks>
    /// There is no payment-provider client in this repository, so a top-up grant is not a charge —
    /// `docs/api/README.md` §C.2 says so outright. What a top-up *does* give us is a provider session
    /// reference, which is the operator's evidence that money changed hands, so the income row is
    /// written only when that reference is present. A free grant, or a granted pack with no
    /// reference, writes nothing.
    /// </remarks>
    [Fact]
    public async Task ATopUpWithAPaymentReference_WritesADerivedIncomeRow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedTopUpBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);
        var reference = $"pay-{suffix}";

        await SeedTopUpSkuAsync($"pack_{suffix}", blossomQuantity: 500m, priceLkr: 9000m);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{orgId}/blossoms/top-ups",
            token,
            new { skuCode = $"pack_{suffix}", paymentReference = reference },
            $"k-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verify = Context();
        var income = await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == orgId)
            .ToListAsync();
        var row = Assert.Single(income);
        Assert.Equal(IncomeEntryKind.TopUpPurchase, row.Kind);
        Assert.Equal(IncomeSourceKind.BlossomTopUp, row.SourceKind);
        // Derived, not Verified: a reference is evidence of a session, not proof of settlement.
        Assert.Equal(IncomeChargeBasis.Derived, row.ChargeBasis);
        Assert.Equal(reference, row.SourceRef);
        Assert.Equal(9000m, row.Amount);
    }

    /// <summary>
    /// The case that stops revenue being invented: a grant with no payment reference writes no
    /// income row at all.
    /// </summary>
    [Fact]
    public async Task ATopUpWithoutAPaymentReference_WritesNoIncomeRow()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var (orgId, clerkId) = await SeedTopUpBoutiqueAsync(suffix);
        var token = CreateToken(clerkId, Roles.BoutiqueOwner, Roles.BoutiqueOwner);

        await SeedTopUpSkuAsync($"pack_{suffix}", blossomQuantity: 500m, priceLkr: 9000m);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{orgId}/blossoms/top-ups",
            token,
            new { skuCode = $"pack_{suffix}" },
            $"k-{suffix}"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var verify = Context();
        Assert.Empty(await verify.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == orgId)
            .ToListAsync());
    }
}
