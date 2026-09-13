using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>Default <see cref="IIdempotencyService"/> over the replay store.</summary>
public sealed class IdempotencyService(
    IIdempotencyRepository repository,
    IConfiguration? configuration = null) : IIdempotencyService
{
    private const int DefaultRetentionHours = 24;

    public async Task<IdempotencyReplay?> TryReplayAsync(
        Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
        string requestHash, DateTime at, CancellationToken cancellationToken = default)
    {
        var record = await repository.FindAsync(
            organizationId, endpoint, httpMethod, idempotencyKey, cancellationToken);

        if (record is null || record.ExpiresAt <= at)
        {
            return null;
        }

        // A failed operation is not a replayable result: the caller must be able to
        // retry the same key after correcting the underlying condition.
        if (record.ResponseStatus >= 400)
        {
            return null;
        }

        if (!string.Equals(record.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyKeyReuseException();
        }

        return new IdempotencyReplay(record.ResponseStatus, record.ResponseBodyJson);
    }

    public async Task SaveAsync(
        Guid? organizationId, string endpoint, string httpMethod, string idempotencyKey,
        string requestHash, short responseStatus, string responseBodyJson, DateTime createdAt,
        Guid? actorUserId = null, Guid? apiKeyId = null, CancellationToken cancellationToken = default)
    {
        var retentionHours = configuration?.GetValue(
            "Billing:IdempotencyRetentionHours", DefaultRetentionHours) ?? DefaultRetentionHours;

        repository.Add(new IdempotencyRecord
        {
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            ApiKeyId = apiKeyId,
            IdempotencyKey = idempotencyKey,
            Endpoint = endpoint,
            HttpMethod = httpMethod,
            RequestHash = requestHash,
            ResponseStatus = responseStatus,
            ResponseBodyJson = string.IsNullOrWhiteSpace(responseBodyJson) ? "{}" : responseBodyJson,
            CreatedAt = createdAt,
            ExpiresAt = createdAt.AddHours(retentionHours),
        });

        await repository.SaveChangesAsync(cancellationToken);
    }

    public Task<int> DeleteExpiredAsync(DateTime asOf, CancellationToken cancellationToken = default) =>
        repository.DeleteExpiredAsync(asOf, cancellationToken);

    /// <summary>
    /// SHA-256 hex of the canonical JSON body. Whitespace and property order are
    /// normalised through a JSON round-trip so formatting differences do not defeat replay.
    /// </summary>
    public static string ComputeHash(string requestBody)
    {
        var canonical = requestBody;
        try
        {
            var node = JsonNode.Parse(requestBody);
            if (node is not null)
            {
                canonical = node.ToJsonString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            canonical = requestBody.Trim();
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(canonical));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
