namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Quota enforcement is opt-in (<c>Quotas:EnforcementEnabled</c>, default <c>false</c>) so
/// the safe rollout never rejects live traffic until entitlement limits are trusted.
/// </summary>
public sealed class QuotaOptions
{
    public const string SectionName = "Quotas";

    /// <summary>When <c>false</c> (the default) quota is measured but never enforced.</summary>
    public bool EnforcementEnabled { get; set; }
}
