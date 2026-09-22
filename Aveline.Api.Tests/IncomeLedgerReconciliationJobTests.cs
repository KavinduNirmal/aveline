using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Jobs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// T3 — the ledger's reconciliation job.
///
/// The job exists because a ledger write can fail **after** the business event succeeded. A payment
/// confirmation returns 200 because the money moved; if the ledger row was never written, the
/// register understates the shop's takings and nothing else will notice. The job is the repair.
///
/// It is safe to run repeatedly by construction: the ledger's filtered unique index on
/// `(OrganizationId, SourceKind, SourceRef)` makes a second repair attempt a no-op rather than a
/// duplicate row. **The repair count is surfaced**, because a job that silently fixes a persistent
/// writer bug is worse than one that fails: the count is the signal that something upstream is
/// broken.
/// </summary>
public class IncomeLedgerReconciliationJobTests
{
    private readonly ServiceProvider _provider;
    private readonly AppDbContext _context;
    private readonly IncomeLedgerReconciliationJob _job;
    private readonly Guid _orgId;

    public IncomeLedgerReconciliationJobTests()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Recon_{Guid.NewGuid()}")
            .Options);
        services.AddSingleton(_context);
        services.AddSingleton<IDistributedJobLock>(new InMemoryDistributedJobLock(TimeSpan.FromMinutes(5)));
        services.AddScoped<IBoutiqueSaleLedgerService, BoutiqueSaleLedgerService>();
        services.AddScoped<IncomeLedgerReconciliationJob>();
        _provider = services.BuildServiceProvider();
        _job = _provider.GetRequiredService<IncomeLedgerReconciliationJob>();

        _orgId = SeedOrganizationAsync().GetAwaiter().GetResult();
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"rc_{ownerId:N}", Email = "rc@aveline.lk",
            FirstName = "R", LastName = "C", Username = $"rc_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Reconcile Boutique", Slug = $"rc-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private async Task<Guid> SeedPaymentAsync(
        string status = "confirmed",
        decimal amount = 12500m,
        DateTime? confirmedAt = null)
    {
        var payment = new Payment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = _orgId,
            OrderId = Guid.CreateVersion7(),
            Amount = amount,
            PaymentType = "full",
            PaymentMethod = "cash",
            Status = status,
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ConfirmedAt = status == "confirmed" ? confirmedAt ?? DateTime.UtcNow.AddDays(-1) : null,
        };
        _context.Payments.Add(payment);
        await _context.SaveChangesAsync();
        return payment.Id;
    }

    private async Task<Guid> SeedCustomerAsync()
    {
        var customer = new Customer
        {
            OrganizationId = _orgId,
            PhoneNumber = $"+9477{Guid.NewGuid().GetHashCode() & 0xFFFFFF:D6}",
            FullName = "Reconcile Client",
        };
        _context.Customers.Add(customer);
        await _context.SaveChangesAsync();
        return customer.Id;
    }

    /// <summary>
    /// The counter-sale case: a purchase was recorded on the interaction, so money moved, but no
    /// ledger row exists. The interaction's amount lives only in `CustomerInteraction.MessageContent`
    /// nowhere — it was read and discarded — so the repair cannot recover it from the interaction
    /// alone; see the note in the test below.
    /// </summary>
    private async Task<Guid> SeedInteractionAsync(
        Guid customerId,
        DateTime? occurredAt = null,
        string channel = "in_person",
        string direction = "inbound")
    {
        var interaction = new CustomerInteraction
        {
            OrganizationId = _orgId,
            CustomerId = customerId,
            Channel = channel,
            Direction = direction,
            MessageContent = "Came in for a fitting.",
            CreatedAt = occurredAt ?? DateTime.UtcNow.AddDays(-1),
        };
        _context.CustomerInteractions.Add(interaction);
        await _context.SaveChangesAsync();
        return interaction.Id;
    }

    [Fact]
    public async Task AConfirmedPaymentWithNoLedgerRow_IsRepaired()
    {
        var paymentId = await SeedPaymentAsync();

        var repaired = await _job.RunAsync(CancellationToken.None);

        Assert.Equal(1, repaired);
        var entry = Assert.Single(await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync());
        Assert.Equal(BoutiqueSaleEntryKind.PaymentReceived, entry.Kind);
        Assert.Equal(BoutiqueSaleChargeBasis.Verified, entry.ChargeBasis);
        Assert.Equal(BoutiqueSaleSourceKind.OrderPayment, entry.SourceKind);
        Assert.Equal($"payment:{paymentId}", entry.SourceRef);
        Assert.Equal(12500m, entry.Amount);
    }

    [Fact]
    public async Task RunningTwice_RepairsNothingTheSecondTime()
    {
        await SeedPaymentAsync();

        var first = await _job.RunAsync(CancellationToken.None);
        var second = await _job.RunAsync(CancellationToken.None);

        Assert.Equal(1, first);
        // Idempotent by construction: the second pass finds a matching row and appends nothing.
        Assert.Equal(0, second);
        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task APendingPayment_IsNotRepaired()
    {
        await SeedPaymentAsync(status: "pending");

        // Money that has not been confirmed has not moved. Writing a `Verified` row for it would
        // be the fabrication the two-basis rule exists to prevent.
        Assert.Equal(0, await _job.RunAsync(CancellationToken.None));
        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task ARefundedPayment_IsNotRepairedAsAPaymentReceived()
    {
        await SeedPaymentAsync(status: "refunded");

        Assert.Equal(0, await _job.RunAsync(CancellationToken.None));
        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task APaymentOutsideTheRepairWindow_IsLeftAlone()
    {
        await SeedPaymentAsync(confirmedAt: DateTime.UtcNow.AddDays(-30));

        // The window is bounded: a repair pass is not a full historical backfill, and the ledger
        // says so rather than claiming complete history.
        Assert.Equal(0, await _job.RunAsync(CancellationToken.None));
    }

    [Fact]
    public async Task APaymentThatAlreadyHasALedgerRow_IsNotRepaired()
    {
        var paymentId = await SeedPaymentAsync();
        var ledger = _provider.GetRequiredService<IBoutiqueSaleLedgerService>();
        await ledger.RecordAsync(new RecordBoutiqueSaleCommand(
            _orgId, 12500m, "Payment confirmed at the counter.",
            BoutiqueSaleEntryKind.PaymentReceived, BoutiqueSaleChargeBasis.Verified,
            BoutiqueSaleSourceKind.OrderPayment, $"payment:{paymentId}",
            DateTime.UtcNow.AddDays(-1), null));

        Assert.Equal(0, await _job.RunAsync(CancellationToken.None));
        Assert.Equal(1, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task RepairsAcrossEveryOrganizationInOnePass()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"rc2_{ownerId:N}", Email = "rc2@aveline.lk",
            FirstName = "R2", LastName = "C2", Username = $"rc2_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var otherOrg = new Organization
        {
            Name = "Second Boutique", Slug = $"rc2-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(otherOrg);
        await _context.SaveChangesAsync();

        await SeedPaymentAsync();
        _context.Payments.Add(new Payment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = otherOrg.Id,
            OrderId = Guid.CreateVersion7(),
            Amount = 8000m,
            PaymentType = "full",
            PaymentMethod = "card",
            Status = "confirmed",
            CreatedAt = DateTime.UtcNow.AddDays(-1),
            ConfirmedAt = DateTime.UtcNow.AddDays(-1),
        });
        await _context.SaveChangesAsync();

        Assert.Equal(2, await _job.RunAsync(CancellationToken.None));
        Assert.Equal(2, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task ARepairedRow_IsAttributedToTheSystemNotToAPerson()
    {
        await SeedPaymentAsync();

        await _job.RunAsync(CancellationToken.None);

        var entry = await _context.BoutiqueSaleEntries.AsNoTracking().SingleAsync();
        // The repair has no human actor, and naming one would be a fabrication. `OrderPayment` is a
        // flow-written source the ledger permits an unattributed row on, and the row's reason says
        // it was repaired rather than observed.
        Assert.Null(entry.RecordedByUserId);
        Assert.Contains("reconcil", entry.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheJobReportsTheRepairCount_SoAPersistentWriterBugIsVisible()
    {
        await SeedPaymentAsync();
        await SeedPaymentAsync();
        await SeedPaymentAsync();

        // The count is the signal. A job that quietly repairs rows every hour is telling an operator
        // that a writer upstream is broken; returning 0 here would hide that.
        Assert.Equal(3, await _job.RunAsync(CancellationToken.None));
    }

    /// <summary>
    /// **A counter sale can not be repaired, and the job says so rather than inventing an amount.**
    ///
    /// The plan's §5.6 specified the job as repairing "confirmed payments **or** interactions
    /// carrying a purchase total". Writing it revealed that the second half is impossible:
    /// <see cref="CustomerInteraction"/> has **no amount column** — the purchase total is read from
    /// the request inside `CustomerVisitService`, folded into `Customer.TotalSpent`, and then
    /// discarded. Nothing on the row records what was taken, so the only "repair" available would be
    /// to guess, and a guessed amount in a money journal is worse than a known gap.
    ///
    /// This test is the record of that finding. If a later migration adds the amount to the
    /// interaction (the honest fix), the job can be extended and this test inverted.
    /// </summary>
    [Fact]
    public async Task ACounterSaleInteraction_IsNotRepairedBecauseNoAmountWasPersisted()
    {
        var customerId = await SeedCustomerAsync();
        await SeedInteractionAsync(customerId);

        Assert.Equal(0, await _job.RunAsync(CancellationToken.None));
        Assert.Equal(0, await _context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task AConfirmedPayment_IsRepairedAlongsideAnUnrepairableInteraction()
    {
        // The two are independent: the payment is repairable from its own row, the interaction is
        // not, and neither stops the other.
        var customerId = await SeedCustomerAsync();
        await SeedInteractionAsync(customerId);
        await SeedPaymentAsync();

        var repaired = await _job.RunAsync(CancellationToken.None);

        Assert.Equal(1, repaired);
        Assert.Contains(
            await _context.BoutiqueSaleEntries.AsNoTracking().ToListAsync(),
            entry => entry.SourceKind == BoutiqueSaleSourceKind.OrderPayment);
    }

    [Fact]
    public async Task TheJob_DoesNotReachIntoAnotherTable()
    {
        await SeedPaymentAsync();

        await _job.RunAsync(CancellationToken.None);

        // The repair writes the boutique ledger and nothing else. The platform's revenue journal is
        // a different economy; a repair reaching into it would be a cross-economy write.
        Assert.Equal(0, await _context.IncomeLedgerEntries.CountAsync());
    }
}
