using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.DTOs;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T5 — the two tenant reads the billing surface was missing: the top-up pack catalogue (E-11) and
/// the billing-period history (E-12).
/// </summary>
public class TenantBillingReadsTests
{
    private readonly AppDbContext _context;
    private readonly Guid _orgId;

    public TenantBillingReadsTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"BillingReads_{Guid.NewGuid()}")
            .Options);
        _orgId = SeedOrganizationAsync().GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"br_{ownerId:N}", Email = "br@aveline.lk",
            FirstName = "B", LastName = "R", Username = $"br_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Billing Reads", Slug = $"br-{ownerId:N}", OwnerUserId = ownerId,
            PlanTier = PlanTier.Bloom,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    // ── E-11: the top-up pack catalogue ──────────────────────────────────────────────────────────

    /// <summary>
    /// The catalogue must offer **exactly** what the purchase accepts. Both sides call
    /// <c>IPricingService.ListPriceEntriesAsync(TopUpPack, planTier: null, organizationId: null)</c>
    /// and then resolve the SKU through the one shared selector,
    /// <c>PriceBookSelection.SelectActiveSku</c> — the purchase for the code it was asked for, the
    /// catalogue for every code it lists — so a SKU the catalogue shows cannot be rejected at
    /// purchase and a SKU the purchase accepts cannot be missing from the list. The active-only,
    /// effective-window rule the lookup needs lives in that selector, applied once, rather than
    /// being re-spelled at each call site.
    /// </summary>
    [Fact]
    public void TheCatalogueAndThePurchaseUseTheSameLookup()
    {
        var source = File.ReadAllText(Path.Combine(
            RepositoryRoot, "Aveline.Api", "Modules", "Billing", "Endpoints", "BlossomEndpoints.cs"));
        var selection = File.ReadAllText(Path.Combine(
            RepositoryRoot, "Aveline.Api", "Modules", "Billing", "Domain", "PriceResolution.cs"));

        // Read each route on its own, so "both use it" is proven per route rather than by a literal
        // that happens to appear somewhere in the file.
        var purchase = RouteBody(source, "MapPost(\"/top-ups\"", "MapGet(\"/top-up-packs\"");
        var catalogue = RouteBody(source, "MapGet(\"/top-up-packs\"", "private static void MapAdminEndpoints");

        // One call shape, spelled the same way on both routes...
        Assert.Contains("BlossomSkuKind.TopUpPack, planTier: null, organizationId: null", purchase);
        Assert.Contains("BlossomSkuKind.TopUpPack, planTier: null, organizationId: null", catalogue);

        // ...resolving through the shared selector, never through a comparison a route re-spells for
        // itself. Inlining the lookup back into either route drops that route's call and fails here.
        Assert.Contains("PriceBookSelection.SelectActiveSku(", purchase);
        Assert.Contains("PriceBookSelection.SelectActiveSku(", catalogue);
        Assert.DoesNotContain("SkuCode ==", source);

        // The active-only, effective-window predicate the old inline comparison carried is now the
        // selector's own rule, shared by both routes instead of copied into each.
        Assert.Contains("BlossomRuleStatus.Active", selection);
    }

    /// <summary>
    /// The source between two markers, so an assertion can be scoped to one route's body. Both
    /// markers must still exist: a rename is a change to the contract this test guards.
    /// </summary>
    private static string RouteBody(string source, string start, string end)
    {
        var from = source.IndexOf(start, StringComparison.Ordinal);
        Assert.True(from >= 0, $"the source no longer contains '{start}'");

        var to = source.IndexOf(end, from + start.Length, StringComparison.Ordinal);
        Assert.True(to >= 0, $"the source no longer contains '{end}' after '{start}'");

        return source[from..to];
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null
                   && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                directory = directory.Parent;
            }

            Assert.NotNull(directory);
            return directory!.FullName;
        }
    }

    // ── E-12: the billing-period history ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ThePeriodHistory_ReturnsClosedPeriodsNewestFirst()
    {
        await SeedPeriodAsync(new DateTime(2026, 7, 1, 0, 0, 0, DateTimeKind.Utc), closed: true);
        await SeedPeriodAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), closed: true);
        await SeedPeriodAsync(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), closed: false);

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        Assert.Equal(3, periods.Count);
        // Newest first, and the open period is included but flagged rather than omitted.
        Assert.Equal(new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc), periods[0].PeriodStart);
        Assert.False(periods[0].IsClosed);
        Assert.True(periods[1].IsClosed);
    }

    [Fact]
    public async Task ThePeriodHistory_HonoursTheTakeLimit()
    {
        for (var month = 0; month < 6; month++)
        {
            await SeedPeriodAsync(
                new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMonths(month), closed: true);
        }

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 3);

        Assert.Equal(3, periods.Count);
    }

    /// <summary>
    /// C-4, the trap this whole field exists for. `OrganizationSubscription.PriceLkr` is **never
    /// assigned anywhere** — creation and upsert both omit it and the snapshot job copies the same
    /// `0m` — so "no row ⇒ null, else the column" would print **`LKR 0` as a plan price** for every
    /// existing subscription. The rule is instead that the field is `null` whenever the price is
    /// zero, with a flag saying prices are not configured.
    /// </summary>
    [Fact]
    public async Task ThePlanListPrice_IsNullWhenTheColumnIsZero_NeverZero()
    {
        await SeedPeriodAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), closed: true, planPrice: 0m);

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        var period = Assert.Single(periods);
        Assert.Null(period.PlanListPriceLkr);
        Assert.False(period.SubscriptionPricesConfigured);
    }

    [Fact]
    public async Task ThePlanListPrice_IsReportedWhenTheColumnActuallyHoldsAPrice()
    {
        // The rule is about a zero being unassigned, not about refusing to show a real number.
        await SeedPeriodAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), closed: true, planPrice: 9000m);

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        var period = Assert.Single(periods);
        Assert.Equal(9000m, period.PlanListPriceLkr);
        Assert.True(period.SubscriptionPricesConfigured);
    }

    [Fact]
    public async Task ThePeriodHistory_IsScopedToTheOrganization()
    {
        var otherOwner = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = otherOwner, ClerkId = $"br2_{otherOwner:N}", Email = "br2@aveline.lk",
            FirstName = "B2", LastName = "R2", Username = $"br2_{otherOwner:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var otherOrg = new Organization
        {
            Name = "Other", Slug = $"br2-{otherOwner:N}", OwnerUserId = otherOwner,
        };
        _context.Organizations.Add(otherOrg);
        await _context.SaveChangesAsync();

        _context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = otherOrg.Id,
            PeriodStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc),
            PeriodEnd = new DateTime(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc),
            MonthlyBlossomLimit = 150m,
            IsClosed = true,
            ClosedAt = DateTime.UtcNow,
        });
        await SeedPeriodAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), closed: true);

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        Assert.Single(periods);
    }

    [Fact]
    public async Task ThePeriodHistory_UsesGrantedAdjustedAndUsedSeparately()
    {
        await SeedPeriodAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), closed: true,
            granted: 100m, adjusted: -20m, used: 30m, limit: 150m);

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        var period = Assert.Single(periods);
        // Each column reported in its own right, so an adjustment is never folded invisibly into a
        // grant.
        Assert.Equal(150m, period.MonthlyBlossomLimit);
        Assert.Equal(100m, period.BlossomGranted);
        Assert.Equal(-20m, period.BlossomAdjusted);
        Assert.Equal(30m, period.BlossomUsed);
        Assert.Equal(200m, period.BlossomRemaining);
    }

    [Fact]
    public async Task ThePeriodHistory_CountsTopUpsFromTheLedger()
    {
        var periodStart = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
        await SeedPeriodAsync(periodStart, closed: true);
        _context.BlossomLedgerEntries.AddRange(
            new BlossomLedgerEntry
            {
                OrganizationId = _orgId,
                EntryType = BlossomLedgerEntryType.TopUpGrant,
                BlossomDelta = 500m,
                Reason = "Top-up purchase pack-500.",
                CreatedAt = periodStart.AddDays(3),
            },
            new BlossomLedgerEntry
            {
                OrganizationId = _orgId,
                EntryType = BlossomLedgerEntryType.TopUpGrant,
                BlossomDelta = 1000m,
                Reason = "Top-up purchase pack-1000.",
                CreatedAt = periodStart.AddDays(9),
            });
        await _context.SaveChangesAsync();

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        var period = Assert.Single(periods);
        Assert.Equal(2, period.TopUpCount);
        Assert.Equal(1500, period.TopUpBlossoms);
    }

    [Fact]
    public async Task ThePeriodHistory_WithNoSubscriptionRow_ReportsNoPlanRatherThanAFreeOne()
    {
        await SeedPeriodAsync(new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc), closed: true,
            withSubscription: false);

        var periods = await TenantBillingReadService.GetPeriodsAsync(_context, _orgId, take: 12);

        var period = Assert.Single(periods);
        Assert.Null(period.PlanTier);
        Assert.Null(period.PlanListPriceLkr);
        Assert.False(period.HasSubscriptionRow);
    }

    // ── helpers ──────────────────────────────────────────────────────────────────────────────────

    private async Task SeedPeriodAsync(
        DateTime periodStart,
        bool closed,
        decimal planPrice = 0m,
        decimal granted = 0m,
        decimal adjusted = 0m,
        decimal used = 0m,
        decimal limit = 150m,
        bool withSubscription = true)
    {
        var periodEnd = periodStart.AddMonths(1);
        _context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = _orgId,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            MonthlyBlossomLimit = limit,
            BlossomGranted = granted,
            BlossomAdjusted = adjusted,
            BlossomUsed = used,
            BlossomRemaining = limit + granted + adjusted - used,
            IsClosed = closed,
            ClosedAt = closed ? periodEnd : null,
        });

        if (withSubscription)
        {
            _context.OrganizationSubscriptions.Add(new OrganizationSubscription
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = _orgId,
                PlanTier = PlanTier.Bloom,
                SeatsIncluded = 3,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStart = periodStart,
                CurrentPeriodEnd = periodEnd,
                PriceLkr = planPrice,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            });
        }

        // A snapshot carries the same `PriceLkr` the subscription does, so a period built from a
        // snapshot hits the same zero a second time.
        _context.OrganizationSubscriptionSnapshots.Add(
            new Aveline.Api.Modules.Analytics.Models.OrganizationSubscriptionSnapshot
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = _orgId,
                SnapshotDay = periodStart,
                PlanTier = PlanTier.Bloom,
                HasBillingRow = withSubscription,
                SeatsIncluded = 3,
                PriceLkr = withSubscription ? planPrice : 0m,
                CreatedAt = DateTime.UtcNow,
            });

        await _context.SaveChangesAsync();
    }
}
