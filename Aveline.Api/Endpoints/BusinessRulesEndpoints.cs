using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

public static class BusinessRulesEndpoints
{
    public static IEndpointRouteBuilder MapBusinessRulesEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/orgs/{orgId:guid}/business-rules")
            .WithTags("Commerce: Business Rules");

        group.MapGet("/", async (
            [FromRoute] Guid orgId,
            [FromQuery] bool activeOnly,
            [FromServices] IBusinessRulesService service,
            CancellationToken ct) =>
        {
            var rules = await service.GetAllRulesAsync(orgId, activeOnly, ct);
            return Results.Ok(rules);
        })
        .WithName("ListBusinessRules")
        .WithSummary("List all business rules for an organization.");

        group.MapGet("/{id:guid}", async (
            [FromRoute] Guid orgId,
            [FromRoute] Guid id,
            [FromServices] IBusinessRulesService service,
            CancellationToken ct) =>
        {
            var rule = await service.GetRuleByIdAsync(id, orgId, ct);
            return rule is not null ? Results.Ok(rule) : Results.NotFound();
        })
        .WithName("GetBusinessRuleById")
        .WithSummary("Get a specific business rule by ID.");

        group.MapPost("/", async (
            [FromRoute] Guid orgId,
            [FromBody] CreateBusinessRuleDto dto,
            [FromServices] IBusinessRulesService service,
            CancellationToken ct) =>
        {
            try
            {
                var created = await service.CreateRuleAsync(orgId, dto, ct);
                return Results.Created($"/api/v1/orgs/{orgId}/business-rules/{created.Id}", created);
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("CreateBusinessRule")
        .WithSummary("Create a new dynamic business rule.");

        group.MapPut("/{id:guid}", async (
            [FromRoute] Guid orgId,
            [FromRoute] Guid id,
            [FromBody] UpdateBusinessRuleDto dto,
            [FromServices] IBusinessRulesService service,
            CancellationToken ct) =>
        {
            try
            {
                var updated = await service.UpdateRuleAsync(id, orgId, dto, ct);
                return updated is not null ? Results.Ok(updated) : Results.NotFound();
            }
            catch (ArgumentException ex)
            {
                return Results.BadRequest(new { error = ex.Message });
            }
        })
        .WithName("UpdateBusinessRule")
        .WithSummary("Update an existing business rule.");

        group.MapDelete("/{id:guid}", async (
            [FromRoute] Guid orgId,
            [FromRoute] Guid id,
            [FromServices] IBusinessRulesService service,
            CancellationToken ct) =>
        {
            var deleted = await service.DeleteRuleAsync(id, orgId, ct);
            return deleted ? Results.NoContent() : Results.NotFound();
        })
        .WithName("DeleteBusinessRule")
        .WithSummary("Delete a business rule.");

        group.MapPost("/evaluate", async (
            [FromRoute] Guid orgId,
            [FromBody] EvaluateOrderRulesRequestDto request,
            [FromServices] IBusinessRulesService service,
            CancellationToken ct) =>
        {
            var result = await service.EvaluateOrderRulesAsync(orgId, request, ct);
            return Results.Ok(result);
        })
        .WithName("EvaluateOrderRules")
        .WithSummary("Evaluate order parameters against active business rules to determine approval requirements.");

        return endpoints;
    }
}
