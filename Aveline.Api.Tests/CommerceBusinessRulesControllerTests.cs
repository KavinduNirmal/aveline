using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class CommerceBusinessRulesControllerTests
{
    private readonly AppDbContext _context;
    private readonly BusinessRulesRepository _repository;
    private readonly BusinessRulesService _service;
    private readonly BusinessRulesController _controller;
    private readonly Guid _orgId = Guid.NewGuid();

    public CommerceBusinessRulesControllerTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ControllerTest_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new BusinessRulesRepository(_context);
        _service = new BusinessRulesService(_repository, NullLogger<BusinessRulesService>.Instance);
        _controller = new BusinessRulesController(_service);
    }

    [Fact]
    public async Task GetAll_ReturnsOkWithList()
    {
        await _service.CreateRuleAsync(_orgId, new CreateBusinessRuleDto("RULE1", "discount", "{}", "Desc"));
        
        var actionResult = await _controller.GetAll(_orgId);
        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var rules = Assert.IsAssignableFrom<IReadOnlyList<BusinessRuleResponseDto>>(okResult.Value);
        
        Assert.Single(rules);
    }

    [Fact]
    public async Task GetById_WhenFound_ReturnsOk_WhenNotFound_ReturnsNotFound()
    {
        var created = await _service.CreateRuleAsync(_orgId, new CreateBusinessRuleDto("RULE2", "discount", "{}", "Desc"));

        var foundResult = await _controller.GetById(_orgId, created.Id);
        var okResult = Assert.IsType<OkObjectResult>(foundResult.Result);
        var dto = Assert.IsType<BusinessRuleResponseDto>(okResult.Value);
        Assert.Equal(created.Id, dto.Id);

        var notFoundResult = await _controller.GetById(_orgId, Guid.NewGuid());
        Assert.IsType<NotFoundResult>(notFoundResult.Result);
    }

    [Fact]
    public async Task Create_WithValidDto_ReturnsCreatedAtAction()
    {
        var dto = new CreateBusinessRuleDto("RULE3", "min_margin", "{\"min_margin\":0.25}", "Desc");
        var result = await _controller.Create(_orgId, dto);

        var createdResult = Assert.IsType<CreatedAtActionResult>(result.Result);
        var responseDto = Assert.IsType<BusinessRuleResponseDto>(createdResult.Value);
        Assert.Equal("RULE3", responseDto.RuleName);
    }

    [Fact]
    public async Task Update_WhenFound_ReturnsOk_WhenNotFound_ReturnsNotFound()
    {
        var created = await _service.CreateRuleAsync(_orgId, new CreateBusinessRuleDto("RULE4", "discount", "{}", "Desc"));
        var updateDto = new UpdateBusinessRuleDto("RULE4_UPDATED", "{}", "Desc Updated", true);

        var result = await _controller.Update(_orgId, created.Id, updateDto);
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var updatedDto = Assert.IsType<BusinessRuleResponseDto>(okResult.Value);
        Assert.Equal("RULE4_UPDATED", updatedDto.RuleName);

        var notFoundResult = await _controller.Update(_orgId, Guid.NewGuid(), updateDto);
        Assert.IsType<NotFoundResult>(notFoundResult.Result);
    }

    [Fact]
    public async Task Delete_WhenFound_ReturnsNoContent_WhenNotFound_ReturnsNotFound()
    {
        var created = await _service.CreateRuleAsync(_orgId, new CreateBusinessRuleDto("RULE5", "discount", "{}", "Desc"));

        var result = await _controller.Delete(_orgId, created.Id);
        Assert.IsType<NoContentResult>(result);

        var notFoundResult = await _controller.Delete(_orgId, Guid.NewGuid());
        Assert.IsType<NotFoundResult>(notFoundResult);
    }

    [Fact]
    public async Task Evaluate_ReturnsOkWithEvaluationResult()
    {
        var request = new EvaluateOrderRulesRequestDto(20000m, 0.35m, 0.05m, "VIP");
        var result = await _controller.Evaluate(_orgId, request);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var response = Assert.IsType<EvaluateOrderRulesResponseDto>(okResult.Value);
        Assert.True(response.IsAutoApproved);
    }
}
