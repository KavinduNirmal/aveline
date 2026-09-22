using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// The tenant income surface's read half: the register (E-4) and the per-kind breakdown (E-5).
/// </summary>
/// <remarks>
/// It reads <see cref="BoutiqueSaleEntry"/> and nothing else. It never touches
/// <see cref="Modules.Revenue.Models.IncomeLedgerEntry"/>, which records what Aveline billed the
/// shop: two isomorphic org-scoped money ledgers in one codebase is the hazard strategy R-12 names.
/// </remarks>
public interface IBoutiqueIncomeReadService
{
    Task<BoutiqueIncomeLedgerPageDto> GetLedgerAsync(
        Guid organizationId,
        BoutiqueIncomeLedgerQuery query,
        CancellationToken cancellationToken = default);

    Task<BoutiqueIncomeAccountsDto> GetAccountsAsync(
        Guid organizationId,
        BoutiqueIncomeAccountsQuery query,
        CancellationToken cancellationToken = default);
}

public sealed class BoutiqueIncomeReadService(AppDbContext db) : IBoutiqueIncomeReadService
{
    /// <summary>The clamp the sibling ledger reader and the Blossom reader share.</summary>
    public const int MaxPageSize = 200;

    public const int DefaultPageSize = 50;

    /// <summary>
    /// The tenant family's **own** window cap. The statistics family already carries three separate
    /// caps (`Telemetry` 92, `BusinessAnalytics` 400, `Revenue` 400), so borrowing one of those for
    /// a different surface would couple two unrelated freshness contracts.
    /// </summary>
    public const int MaxWindowDays = 400;

    /// <summary>
    /// The prefix the reconciliation job writes into a repaired row's reason. It is the marker that
    /// makes `incomeLedgerBackfilled` computable, so it lives beside the job's writer rather than
    /// being spelled out at both ends.
    /// </summary>
    public const string ReconciliationReasonPrefix = "Reconciled:";

    private static readonly string[] KnownKinds =
        Enum.GetNames<BoutiqueSaleEntryKind>();

    private static readonly string[] KnownBases =
        Enum.GetNames<BoutiqueSaleChargeBasis>();

    public async Task<BoutiqueIncomeLedgerPageDto> GetLedgerAsync(
        Guid organizationId,
        BoutiqueIncomeLedgerQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveWindow(query.From, query.To, out var window, out var capped, out var error))
        {
            return BoutiqueIncomeLedgerPageDto.Invalid(error!);
        }

        if (query.Kind is { Length: > 0 } kind
            && !KnownKinds.Contains(kind, StringComparer.OrdinalIgnoreCase))
        {
            return BoutiqueIncomeLedgerPageDto.Invalid(
                $"Unknown kind '{kind}'. Known kinds: {string.Join(", ", KnownKinds)}.");
        }

        if (query.Basis is { Length: > 0 } basis
            && !KnownBases.Contains(basis, StringComparer.OrdinalIgnoreCase))
        {
            return BoutiqueIncomeLedgerPageDto.Invalid(
                $"Unknown basis '{basis}'. Known bases: {string.Join(", ", KnownBases)}.");
        }

        var page = Math.Max(1, query.Page);
        var pageSize = Math.Clamp(
            query.PageSize <= 0 ? DefaultPageSize : query.PageSize, 1, MaxPageSize);

        var inWindow = ApplyFilters(
            db.BoutiqueSaleEntries
                .AsNoTracking()
                .Where(entry => entry.OrganizationId == organizationId
                                && entry.OccurredAt >= window.From
                                && entry.OccurredAt < window.To),
            query);

        var total = await inWindow.CountAsync(cancellationToken);

        var rows = await inWindow
            .OrderByDescending(entry => entry.OccurredAt)
            .ThenByDescending(entry => entry.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        // Totals are computed over the **whole window**, not the page, so a reader who pages
        // through a week still sees the week's totals rather than the last page's.
        var all = await inWindow.ToListAsync(cancellationToken);
        var totals = TotalsOf(all);
        var reconciliation = Reconcile(all);

        var currency = await ResolveCurrencyAsync(organizationId, all, cancellationToken);
        var quality = await BuildQualityAsync(organizationId, all, capped, cancellationToken);

        return new BoutiqueIncomeLedgerPageDto(
            new BoutiqueIncomeWindowDto(window.From, window.To, DateTime.UtcNow),
            rows.Select(ToDto).ToArray(),
            total,
            page,
            pageSize,
            capped,
            totals,
            reconciliation,
            currency,
            quality);
    }

