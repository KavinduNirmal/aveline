namespace Aveline.Api.Modules.CustomerConcierge.Models;

/// <summary>
/// The three consent states, plus the single value used when no <see cref="CustomerConsent"/> row
/// exists yet. Constants rather than an enum because the value is persisted as a bounded varchar
/// and read back from the wire as camelCase JSON by the Python agent.
/// </summary>
public static class ConsentStatuses
{
    public const string Pending = "pending";
    public const string Granted = "granted";
    public const string Revoked = "revoked";

    /// <summary>
    /// A missing row means "identified, disclosure not yet answered", which is exactly
    /// <see cref="Pending"/>. One definition, because two call sites used to disagree
    /// (<c>pending</c> in the consent service vs <c>unknown</c> on the profile, defect D-2).
    /// </summary>
    public const string AbsentRow = Pending;

    /// <summary>
    /// Trims and lower-cases <paramref name="status"/>; returns <c>null</c> when the value is not
    /// one of the three valid states.
    /// </summary>
    public static string? TryNormalize(string? status)
    {
        var normalized = status?.Trim().ToLowerInvariant();
        return normalized is Pending or Granted or Revoked ? normalized : null;
    }
}
