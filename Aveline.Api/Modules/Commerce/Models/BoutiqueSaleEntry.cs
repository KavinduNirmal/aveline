using Aveline.Api.Common.MultiTenancy;

namespace Aveline.Api.Modules.Commerce.Models;

/// <summary>
/// One append-only entry in the **boutique's own takings** journal.
/// </summary>
/// <remarks>
/// <para>
/// **This is a different economy from <see cref="Modules.Revenue.Models.IncomeLedgerEntry"/>, in a
/// different table, and the two must never be merged.** That entity records what Aveline billed
/// *the shop* (subscriptions and Blossom top-ups); this one records what the shop took *from its
/// clients*. The domain name says "sale" precisely so a future reader cannot confuse the two; the
/// wire and the navigation call the surface "Income", which is the owner-facing word for the same
/// thing. If you are looking for platform revenue, you are in the wrong file.
/// </para>
/// <para>
/// Modelled on the sibling ledger's shape: rows are never updated or deleted, a correction is a new
/// row, and a superseded row is marked <see cref="BoutiqueSaleEntryStatus.Voided"/> rather than
/// removed. Three departures are deliberate and inherited from that design:
/// </para>
/// <list type="number">
///   <item><see cref="Amount"/> is **always positive** and the sign comes from <see cref="Kind"/>,
///   so a column sum can never net a refund against a sale by accident.</item>
///   <item><see cref="ChargeBasis"/> exists, because this journal records both billed value and
///   money actually taken, and conflating them would invent income.</item>
///   <item><see cref="Currency"/> is explicit even though only LKR is used today, because a money
///   number without a unit is not a measurement.</item>
/// </list>
/// </remarks>
public sealed class BoutiqueSaleEntry : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The boutique this money belongs to (tenant scope).</summary>
    public Guid OrganizationId { get; set; }

    public BoutiqueSaleEntryKind Kind { get; set; }

    public BoutiqueSaleSourceKind SourceKind { get; set; }

    /// <summary>
    /// What produced this entry, unique within its <see cref="SourceKind"/>. Together with
    /// <see cref="SourceKind"/> this is the dedup identity, enforced per organization by a filtered
    /// unique index so a retried write cannot double-book.
    /// </summary>
    public string? SourceRef { get; set; }

    public BoutiqueSaleChargeBasis ChargeBasis { get; set; }

    public BoutiqueSaleEntryStatus Status { get; set; } = BoutiqueSaleEntryStatus.Recorded;

    /// <summary>ISO-4217, copied from <c>Organization.Currency</c>.</summary>
    public string Currency { get; set; } = "LKR";

    /// <summary>Always positive; the sign is derived from <see cref="Kind"/>.</summary>
    public decimal Amount { get; set; }

    public string Reason { get; set; } = string.Empty;

    /// <summary>The order this money relates to, when there is one.</summary>
    public Guid? OrderId { get; set; }

    /// <summary>The client this money relates to, when there is one.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>The payment this money relates to, when there is one.</summary>
    public Guid? PaymentId { get; set; }

    /// <summary>The entry this one supersedes, when a verified entry replaces a derived one.</summary>
    public Guid? SupersedesEntryId { get; set; }

    /// <summary>
    /// Who recorded this. Null if and only if <see cref="SourceKind"/> is
    /// <see cref="BoutiqueSaleSourceKind.System"/>: an entry that cannot be attributed to a person
    /// is not journal-worthy, and the service refuses one.
    /// </summary>
    public Guid? RecordedByUserId { get; set; }

    /// <summary>When the money moved, as opposed to when the row was written.</summary>
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;

    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    public string? IdempotencyKey { get; set; }

    public string? IdempotencyScope { get; set; }

    /// <summary>
    /// The sign a reader applies to <see cref="Amount"/> for a given kind. Exposed as a function
    /// rather than a stored column so a total can never be produced by summing a signed column.
    /// </summary>
    public static int SignOf(BoutiqueSaleEntryKind kind) => kind switch
    {
        BoutiqueSaleEntryKind.Sale => 1,
        BoutiqueSaleEntryKind.PaymentReceived => 1,
        BoutiqueSaleEntryKind.Refund => -1,
        // An adjustment carries its own direction in its reason and is reported in its own total,
        // so it contributes no sign to any netted figure.
        BoutiqueSaleEntryKind.Adjustment => 0,
        _ => 0,
    };
}
