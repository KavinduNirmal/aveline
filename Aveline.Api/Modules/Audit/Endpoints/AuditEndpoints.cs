using Aveline.Api.Configurations;
using Aveline.Api.Modules.Audit.DTOs;
using Aveline.Api.Modules.Audit.Repositories;

namespace Aveline.Api.Modules.Audit.Endpoints;

/// <summary>
/// The audit read surface (issue #241, docs/api/README.md §C.1). Team-only: both routes
/// require <see cref="AuthorizationConfiguration.AuditViewPolicy"/>, which no boutique
/// role satisfies. The audit log was write-only before this endpoint existed.
/// </summary>
public static class AuditEndpoints
{
    private const int DefaultPageSize = 50;
    private const int MaxPageSize = 200;

    public static IEndpointRouteBuilder MapAuditEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/admin/audit").WithTags("Audit");

        group.MapGet("", async (
            string? action,
            string? entityType,
            string? entityId,
            Guid? organizationId,
            Guid? actorUserId,
            DateTime? from,
            DateTime? to,
            int? page,
            int? pageSize,
            IAuditRepository repository,
            CancellationToken ct) =>
        {
            var normalisedPage = page is null or < 1 ? 1 : page.Value;
            var normalisedPageSize = pageSize is null or < 1
                ? DefaultPageSize
                : Math.Min(pageSize.Value, MaxPageSize);

            var (items, total) = await repository.QueryAsync(
                action, entityType, entityId, organizationId, actorUserId, from, to,
                normalisedPage, normalisedPageSize, ct);

            return Results.Ok(new PagedAuditLogEntries(
                items.Select(AuditLogEntryDto.From).ToArray(),
                normalisedPage,
                normalisedPageSize,
                total));
        }).RequireAuthorization(AuthorizationConfiguration.AuditViewPolicy);

        group.MapGet("/{entryId:guid}", async (
            Guid entryId,
            IAuditRepository repository,
            CancellationToken ct) =>
        {
            var entry = await repository.GetByIdAsync(entryId, ct);
            return entry is null
                ? Results.NotFound(new { message = "Audit entry not found." })
                : Results.Ok(AuditLogEntryDto.From(entry));
        }).RequireAuthorization(AuthorizationConfiguration.AuditViewPolicy);

        return endpoints;
    }
}
