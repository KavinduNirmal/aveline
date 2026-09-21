using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

/// <summary>
/// The organization's order business rules: the thresholds that decide when an order needs
/// approval and the caps a discount must respect.
/// </summary>
/// <remarks>
/// **The route token is <c>organizationId</c>** for the same reason as
/// <see cref="OrdersController"/>: the organization-scope handler matches that key exactly.
///
/// Every route here — including the reads and the evaluator — requires
/// <see cref="AuthorizationConfiguration.BoutiqueOrderManagePolicy"/> (<c>orders:manage</c>).
/// A business rule <em>is</em> order policy: it is what routes a threshold-exceeding order to
/// <c>pending_approval</c> and what caps a discount, so editing it is an order-class act, not a
/// catalogue one. Before this attribute existed the controller had no authorisation at all.
/// </remarks>
[ApiController]
[Route("api/v1/orgs/{organizationId:guid}/business-rules")]
[Authorize(Policy = AuthorizationConfiguration.BoutiqueOrderManagePolicy)]
public class BusinessRulesController : ControllerBase
{
    private readonly IBusinessRulesService _service;

    public BusinessRulesController(IBusinessRulesService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BusinessRuleResponseDto>>> GetAll(
        [FromRoute] Guid organizationId,
        [FromQuery] bool activeOnly = true,
        CancellationToken ct = default)
    {
        var rules = await _service.GetAllRulesAsync(organizationId, activeOnly, ct);
        return Ok(rules);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BusinessRuleResponseDto>> GetById(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var rule = await _service.GetRuleByIdAsync(id, organizationId, ct);
        return rule is not null ? Ok(rule) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<BusinessRuleResponseDto>> Create(
        [FromRoute] Guid organizationId,
        [FromBody] CreateBusinessRuleDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var created = await _service.CreateRuleAsync(organizationId, dto, ct);
            return CreatedAtAction(
                nameof(GetById), new { organizationId, id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BusinessRuleResponseDto>> Update(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        [FromBody] UpdateBusinessRuleDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await _service.UpdateRuleAsync(id, organizationId, dto, ct);
            return updated is not null ? Ok(updated) : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid organizationId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var deleted = await _service.DeleteRuleAsync(id, organizationId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluateOrderRulesResponseDto>> Evaluate(
        [FromRoute] Guid organizationId,
        [FromBody] EvaluateOrderRulesRequestDto request,
        CancellationToken ct = default)
    {
        var result = await _service.EvaluateOrderRulesAsync(organizationId, request, ct);
        return Ok(result);
    }
}
