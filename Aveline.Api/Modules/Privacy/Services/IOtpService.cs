namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Issues and verifies the six-digit OTP that proves control of a phone number before an opt-out is
/// honoured (plan §5.2, §5.3). Nothing else in the API knows the code, and nothing persists it.
/// </summary>
/// <remarks>
/// <b>The interface returns the phone from <see cref="VerifyAsync"/>, and that is load-bearing.</b>
/// The code is minted for a specific number and the digest covers it, so the number the verify
/// reports is the number the caller proved. A revocation must use that value rather than the number
/// in the request body - otherwise a caller who knows a code for their own number could revoke
/// somebody else's.
/// </remarks>
public interface IOtpService
{
    /// <summary>
    /// Mints a six-digit code for <paramref name="phoneE164"/>, stores only its digest, and returns
    /// the plaintext once so the caller can send it. Never logs or persists the code.
    /// </summary>
    /// <exception cref="OtpStoreUnavailableException">
    /// The code could not be stored. The caller must answer <c>503</c>, never "accepted".
    /// </exception>
    Task<OtpIssueResult> IssueAsync(
        Guid organizationId,
        string phoneE164,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Verifies <paramref name="code"/> against <paramref name="handle"/>. Single-use: a success
    /// consumes the code. After <see cref="OtpService.MaxAttempts"/> attempts the code is refused
    /// even if the guess is correct. Never throws for a store failure - that is the
    /// <see cref="OtpVerifyFailure.StoreUnavailable"/> outcome, which the caller must map to
    /// <c>503</c>.
    /// </summary>
    Task<OtpVerifyResult> VerifyAsync(
        string? handle,
        string? code,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Consumes one unit of both start budgets (per address and per number) and reports whether the
    /// request may proceed to issue. <b>Fails closed</b>: a store error answers <c>null</c>, which
    /// the caller must map to <c>503</c> and never to "allowed" (plan §9.2, DR-6).
    /// </summary>
    /// <returns>
    /// <c>true</c> when the request may proceed; <c>false</c> when a budget is spent; <c>null</c>
    /// when the store is unreachable.
    /// </returns>
    Task<bool?> TryStartAsync(
        string ipAddress,
        string phoneE164,
        CancellationToken cancellationToken = default);
}
