using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Tenant-scoped integration (third-party credentials) endpoints. Routed under
/// <c>/orgs/&#123;organizationId&#125;/integrations</c> so the existing
/// <see cref="OrganizationScopeAuthorizationHandler"/> resolves the target organization
/// from the route and enforces an active membership granting <c>settings:manage</c>.
/// Every response is masked — plaintext secrets never leave the backend.
/// </summary>
public static class IntegrationEndpoints
{
    public static IEndpointRouteBuilder MapIntegrationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{organizationId:guid}/integrations");

        group.MapGet("", async (
            Guid organizationId,
            IIntegrationService integrationService,
            CancellationToken ct) =>
        {
            var status = await integrationService.ListStatusAsync(organizationId, ct);
            return Results.Ok(status);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        group.MapGet("/messages", async (
            Guid organizationId,
            AppDbContext db,
            CancellationToken ct) =>
        {
            var logs = await db.InboundMessageLogs
                .Where(m => m.OrganizationId == organizationId)
                .OrderByDescending(m => m.ReceivedAt)
                .Take(50)
                .Select(m => new InboundMessageLogDto(
                    m.Id,
                    m.Channel,
                    m.Direction,
                    m.ExternalId,
                    m.From,
                    m.To,
                    m.Content,
                    m.ReceivedAt))
                .ToListAsync(ct);

            return Results.Ok(logs);
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        group.MapPut("/{type:alpha}", async (
            Guid organizationId,
            string type,
            SaveIntegrationRequest request,
            IIntegrationService integrationService,
            CancellationToken ct) =>
        {
            if (!TryParseType(type, out var integrationType))
            {
                return Results.BadRequest(new { message = $"Unknown integration type '{type}'." });
            }

            try
            {
                await integrationService.SaveAsync(organizationId, integrationType, request, ct);
                // Auto-connect: validate the freshly saved credentials against the provider.
                var test = await integrationService.TestConnectionAsync(organizationId, integrationType, ct);
                return Results.Ok(test.Status);
            }
            catch (InvalidIntegrationCredentialsException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        group.MapPost("/{type:alpha}/test", async (
            Guid organizationId,
            string type,
            IIntegrationService integrationService,
            CancellationToken ct) =>
        {
            if (!TryParseType(type, out var integrationType))
            {
                return Results.BadRequest(new { message = $"Unknown integration type '{type}'." });
            }

            try
            {
                var test = await integrationService.TestConnectionAsync(organizationId, integrationType, ct);
                return Results.Ok(test);
            }
            catch (IntegrationNotConfiguredException ex)
            {
                return Results.BadRequest(new { message = ex.Message });
            }
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        group.MapDelete("/{type:alpha}", async (
            Guid organizationId,
            string type,
            IIntegrationService integrationService,
            CancellationToken ct) =>
        {
            if (!TryParseType(type, out var integrationType))
            {
                return Results.BadRequest(new { message = $"Unknown integration type '{type}'." });
            }

            await integrationService.DeleteAsync(organizationId, integrationType, ct);
            return Results.NoContent();
        }).RequireAuthorization(AuthorizationConfiguration.BoutiqueMembershipManagePolicy);

        return endpoints;
    }

    private static bool TryParseType(string? value, out IntegrationType type) =>
        Enum.TryParse(value, ignoreCase: true, out type);
}
