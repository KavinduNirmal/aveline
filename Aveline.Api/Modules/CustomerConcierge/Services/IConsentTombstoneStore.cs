namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// The one question the consent gate needs answered about erasure: "was this number's data erased
/// for this organisation, and did that leave a terminal objection behind?" (plan §15 Q-4, risk R-3).
/// </summary>
/// <remarks>
/// It is a separate interface because the tombstone lives in the privacy module and the gate must not
/// learn how consent is stored — the same reason <c>IConsentGateService</c> only knows the consent
/// repository. The implementation is <c>ErasureTombstoneStore</c> in the privacy module; it hashes
/// the number and never reads a customer row.
/// </remarks>
public interface IConsentTombstoneStore
{
    /// <summary>
    /// Whether an erasure tombstone exists for the number in this organisation. A <c>true</c> answer
    /// means "this number objected and was erased", which the gate treats as a revocation whenever
    /// there is no explicit <c>granted</c> row for the current customer.
    /// </summary>
    Task<bool> WasErasedAsync(
        Guid organizationId,
        string phoneE164,
        CancellationToken cancellationToken = default);
}
