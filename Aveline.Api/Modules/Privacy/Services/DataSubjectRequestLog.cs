using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Privacy.Models;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// One completed data-subject request to record. Counts only: the log is the evidence a request
/// happened, and it must not become a copy of the data it is evidence about.
/// </summary>
/// <param name="OrganizationId">The organisation the request arrived for.</param>
/// <param name="CustomerId">The customer it named, when one was resolved.</param>
/// <param name="Kind">One of <see cref="DataSubjectRequestKinds"/>.</param>
/// <param name="PhoneHash">The fingerprint of the proven number. Never the number.</param>
/// <param name="IdempotencyKey">
/// The caller's key for a deletion; <c>null</c> for an export, which is idempotent by nature and gets
/// a server-generated key.
/// </param>
/// <param name="Counts">Per-collection counts, never content.</param>
public sealed record DataSubjectRequestRecord(
    Guid OrganizationId,
    Guid? CustomerId,
    string Kind,
    string PhoneHash,
    string? IdempotencyKey,
    IReadOnlyDictionary<string, int> Counts);

/// <summary>
/// The durable request log (plan §3.3). Deletions are written by the erasure service inside the
/// deletion transaction; this interface is the export's writer, so the endpoint does not have to
/// know the table shape.
/// </summary>
public interface IDataSubjectRequestLog
{
    /// <summary>Records a completed request and returns its id.</summary>
    Task<Guid> RecordAsync(
        DataSubjectRequestRecord record, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class DataSubjectRequestLog : IDataSubjectRequestLog
{
    private readonly AppDbContext _db;
    private readonly TimeProvider _clock;

    public DataSubjectRequestLog(AppDbContext db, TimeProvider clock)
    {
        _db = db;
        _clock = clock;
    }

    /// <inheritdoc />
    public async Task<Guid> RecordAsync(
        DataSubjectRequestRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        var now = _clock.GetUtcNow().UtcDateTime;
        var row = new DataSubjectRequest
        {
            OrganizationId = record.OrganizationId,
            CustomerId = record.CustomerId,
            Kind = record.Kind,
            Status = DataSubjectRequestStatuses.Completed,
            PhoneHash = record.PhoneHash,
            RequestedAt = now,
            VerifiedAt = now,
            CompletedAt = now,
            ResultJson = System.Text.Json.JsonSerializer.Serialize(record.Counts),
            // An export has no caller key (a repeat returns the same data), so it gets a stable
            // server-generated one to satisfy the unique index.
            IdempotencyKey = string.IsNullOrWhiteSpace(record.IdempotencyKey)
                ? $"auto:{Guid.CreateVersion7():N}"
                : record.IdempotencyKey!,
        };

        _db.DataSubjectRequests.Add(row);
        await _db.SaveChangesAsync(cancellationToken);
        return row.Id;
    }
}
