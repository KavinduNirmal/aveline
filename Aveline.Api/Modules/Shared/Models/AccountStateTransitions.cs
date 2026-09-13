namespace Aveline.Api.Modules.Shared.Models;

/// <summary>Raised when an account lifecycle transition is not permitted (FR-3.9).</summary>
public class InvalidAccountStateTransitionException(AccountState from, AccountState to)
    : InvalidOperationException($"The account cannot transition from {from} to {to}.")
{
    public AccountState From { get; } = from;

    public AccountState To { get; } = to;
}

/// <summary>
/// The account lifecycle state machine (FR-3.9):
/// <c>OnboardingPending → Active | Suspended</c>, <c>Active → Suspended</c>, and
/// <c>Suspended → Active</c>. Every other transition is rejected.
/// </summary>
public static class AccountStateTransitions
{
    public static bool IsAllowed(AccountState from, AccountState to) => (from, to) switch
    {
        (AccountState.OnboardingPending, AccountState.Active) => true,
        (AccountState.OnboardingPending, AccountState.Suspended) => true,
        (AccountState.Active, AccountState.Suspended) => true,
        (AccountState.Suspended, AccountState.Active) => true,
        _ => false,
    };

    public static void EnsureAllowed(AccountState from, AccountState to)
    {
        if (!IsAllowed(from, to))
        {
            throw new InvalidAccountStateTransitionException(from, to);
        }
    }
}
