using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>A stored idempotent response to replay.</summary>
public sealed record IdempotencyReplay(short Status, string Body);

/// <summary>The same key was reused with a different request body (409, <c>idempotency-key-reuse</c>).</summary>
public sealed class IdempotencyKeyReuseException()
    : Exception("The Idempotency-Key was already used with a different request body.");

/// <summary>
/// The idempotency lease store is unreachable. The endpoint filter fails closed: it refuses
/// the request with 503 rather than executing an idempotent money operation without the
/// exactly-once guarantee (disposition §2.1).
/// </summary>
public sealed class IdempotencyLeaseUnavailableException(Exception inner)
    : Exception("The idempotency lease store is unavailable.", inner);

/// <summary>
/// Idempotent replay store operations (FR-2.7, BR-2.8, BR-2.9). Kept separate from the
/// HTTP filter so the replay decision is unit-testable.
/// </summary>
public interface IIdempotencyService
{
    /// <summary>
    /// Returns the stored response when the key was already used with the same body,
    /// <c>null</c> when the key is new or its record has expired, and throws
    /// <see cref="IdempotencyKeyReuseException"/> when the body differs.
    /// </summary>
    Task<IdempotencyReplay?> TryReplayAsync(
        Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
        string requestHash, DateTime at, CancellationToken cancellationToken = default);

    Task SaveAsync(
        Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
        string requestHash, short responseStatus, string responseBodyJson, DateTime createdAt,
        Guid? actorUserId = null, Guid? apiKeyId = null, CancellationToken cancellationToken = default);

    Task<int> DeleteExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default);
}
