using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Revenue.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Revenue.Services;

/// <summary>
/// The revenue journal's only writer. Every rule lives here so no call site can re-implement one
/// and drift — the same reason <c>BlossomService</c> owns the Blossom ledger's validation.
/// </summary>
public sealed class IncomeLedgerService(
    AppDbContext db,
    ILogger<IncomeLedgerService> logger) : IIncomeLedgerService
{
    /// <summary>Matches the Blossom ledger's <c>CK_BlossomLedgerEntries_Reason</c> constraint.</summary>
    public const int MinReasonLength = 10;
    public const int MaxReasonLength = 500;

    public async Task<IncomeLedgerEntry> RecordAsync(
        RecordIncomeCommand command, CancellationToken cancellationToken = default)
    {
        Validate(command);

        var organizationExists = await db.Organizations
            .AnyAsync(organization => organization.Id == command.OrganizationId, cancellationToken);
        if (!organizationExists)
        {
            throw new RevenueOrganizationNotFoundException(command.OrganizationId);
        }

        var superseded = await ResolveSupersedeTargetAsync(command, cancellationToken);

        var created = new IncomeLedgerEntry
        {
            OrganizationId = command.OrganizationId,
            Kind = command.Kind,
            SourceKind = command.SourceKind,
            SourceRef = command.SourceRef?.Trim(),
            ChargeBasis = command.ChargeBasis,
            Status = IncomeEntryStatus.Recorded,
            Currency = "LKR",
            Amount = command.Amount,
            Reason = command.Reason.Trim(),
            PeriodStart = command.PeriodStart,
            PeriodEnd = command.PeriodEnd,
            OccurredAt = command.OccurredAt,
            RecordedByUserId = command.RecordedByUserId,
            SupersedesEntryId = superseded?.Id,
            IdempotencyKey = command.IdempotencyKey,
            IdempotencyScope = command.IdempotencyScope,
        };

        if (superseded is not null)
        {
            // Releasing the dedup identity takes two things, and the second is not obvious.
            //
            // The filtered unique index is on `(SourceKind, SourceRef)` with a filter of
            // `"SourceRef" IS NOT NULL`. A voided row whose `SourceRef` is still populated
            // therefore *still occupies the key* — the filter does not look at `Status`, so
            // marking the row voided alone is not enough, and the takeover INSERT trips `23505`.
            // `IncomeLedgerPostgresTests` demonstrates exactly that against a real database; the
            // in-memory provider cannot, which is why this test exists.
            //
            // So the void nulls the reference, and the historical link is preserved by
            // `SupersedesEntryId` on the incoming row pointing back at it.
            superseded.SourceRef = null;
            superseded.Status = IncomeEntryStatus.Voided;

            // Ordering is load-bearing too. EF sorts its modification commands by entity type and
            // then by state, which puts the INSERT ahead of the UPDATE, so the void is flushed
            // first, inside a transaction, and the insert follows.
            await using var transaction = await BeginTransactionIfRelationalAsync(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            db.IncomeLedgerEntries.Add(created);
            await db.SaveChangesAsync(cancellationToken);
            if (transaction is not null)
            {
                await transaction.CommitAsync(cancellationToken);
            }
        }
        else
        {
            db.IncomeLedgerEntries.Add(created);
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Income entry recorded. id={EntryId} org={OrganizationId} kind={Kind} basis={Basis} amount={Amount}",
            created.Id, created.OrganizationId, created.Kind, created.ChargeBasis, created.Amount);

        return created;
    }

    /// <summary>
    /// The in-memory provider has no transactions, so the supersede's two-phase write is only
    /// wrapped on a relational provider — the same guard `BlossomLedgerRepository` uses for its
    /// insert-plus-update.
    /// </summary>
    private async Task<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction?> BeginTransactionIfRelationalAsync(
        CancellationToken cancellationToken) =>
        db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

    private static void Validate(RecordIncomeCommand command)
    {
        if (command.Amount <= 0m)
        {
            // `Amount` is stored positive; the sign is derived from `Kind`. A non-positive amount
            // is a bug rather than a legitimate credit.
            throw new RevenueValidationException("Amount must be positive.");
        }

        if (string.IsNullOrWhiteSpace(command.Reason)
            || command.Reason.Trim().Length < MinReasonLength
            || command.Reason.Trim().Length > MaxReasonLength)
        {
            throw new RevenueValidationException(
                $"Reason must be between {MinReasonLength} and {MaxReasonLength} characters.");
        }

        if (string.IsNullOrWhiteSpace(command.SourceRef))
        {
            // `SourceRef` is the dedup identity, so an entry without one can never be reconciled
            // against the thing that produced it.
            throw new RevenueValidationException("A SourceRef is required to identify the entry.");
        }

        if (command.RecordedByUserId is null || command.RecordedByUserId == Guid.Empty)
        {
            throw new RevenueValidationException(
                "A recorded actor is required; an unattributable money entry must not be written.");
        }
    }

    private async Task<IncomeLedgerEntry?> ResolveSupersedeTargetAsync(
        RecordIncomeCommand command, CancellationToken cancellationToken)
    {
        var sourceRef = command.SourceRef!.Trim();
        // The `Status` predicate stays in the query: EF guarantees a materialised entity matches the
        // predicate that produced it, so a row the tracker already holds as voided cannot come back
        // as live however its other columns have changed in memory.
        var existing = await db.IncomeLedgerEntries.FirstOrDefaultAsync(
            entry => entry.OrganizationId == command.OrganizationId
                && entry.SourceKind == command.SourceKind
                && entry.SourceRef == sourceRef
                && entry.Status == IncomeEntryStatus.Recorded,
            cancellationToken);

        var live = existing;

        if (command.SupersedesEntryId is { } targetId)
        {
            var target = live?.Id == targetId
                ? live
                : await db.IncomeLedgerEntries.FirstOrDefaultAsync(
                    entry => entry.Id == targetId
                        && entry.OrganizationId == command.OrganizationId,
                    cancellationToken);

            if (target is null)
            {
                throw new IncomeLedgerEntryNotFoundException(targetId);
            }

            if (target.Status != IncomeEntryStatus.Recorded)
            {
                throw new IncomeEntryNotVoidableException(targetId, "it is already voided.");
            }

            return target;
        }

        if (live is not null)
        {
            throw new DuplicateRevenueEntryException(command.SourceKind, sourceRef);
        }

        return null;
    }
}
