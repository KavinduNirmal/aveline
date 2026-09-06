using Aveline.Api.Configurations;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

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
                var status = await integrationService.SaveAsync(organizationId, integrationType, request, ct);
                return Results.Ok(status);
            }
            catch (InvalidIntegrationCredentialsException ex)
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
