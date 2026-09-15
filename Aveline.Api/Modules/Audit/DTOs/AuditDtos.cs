using System.Text.Json;
using Aveline.Api.Modules.Audit.Models;

namespace Aveline.Api.Modules.Audit.DTOs;

/// <summary>
/// A read-only view of an audit entry. The <c>before</c>/<c>after</c> snapshots are
/// redacted at write time, so a secret never appears here; the persisted columns are
/// returned without the internal actor-key/trace/ip-hash fields the API never documents.
/// </summary>
public sealed record AuditLogEntryDto(
    Guid Id,
    DateTime OccurredAt,
    Guid? OrganizationId,
    string ActorKind,
    Guid? ActorUserId,
    string? ActorRef,
    string Action,
    string EntityType,
    string EntityId,
    string? Reason,
    string? RequestId,
    JsonElement? Before,
    JsonElement? After)
{
    public static AuditLogEntryDto From(AuditLogEntry entry) => new(
        entry.Id,
        entry.CreatedAt,
        entry.OrganizationId,
        entry.ActorKind,
        entry.ActorUserId,
        entry.ActorRef,
        entry.Action,
        entry.EntityType,
        entry.EntityId,
        entry.Reason,
        entry.RequestId,
        ParseJson(entry.BeforeJson),
        ParseJson(entry.AfterJson));

    private static JsonElement? ParseJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A malformed snapshot must never fail an audit read.
            return null;
        }
    }
}

/// <summary>A page of audit entries at the §A.4 pagination convention.</summary>
public sealed record PagedAuditLogEntries(
    IReadOnlyList<AuditLogEntryDto> Items,
    int Page,
    int PageSize,
    int Total);
