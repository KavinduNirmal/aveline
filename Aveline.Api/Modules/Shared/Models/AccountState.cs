namespace Aveline.Api.Modules.Shared.Models;

/// <summary>
/// Explicit account lifecycle state (replaces the boolean onboarding gate).
/// </summary>
public enum AccountState
{
    /// <summary>Profile incomplete, or the account has not yet joined an organization.</summary>
    OnboardingPending,

    /// <summary>Profile complete with a valid organization membership (or org context).</summary>
    Active,

    /// <summary>Account disabled — no authenticated API access.</summary>
    Suspended,
}