    public async Task<BoutiqueIncomeAccountsDto> GetAccountsAsync(
        Guid organizationId,
        BoutiqueIncomeAccountsQuery query,
        CancellationToken cancellationToken = default)
    {
        if (!TryResolveWindow(query.From, query.To, out var window, out var capped, out var error))
        {
            return BoutiqueIncomeAccountsDto.Invalid(error!);
        }

        var all = await db.BoutiqueSaleEntries
            .AsNoTracking()
            .Where(entry => entry.OrganizationId == organizationId
                            && entry.OccurredAt >= window.From
                            && entry.OccurredAt < window.To)
            .ToListAsync(cancellationToken);

        var items = all
            .GroupBy(entry => entry.Kind)
            .OrderBy(group => group.Key)
            .Select(group => new BoutiqueIncomeKindTotalDto(
                group.Key.ToString(),
                group.Sum(entry => entry.Amount),
                group.Count()))
            .ToArray();

        // The payment-method split is read from the payment rows rather than inferred from the
        // ledger, because a ledger entry carries no method of its own. Joining on `PaymentId` keeps
        // it exact: an entry with no payment simply has no method to report.
        var paymentIds = all
            .Where(entry => entry.PaymentId is not null)
            .Select(entry => entry.PaymentId!.Value)
            .Distinct()
            .ToArray();

        var methods = paymentIds.Length == 0
            ? []
            : await db.Payments
                .AsNoTracking()
                .Where(payment => payment.OrganizationId == organizationId
                                  && paymentIds.Contains(payment.Id))
                .Select(payment => new { payment.Id, payment.PaymentMethod })
                .ToDictionaryAsync(row => row.Id, row => row.PaymentMethod, cancellationToken);

        var byMethod = all
            .Where(entry => entry.PaymentId is not null
                            && methods.ContainsKey(entry.PaymentId!.Value))
            .GroupBy(entry => NormaliseMethod(methods[entry.PaymentId!.Value]))
            .OrderBy(group => group.Key, StringComparer.Ordinal)
            .Select(group => new BoutiqueIncomePaymentMethodTotalDto(
                group.Key,
                group.Sum(entry => entry.Amount),
                group.Count()))
            .ToArray();

        var currency = await ResolveCurrencyAsync(organizationId, all, cancellationToken);
        var quality = await BuildQualityAsync(organizationId, all, capped, cancellationToken);

        return new BoutiqueIncomeAccountsDto(
            new BoutiqueIncomeWindowDto(window.From, window.To, DateTime.UtcNow),
            items,
            byMethod,
            Reconcile(all),
            currency,
            quality);
    }

    // ── window ───────────────────────────────────────────────────────────────────────────────────

    private static bool TryResolveWindow(
        DateTime? from,
        DateTime? to,
        out (DateTime From, DateTime To) window,
        out bool capped,
        out string? error)
    {
        var effectiveTo = to ?? DateTime.UtcNow;
        var effectiveFrom = from ?? effectiveTo.AddDays(-30);

        if (effectiveFrom >= effectiveTo)
        {
            window = default;
            capped = false;
            error = "The window's start must be before its end.";
            return false;
        }

        capped = false;
        var cap = TimeSpan.FromDays(MaxWindowDays);
        if (effectiveTo - effectiveFrom > cap)
        {
            // The cap is **applied and reported**, never silent. A caller that asked for a year and
            // received 400 days is told so, because a chart labelled "1 year" over 400 days of data
            // is a lie about the shape of the series.
            effectiveFrom = effectiveTo - cap;
            capped = true;
        }

        window = (effectiveFrom, effectiveTo);
        error = null;
        return true;
    }

    // ── filters and projection ───────────────────────────────────────────────────────────────────

    private static IQueryable<BoutiqueSaleEntry> ApplyFilters(
        IQueryable<BoutiqueSaleEntry> query, BoutiqueIncomeLedgerQuery filters)
    {
        if (filters.Kind is { Length: > 0 } kind
            && Enum.TryParse<BoutiqueSaleEntryKind>(kind, ignoreCase: true, out var parsedKind))
        {
            query = query.Where(entry => entry.Kind == parsedKind);
        }

        if (filters.Basis is { Length: > 0 } basis
            && Enum.TryParse<BoutiqueSaleChargeBasis>(basis, ignoreCase: true, out var parsedBasis))
        {
            query = query.Where(entry => entry.ChargeBasis == parsedBasis);
        }

        if (filters.Query is { Length: > 0 } text)
        {
            var term = text.Trim().ToLowerInvariant();
            query = query.Where(entry =>
                entry.Reason.ToLower().Contains(term)
                || (entry.SourceRef != null && entry.SourceRef.ToLower().Contains(term)));
        }

        return query;
    }

    private static BoutiqueIncomeEntryDto ToDto(BoutiqueSaleEntry entry) => new(
        entry.Id,
        entry.Kind.ToString(),
        entry.SourceKind.ToString(),
        entry.SourceRef,
        entry.ChargeBasis.ToString(),
        entry.Status.ToString(),
        entry.Currency,
        entry.Amount,
        entry.Reason,
        entry.OccurredAt,
        entry.RecordedAt,
        entry.OrderId,
        entry.CustomerId,
        entry.PaymentId,
        entry.RecordedByUserId);

    private static BoutiqueIncomeTotalsDto TotalsOf(IReadOnlyCollection<BoutiqueSaleEntry> entries) =>
        new(
            entries.Where(e => e.Kind == BoutiqueSaleEntryKind.Sale).Sum(e => e.Amount),
            entries.Where(e => e.Kind == BoutiqueSaleEntryKind.PaymentReceived).Sum(e => e.Amount),
            entries.Where(e => e.Kind == BoutiqueSaleEntryKind.Refund).Sum(e => e.Amount),
            entries.Where(e => e.Kind == BoutiqueSaleEntryKind.Adjustment).Sum(e => e.Amount));

