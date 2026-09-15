using Aveline.Api.Authorization;

namespace Aveline.Api.Modules.ApiAccess.Models;

/// <summary>
/// Validates API-key scopes against the permission catalog (FR-3.15, BR-3.4).
/// </summary>
/// <remarks>
/// Two independent rules: every scope must be a known permission, and the money-shaped
/// or cross-tenant scopes (<c>pricing:*</c>, <c>billing:adjust</c>, <c>admin:*</c>) may
/// never be delegated to a machine credential.
/// </remarks>
public static class ApiKeyScopes
{
    private static readonly string[] ForbiddenPrefixes = ["pricing:", "admin:"];

    public static IReadOnlyList<string> Validate(IEnumerable<string>? scopes)
    {
        var requested = (scopes ?? [])
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        if (requested.Length == 0)
        {
            throw new ApiKeyValidationException("At least one scope is required.");
        }

        foreach (var scope in requested)
        {
            if (!Permissions.All.Contains(scope))
            {
                throw new ApiKeyValidationException($"'{scope}' is not a known permission.");
            }

            var forbidden = scope == Permissions.BillingAdjust
                            || ForbiddenPrefixes.Any(prefix => scope.StartsWith(prefix, StringComparison.Ordinal));

            if (forbidden)
            {
                throw new ApiKeyScopeNotAllowedException(scope);
            }
        }

        return requested;
    }
}
