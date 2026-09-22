using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace Aveline.Api.Tests;

/// <summary>
/// A minimal <see cref="IAuthorizationService"/> for controller unit tests: it answers a policy by
/// name and never touches the framework's real requirement handlers, which need a full
/// authentication pipeline the unit tests do not stand up.
/// </summary>
/// <remarks>
/// Used by the Q14 tests, where the whole question is "does the controller refuse a `reject`
/// decision for a caller who does not hold `orders:manage`" — a question about the controller's own
/// branch, not about policy registration.
/// </remarks>
public sealed class TestAuthorizationService(Func<string, bool> allows) : IAuthorizationService
{
    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
        => Task.FromResult(AuthorizationResult.Success());

    public Task<AuthorizationResult> AuthorizeAsync(
        ClaimsPrincipal user, object? resource, string policyName)
        => Task.FromResult(
            allows(policyName) ? AuthorizationResult.Success() : AuthorizationResult.Failed());
}
