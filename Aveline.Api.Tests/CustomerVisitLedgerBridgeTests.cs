using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 — the **counter-sale writer** onto the boutique income ledger.
///
/// This is the writer that closes the gap the plan's §2.4 names as the single most important
/// finding: <c>CustomerVisitService.RecordAsync</c> read <c>PurchaseTotal</c>, wrote a
/// <c>CustomerInteraction</c> that has **no amount column**, and incremented
/// <c>Customer.TotalSpent</c> — so a cash sale left a counter with no row behind it and the shop
/// could not answer "what did we take this week, and how". The visit now writes a
/// <c>Sale</c>/<c>Verified</c> entry in the same operation.
/// </summary>
public class CustomerVisitLedgerBridgeTests
{
    private readonly AppDbContext _context;
    private readonly BoutiqueSaleLedgerService _ledger;
    private readonly CustomerVisitService _visits;

    public CustomerVisitLedgerBridgeTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"VisitBridge_{Guid.NewGuid()}")
            .Options);
        _ledger = new BoutiqueSaleLedgerService(_context, NullLogger<BoutiqueSaleLedgerService>.Instance);
        _visits = new CustomerVisitService(_context, _ledger);
    }

    private async Task<(Guid OrgId, Guid CustomerId, Guid ActorId)> SeedAsync()
    {
        var actorId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = actorId, ClerkId = $"a_{actorId:N}", Email = "a@aveline.lk",
            FirstName = "A", LastName = "B", Username = $"a_{actorId:N}",
            UserRole = "staff", OrganizationRole = "org:boutique_staff",
        });
        var org = new Organization
        {
            Name = "Bridge Boutique", Slug = $"br-{actorId:N}", OwnerUserId = actorId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = "+94771111222",
            FullName = "Counter Client",
            Status = "new",
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();

        return (org.Id, customer.Id, actorId);
    }

    private static RecordCustomerInteractionRequest Visit(decimal? purchaseTotal) => new(
        OccurredAtUtc: DateTime.UtcNow.AddMinutes(-5),
        Channel: "in_person",
        Direction: "inbound",
        Note: "Bought a saree at the counter.",
        PurchaseTotal: purchaseTotal);

    [Fact]
    public async Task AVisitWithAnAmount_WritesOneVerifiedSaleEntry()
    {
        var (orgId, customerId, actorId) = await SeedAsync();

        var receipt = await _visits.RecordAsync(orgId, customerId, actorId, Visit(18500m));
        Assert.NotNull(receipt);

        var entries = await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync();
        var entry = Assert.Single(entries);
        Assert.Equal(orgId, entry.OrganizationId);
        Assert.Equal(BoutiqueSaleEntryKind.Sale, entry.Kind);
        // A person asserted this amount, so it is `Verified`, not `Derived`: this money was taken.
        Assert.Equal(BoutiqueSaleChargeBasis.Verified, entry.ChargeBasis);
        Assert.Equal(BoutiqueSaleSourceKind.CounterWalkIn, entry.SourceKind);
        Assert.Equal(18500m, entry.Amount);
        Assert.Equal(customerId, entry.CustomerId);
        Assert.Equal(actorId, entry.RecordedByUserId);
        // The dedup identity is the interaction, so a retried visit cannot double-book.
        Assert.Equal($"interaction:{receipt!.VisitId}", entry.SourceRef);
    }

    [Fact]
    public async Task AVisitWithNoAmount_WritesNoEntry()
    {
        var (orgId, customerId, actorId) = await SeedAsync();

        await _visits.RecordAsync(orgId, customerId, actorId, Visit(null));
        await _visits.RecordAsync(orgId, customerId, actorId, Visit(0m));

        // A visit that took no money is a visit, not a sale. An entry of zero would be a fabricated
        // measurement, and the ledger's check constraint would refuse it anyway.
        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task AMessagingInteractionWithAnAmount_StillWritesASaleEntry()
    {
        // The amount is money taken whichever channel recorded it; only the *visit counter* is
        // restricted to an inbound in-person interaction.
        var (orgId, customerId, actorId) = await SeedAsync();
        var request = Visit(4000m) with { Channel = "whatsapp" };

        var receipt = await _visits.RecordAsync(orgId, customerId, actorId, request);

        Assert.False(receipt!.CountedAsVisit);
        var entry = Assert.Single(await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync());
        Assert.Equal(4000m, entry.Amount);
    }

    [Fact]
    public async Task TheSaleEntryOccurredWhenTheVisitDid_NotWhenTheRowWasWritten()
    {
        var (orgId, customerId, actorId) = await SeedAsync();
        var occurredAt = DateTime.UtcNow.AddHours(-6);
        var request = Visit(9000m) with { OccurredAtUtc = occurredAt };

        await _visits.RecordAsync(orgId, customerId, actorId, request);

        var entry = await _context.BoutiqueSaleEntries.AsNoTracking().SingleAsync();
        // `OccurredAt` is when the money moved; `RecordedAt` is when the row was written. A register
        // ordered by the wrong one puts a back-dated sale at the top of today.
        Assert.Equal(occurredAt, entry.OccurredAt, TimeSpan.FromSeconds(1));
        Assert.True(entry.RecordedAt >= occurredAt);
    }

    [Fact]
    public async Task AVisitForAClientInAnotherOrganization_WritesNoEntry()
    {
        var mine = await SeedAsync();
        var theirs = await SeedAsync();

        var receipt = await _visits.RecordAsync(
            mine.OrgId, theirs.CustomerId, mine.ActorId, Visit(1000m));

        Assert.Null(receipt);
        // Nothing is written for a client that is not in this boutique, including the ledger row.
        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }
}
