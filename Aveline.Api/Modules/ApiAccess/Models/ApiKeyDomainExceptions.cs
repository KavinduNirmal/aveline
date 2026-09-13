using System.Security.Cryptography;
using System.Text;

namespace Aveline.Api.Modules.ApiAccess.Models;

/// <summary>Base type for API-key failures that map to a specific HTTP status.</summary>
public abstract class ApiKeyDomainException(string message) : Exception(message);

/// <summary>A field or scope failed validation (HTTP 400).</summary>
public sealed class ApiKeyValidationException(string message) : ApiKeyDomainException(message);

/// <summary>The scope may never be granted to an API key (HTTP 400, FR-3.15).</summary>
public sealed class ApiKeyScopeNotAllowedException(string scope)
    : ApiKeyDomainException($"The scope '{scope}' cannot be granted to an API key.")
{
    public string Scope { get; } = scope;
}

/// <summary>The key does not exist in the caller's organization (HTTP 404).</summary>
public sealed class ApiKeyNotFoundException(Guid keyId)
    : ApiKeyDomainException($"API key '{keyId}' was not found.")
{
    public Guid KeyId { get; } = keyId;
}

/// <summary>A key that has served traffic cannot be hard-deleted (HTTP 409, FR-3.13).</summary>
public sealed class ApiKeyAlreadyUsedException(Guid keyId)
    : ApiKeyDomainException($"API key '{keyId}' has been used and cannot be deleted; revoke it instead.")
{
    public Guid KeyId { get; } = keyId;
}

/// <summary>The plan does not include the <c>api.access</c> entitlement (HTTP 403, FR-3.16).</summary>
public sealed class ApiKeyEntitlementRequiredException(string entitlementKey = "api.access")
    : ApiKeyDomainException($"The current plan does not include the '{entitlementKey}' entitlement.")
{
    public string EntitlementKey { get; } = entitlementKey;
}
