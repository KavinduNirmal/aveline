using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Revenue.Endpoints;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Revenue Ledger R2 (issue #343) — the audit trail behind the three money verbs.
///
/// `blossom.ledger.*` rows already exist for the entitlement journal, so the revenue journal joins
/// them in the same Audit Explorer rather than growing a second way to read the same history. The
/// assertion that matters is not just that a row appears but that the actor on it is real: an
/// audited money entry attributed to nobody is the failure this exists to catch.
/// </summary>
public class RevenueAuditTests
{
    private const string DatabaseName = "RevAuditInMemoryDb";

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: DatabaseName)
            .Options);

    private static async Task<(Guid OrgId, Guid ActorId)> SeedAsync()
    {
        await using var context = Context();
        var actorId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = actorId, ClerkId = $"ra_{actorId:N}", Email = "ra@aveline.lk", FirstName = "R",
            LastName = "A", Username = $"ra_{actorId:N}", UserRole = Roles.Owner,
            OrganizationRole = string.Empty, HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        var org = new Organization
        {
            Name = "Rev Audit", Slug = $"ra-{actorId:N}", OwnerUserId = actorId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return (org.Id, actorId);
    }

    private static async Task<IncomeLedgerEntry> RecordAsync(
        Guid orgId, Guid actorId, IncomeEntryKind kind, IncomeChargeBasis basis, string sourceRef)
    {
        await using var context = Context();
        var service = new IncomeLedgerService(context, NullLogger<IncomeLedgerService>.Instance);
        return await service.RecordAsync(new RecordIncomeCommand(
            orgId, 5000m, "A reason long enough to satisfy the rule.", kind, basis,
            IncomeSourceKind.SubscriptionBilling, sourceRef, null, null, DateTime.UtcNow,
            actorId));
    }

    /// <summary>
    /// The three action names, as the Audit Explorer will show them. Pinned as an exact set so a
    /// fourth verb cannot appear without this test being updated deliberately.
    /// </summary>
    [Theory]
    [InlineData(IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified, "revenue.ledger.verified")]
    [InlineData(IncomeEntryKind.Refund, IncomeChargeBasis.Verified, "revenue.ledger.refunded")]
    [InlineData(IncomeEntryKind.Adjustment, IncomeChargeBasis.Verified, "revenue.ledger.adjusted")]
    public async Task EachVerbRecordsItsAuditAction(
        IncomeEntryKind kind, IncomeChargeBasis basis, string expectedAction)
    {
        var (orgId, actorId) = await SeedAsync();
        var entry = await RecordAsync(orgId, actorId, kind, basis, $"ref-{kind}-{Guid.NewGuid():N}");

        var action = RevenueAuditActions.For(entry);

        Assert.Equal(expectedAction, action);
    }

    /// <summary>
    /// A derived charge written by the rollover is a system event, not an operator action, so it
    /// must not claim one of the three administrative action names.
    /// </summary>
    [Fact]
    public void ADerivedSystemCharge_IsNotAuditedAsAnAdministrativeVerb()
    {
        var derived = new IncomeLedgerEntry
        {
            Kind = IncomeEntryKind.SubscriptionCharge,
            ChargeBasis = IncomeChargeBasis.Derived,
            SourceKind = IncomeSourceKind.SubscriptionBilling,
        };

        Assert.Equal("revenue.ledger.derived", RevenueAuditActions.For(derived));
    }

    [Fact]
    public async Task TheAuditRow_NamesTheEntryAndCarriesANonNullActor()
    {
        var (orgId, actorId) = await SeedAsync();
        var entry = await RecordAsync(
            orgId, actorId, IncomeEntryKind.SubscriptionCharge, IncomeChargeBasis.Verified,
            $"ref-{Guid.NewGuid():N}");

        await using var context = Context();
        var service = new Modules.Audit.Services.AuditService(
            new Modules.Audit.Repositories.AuditRepository(context),
            new Modules.Audit.Services.AuditRedactor(),
            new HttpContextAccessor(),
            NullLogger<Modules.Audit.Services.AuditService>.Instance);

        await service.RecordAsync(new Modules.Audit.Services.AuditEntryRequest(
            Action: RevenueAuditActions.For(entry),
            EntityType: nameof(IncomeLedgerEntry),
            EntityId: entry.Id.ToString(),
            OrganizationId: entry.OrganizationId,
            ActorKind: AuditActorKind.User,
            ActorUserId: entry.RecordedByUserId,
            After: new { entry.Kind, entry.ChargeBasis, entry.Amount },
            Reason: entry.Reason));

        var row = await context.AuditLogEntries.SingleAsync();
        Assert.Equal("revenue.ledger.verified", row.Action);
        Assert.Equal(nameof(IncomeLedgerEntry), row.EntityType);
        Assert.Equal(entry.Id.ToString(), row.EntityId);
        Assert.Equal(orgId, row.OrganizationId);
        Assert.Equal(actorId, row.ActorUserId);
        Assert.Equal(AuditActorKind.User, row.ActorKind);
    }
}
