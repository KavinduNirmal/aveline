namespace Aveline.Api.Modules.Billing.Repositories;

/// <summary>
/// The statement's merged shape as the database sees it: the columns both sources share, so a single
/// query can order, skip and count across them.
/// </summary>
/// <remarks>
/// Internal, and deliberately not the wire shape: only the current page is hydrated to full rows,
/// and everything the filter needs — the ordering key and the balance movement — is already here.
/// </remarks>
internal sealed class BlossomStatementProjection
{
    public Guid Id { get; init; }

    public DateTime OccurredAt { get; init; }

    public bool IsEntitlement { get; init; }

    public decimal BlossomDelta { get; init; }

    public decimal BlossomUnits { get; init; }
}
