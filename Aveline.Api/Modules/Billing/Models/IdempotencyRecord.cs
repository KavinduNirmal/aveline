namespace Aveline.Api.Modules.Billing.Models;

/// <summary>
/// Database-backed idempotency replay store (domain-model.md §4.3). The response body is
/// replayed verbatim on retry; a retry with a different body is a conflict.
/// </summary>
public sealed class IdempotencyRecord
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary><c>null</c> for non-tenant endpoints.</summary>
    public Guid? OrganizationId { get; set; }

    public Guid? ActorUserId { get; set; }

    public Guid? ApiKeyId { get; set; }

    public string IdempotencyKey { get; set; } = string.Empty;

    /// <summary>Route template that consumed the key.</summary>
    public string Endpoint { get; set; } = string.Empty;

    public string HttpMethod { get; set; } = string.Empty;

    /// <summary>SHA-256 hex of the canonical JSON body.</summary>
    public string RequestHash { get; set; } = string.Empty;

    public short ResponseStatus { get; set; }

    public string ResponseBodyJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }
}
