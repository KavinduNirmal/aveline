using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Tests;

/// <summary>Issue #206 — the account lifecycle transition matrix (FR-3.9).</summary>
public class AccountStateTransitionTests
{
    [Theory]
    [InlineData(AccountState.OnboardingPending, AccountState.Active)]
    [InlineData(AccountState.OnboardingPending, AccountState.Suspended)]
    [InlineData(AccountState.Active, AccountState.Suspended)]
    [InlineData(AccountState.Suspended, AccountState.Active)]
    public void AllowedTransitions_AreAccepted(AccountState from, AccountState to)
    {
        Assert.True(AccountStateTransitions.IsAllowed(from, to));
        AccountStateTransitions.EnsureAllowed(from, to);
    }

    [Theory]
    [InlineData(AccountState.Active, AccountState.OnboardingPending)]
    [InlineData(AccountState.Suspended, AccountState.OnboardingPending)]
    [InlineData(AccountState.Active, AccountState.Active)]
    [InlineData(AccountState.Suspended, AccountState.Suspended)]
    [InlineData(AccountState.OnboardingPending, AccountState.OnboardingPending)]
    public void OtherTransitions_AreRejected(AccountState from, AccountState to)
    {
        Assert.False(AccountStateTransitions.IsAllowed(from, to));
        var exception = Assert.Throws<InvalidAccountStateTransitionException>(
            () => AccountStateTransitions.EnsureAllowed(from, to));
        Assert.Equal(from, exception.From);
        Assert.Equal(to, exception.To);
    }
}
