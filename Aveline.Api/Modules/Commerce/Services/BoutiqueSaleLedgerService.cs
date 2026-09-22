using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// The boutique takings journal's only writer. Every rule lives here so no call site can
/// re-implement one and drift — the same reason <c>IncomeLedgerService</c> owns the platform
/// revenue journal's validation and <c>BlossomService</c> owns the Blossom ledger's.
/// </summary>
/// <remarks>
/// This service writes <see cref="BoutiqueSaleEntry"/> and nothing else. It never touches
/// <see cref="Modules.Revenue.Models.IncomeLedgerEntry"/>, which is Aveline's own revenue journal:
/// two isomorphic org-scoped money ledgers in one codebase is the hazard strategy R-12 names, and
/// the name plus a test are the whole defence.
/// </remarks>
public sealed class BoutiqueSaleLedgerService(
    AppDbContext db,
    ILogger<BoutiqueSaleLedgerService> logger) : IBoutiqueSaleLedgerService
{
    /// <summary>Matches the sibling ledger's and the Blossom ledger's reason bounds.</summary>
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    /// <summary>Used when an organization row has no currency of its own.</summary>
    public const string DefaultCurrency = "LKR";

    public async Task<BoutiqueSaleEntry> RecordAsync(
        RecordBoutiqueSaleCommand command, CancellationToken cancellationToken = default)
    {
        Validate(command);

        var organization = await db.Organizations
            .AsNoTracking()
            .FirstOrDefaultAsync(org => org.Id == command.OrganizationId, cancellationToken);
        if (organization is null)
        {
            throw new BoutiqueSaleOrganizationNotFoundException(command.OrganizationId);
        }

        var superseded = await ResolveSupersedeTargetAsync(command, cancellationToken);

        var created = new BoutiqueSaleEntry
        {
            OrganizationId = command.OrganizationId,
            Kind = command.Kind,
            SourceKind = command.SourceKind,
            SourceRef = command.SourceRef?.Trim(),
            ChargeBasis = command.ChargeBasis,
            Status = BoutiqueSaleEntryStatus.Recorded,
            // Copied from the organization rather than hardcoded, so a shop whose row says something
            // other than the platform default does not have to migrate the ledger later. The value
            // is not invented: it is the column's own content, with the default as the fallback.
            Currency = string.IsNullOrWhiteSpace(organization.Currency)
                ? DefaultCurrency
                : organization.Currency,
            Amount = command.Amount,
            Reason = command.Reason.Trim(),
            OrderId = command.OrderId,
            CustomerId = command.CustomerId,
            PaymentId = command.PaymentId,
            OccurredAt = command.OccurredAt,
            RecordedAt = DateTime.UtcNow,
            RecordedByUserId = command.RecordedByUserId,
            SupersedesEntryId = superseded?.Id,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyScope = command.IdempotencyScope,
        };

        if (superseded is not null)
        {
            // Releasing the dedup identity takes two things, and the second is not obvious.
            //
            // The filtered unique index is on `(OrganizationId, SourceKind, SourceRef)` with a
            // filter of `"SourceRef" IS NOT NULL`. A voided row whose `SourceRef` is still populated
            // therefore *still occupies the key* — the filter does not look at `Status`, so marking
            // the row voided alone is not enough and the takeover INSERT trips `23505`. So the void
            // nulls the reference, and the historical link is preserved by `SupersedesEntryId` on
            // the incoming row pointing back at it.
            superseded.SourceRef = null;
            superseded.Status = BoutiqueSaleEntryStatus.Voided;

            // Ordering is load-bearing: EF sorts its modification commands by entity type then by
            // state, which puts the INSERT ahead of the UPDATE, so the void is flushed first inside
            // a transaction and the insert follows.
            await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            db.BoutiqueSaleEntries.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        else
        {
            db.BoutiqueSaleEntries.Add(created);
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Boutique sale entry recorded. id={EntryId} org={OrganizationId} kind={Kind} basis={Basis} amount={Amount}",
            created.Id, created.OrganizationId, created.Kind, created.ChargeBasis, created.Amount);

        return created;
    }

    /// <summary>
    /// The in-memory provider has no transactions, so the supersede's two-phase write is only
    /// wrapped on a relational provider — the same guard the sibling ledger uses.
    /// </summary>
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionIfRelationalAsync(
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static void Validate(RecordBoutiqueSaleCommand command)
    {
        if (command.Amount <= 0m)
        {
            // `Amount` is stored positive and the sign is derived from `Kind`, so a non-positive
            // amount is a bug rather than a legitimate credit.
            throw new BoutiqueSaleValidationException("Amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason)
            || command.Reason.Trim().Length < MinReasonLength
            || command.Reason.Trim().Length > MaxReasonLength)
        {
            throw new BoutiqueSaleValidationException(
                $"Reason must be between {MinReasonLength} and {MaxReasonLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(command.SourceRef))
        {
            // `SourceRef` is the dedup identity, so an entry without one can never be reconciled
            // against the thing that produced it.
            throw new BoutiqueSaleValidationException("A SourceRef is required to identify the entry.");
        }

        // Only the counter sources require an actor. `CounterWalkIn` and `Refund` both have a
        // person behind them at the counter. `OrderPayment` and `OrderSettlement` do not: nothing in
        // the order or payment flow resolves an actor (`OrdersController` passes `createdBy: null`),
        // so requiring one would force a fabricated user id. The row says which flow wrote it, and
        // that is the honest record.
        var actorRequired = command.SourceKind
            is BoutiqueSaleSourceKind.CounterWalkIn
            or BoutiqueSaleSourceKind.Refund;
        if ((command.RecordedByUserId is null || command.RecordedByUserId == Guid.Empty) && actorRequired)
        {
            // The sibling ledger's actor rule, narrowed to the sources that always have a person
            // behind them: a counter sale, a confirmed payment and a refund are each triggered by
            // somebody, so a row that cannot name them is not journal-worthy.
            //
            // `System` is the sibling's stated exception. `OrderSettlement` is added here for one
            // concrete reason the sibling does not have: **nothing in the order or payment flow ever
            // resolves an actor.** `OrdersController` passes `createdBy: null` on create and the
            // payments controller resolves none, so refusing a null actor on a derived settlement
            // would mean either no derived entry at all or a fabricated user id. The honest option
            // is a null actor on a row that says it was derived rather than asserted.
            throw new BoutiqueSaleValidationException(
                "A recorded actor is required; an unattributable money entry must not be written.");
        }
    }

    private async Task<BoutiqueSaleEntry?> ResolveSupersedeTargetAsync(
        RecordBoutiqueSaleCommand command, CancellationToken cancellationToken)
    {
        var sourceRef = command.SourceRef!.Trim();

        // The `Status` predicate stays in the query: EF guarantees a materialised entity matches the
        // predicate that produced it, so a row the tracker already holds as voided cannot come back
        // as live however its other columns have changed in memory.
        var live = await db.BoutiqueSaleEntries.FirstOrDefaultAsync(
            entry => entry.OrganizationId == command.OrganizationId
                && entry.SourceKind == command.SourceKind
                && entry.SourceRef == sourceRef
                && entry.Status == BoutiqueSaleEntryStatus.Recorded,
            cancellationToken);

        if (command.SupersedesEntryId is { } targetId)
        {
            var target = live?.Id == targetId
                ? live
                : await db.BoutiqueSaleEntries.FirstOrDefaultAsync(
                    entry => entry.Id == targetId
                        && entry.OrganizationId == command.OrganizationId,
                    cancellationToken);

            if (target is null)
            {
                throw new BoutiqueSaleEntryNotFoundException(targetId);
            }

            if (target.Status != BoutiqueSaleEntryStatus.Recorded)
            {
                throw new BoutiqueSaleEntryNotVoidableException(targetId, "it is already voided.");
            }

            return target;
        }

        if (live is not null)
        {
            throw new DuplicateBoutiqueSaleEntryException(command.SourceKind, sourceRef);
        }

        return null;
    }
}