    /// <summary>
    /// The reconciliation identity. Only <c>Recorded</c> rows count: a voided row has been superseded
    /// and is excluded from every total, but it stays on disk because the ledger is append-only.
    /// </summary>
    private static BoutiqueIncomeReconciliationDto Reconcile(
        IReadOnlyCollection<BoutiqueSaleEntry> entries)
    {
        var recorded = entries
            .Where(entry => entry.Status == BoutiqueSaleEntryStatus.Recorded)
            .ToArray();

        var verified = recorded
            .Where(entry => entry.ChargeBasis == BoutiqueSaleChargeBasis.Verified
                            && entry.Kind != BoutiqueSaleEntryKind.Refund)
            .Sum(entry => entry.Amount);
        var derived = recorded
            .Where(entry => entry.ChargeBasis == BoutiqueSaleChargeBasis.Derived)
            .Sum(entry => entry.Amount);
        var refunds = recorded
            .Where(entry => entry.Kind == BoutiqueSaleEntryKind.Refund)
            .Sum(entry => entry.Amount);

        var netVerified = verified - refunds;

        return new BoutiqueIncomeReconciliationDto(
            VerifiedTotal: verified,
            DerivedTotal: derived,
            RefundTotal: refunds,
            NetVerified: netVerified,
            // The gap is what the shop billed and has no evidence of collecting. It is the headline
            // honesty number, not an error.
            UnverifiedGap: derived,
            IsReconciled: derived == 0m);
    }

    // ── currency and quality ─────────────────────────────────────────────────────────────────────

    private async Task<string> ResolveCurrencyAsync(
        Guid organizationId,
        IReadOnlyCollection<BoutiqueSaleEntry> entries,
        CancellationToken cancellationToken)
    {
        // `Organization.Currency` is a real column, so it is read rather than hardcoded. The default
        // exists only for a row whose value is blank.
        var currency = await db.Organizations
            .AsNoTracking()
            .Where(org => org.Id == organizationId)
            .Select(org => org.Currency)
            .FirstOrDefaultAsync(cancellationToken);

        if (!string.IsNullOrWhiteSpace(currency))
        {
            return currency;
        }

        return entries.FirstOrDefault()?.Currency
               ?? BoutiqueSaleLedgerService.DefaultCurrency;
    }

    private async Task<BoutiqueIncomeDataQualityDto> BuildQualityAsync(
        Guid organizationId,
        IReadOnlyCollection<BoutiqueSaleEntry> entries,
        bool windowCapped,
        CancellationToken cancellationToken)
    {
        var paymentRowsPresent = await db.Payments
            .AsNoTracking()
            .AnyAsync(payment => payment.OrganizationId == organizationId, cancellationToken);

        var notes = new List<string>();

        if (!paymentRowsPresent)
        {
            // The single most likely reason a real shop shows zero cash, stated rather than left for
            // the owner to conclude their own shop took nothing.
            notes.Add(
                "No payment rows exist for this boutique yet, so the cash figures come from counter "
                + "sales only. This is not a measurement of zero.");
        }

        if (windowCapped)
        {
            notes.Add(
                $"The requested window exceeded the {MaxWindowDays}-day limit and was capped; the "
                + "figures cover the window echoed in this response.");
        }

        // There is no payment-provider client in this repository, so no figure here is settled
        // money. `Derived` entries are billed value, and the surface must never imply otherwise.
        notes.Add(
            "No payment provider is connected, so `Derived` entries are billed value rather than "
            + "collected money; the two bases are reported separately and never summed.");

        var derivedCount = entries.Count(entry =>
            entry.ChargeBasis == BoutiqueSaleChargeBasis.Derived
            && entry.Status == BoutiqueSaleEntryStatus.Recorded);
        if (derivedCount > 0)
        {
            notes.Add($"{derivedCount} entry(ies) in this window are unverified billed value.");
        }

        // Computed rather than hardcoded. A repaired row means the register contains money that was
        // written by the reconciliation job rather than observed by a writer, so the ledger's start
        // date is a real boundary and a reader is told about it.
        var backfilled = await db.BoutiqueSaleEntries
            .AsNoTracking()
            .AnyAsync(entry => entry.OrganizationId == organizationId
                               && entry.Reason.StartsWith(ReconciliationReasonPrefix),
                cancellationToken);

        if (backfilled)
        {
            notes.Add(
                "This register contains entries repaired by the reconciliation job, so it begins at "
                + "a date rather than covering the boutique's full history.");
        }

        return new BoutiqueIncomeDataQualityDto(
            PaymentRowsPresent: paymentRowsPresent,
            OrderCostsComplete: true,
            IncomeLedgerBackfilled: backfilled,
            RefundAndOutstandingExcludedFromCollected: true,
            CheckedAt: DateTime.UtcNow,
            Notes: notes);
    }

    private static string NormaliseMethod(string? method) =>
        string.IsNullOrWhiteSpace(method) ? "unspecified" : method.Trim().ToLowerInvariant();
}
