using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Modules.Commerce.Controllers;

[ApiController]
[Route("api/v1/orgs/{orgId:guid}/business-rules")]
public class BusinessRulesController : ControllerBase
{
    private readonly IBusinessRulesService _service;

    public BusinessRulesController(IBusinessRulesService service)
    {
        _service = service ?? throw new ArgumentNullException(nameof(service));
    }

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<BusinessRuleResponseDto>>> GetAll(
        [FromRoute] Guid orgId,
        [FromQuery] bool activeOnly = true,
        CancellationToken ct = default)
    {
        var rules = await _service.GetAllRulesAsync(orgId, activeOnly, ct);
        return Ok(rules);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<BusinessRuleResponseDto>> GetById(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var rule = await _service.GetRuleByIdAsync(id, orgId, ct);
        return rule is not null ? Ok(rule) : NotFound();
    }

    [HttpPost]
    public async Task<ActionResult<BusinessRuleResponseDto>> Create(
        [FromRoute] Guid orgId,
        [FromBody] CreateBusinessRuleDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var created = await _service.CreateRuleAsync(orgId, dto, ct);
            return CreatedAtAction(nameof(GetById), new { orgId, id = created.Id }, created);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpPut("{id:guid}")]
    public async Task<ActionResult<BusinessRuleResponseDto>> Update(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        [FromBody] UpdateBusinessRuleDto dto,
        CancellationToken ct = default)
    {
        try
        {
            var updated = await _service.UpdateRuleAsync(id, orgId, dto, ct);
            return updated is not null ? Ok(updated) : NotFound();
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new { error = ex.Message });
        }
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(
        [FromRoute] Guid orgId,
        [FromRoute] Guid id,
        CancellationToken ct = default)
    {
        var deleted = await _service.DeleteRuleAsync(id, orgId, ct);
        return deleted ? NoContent() : NotFound();
    }

    [HttpPost("evaluate")]
    public async Task<ActionResult<EvaluateOrderRulesResponseDto>> Evaluate(
        [FromRoute] Guid orgId,
        [FromBody] EvaluateOrderRulesRequestDto request,
        CancellationToken ct = default)
    {
        var result = await _service.EvaluateOrderRulesAsync(orgId, request, ct);
        return Ok(result);
    }
}
