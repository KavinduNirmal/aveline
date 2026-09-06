using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

public class UsageTrackerServiceTests
{
    private readonly AppDbContext _context;
    private readonly IUsageRepository _repository;
    private readonly TestLogger<UsageTrackerService> _logger;
    private readonly IConfiguration _config;
    private readonly UsageTrackerService _service;

    public UsageTrackerServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"UsageTrackerServiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        _repository = new UsageRepository(_context);
        _logger = new TestLogger<UsageTrackerService>();

        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:AbnormalCostThresholdUsd"] = "1.00",
            })
            .Build();

        _service = new UsageTrackerService(_repository, _logger, _config);
    }

    [Theory]
    [InlineData(0, 0, 0, 0.1)]       // Minimum floor of 0.1
    [InlineData(100, 100, 0, 0.2)]   // 200 tokens -> 0.2
    [InlineData(500, 500, 0, 1.0)]   // 1000 tokens -> 1.0
    [InlineData(1000, 500, 200, 1.7)]// 1700 tokens -> 1.7
    [InlineData(1001, 0, 0, 1.1)]    // 1001 tokens -> ceiling to 1.1
    [InlineData(999, 0, 0, 1.0)]     // 999 tokens -> ceiling to 1.0
    public void CalculateBlossomUnits_Follows_Formula_Ceil_To_1dp(
        int input, int output, int cached, decimal expected)
    {
        var units = UsageTrackerService.CalculateBlossomUnits(input, output, cached);
        Assert.Equal(expected, units);
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_ValidRequest_PersistsRecordAndUpdatesLedger()
    {
        var orgId = Guid.NewGuid();
        var request = new RecordUsageRequest(
            OrganizationId: orgId,
            RequestId: "req-123",
            WorkflowId: "wf-abc",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 1200,
            OutputTokens: 300,
            CachedTokens: 0,
            ActualCostUsd: 0.005m);

        var result = await _service.RecordWorkflowUsageAsync(request);

        Assert.NotNull(result);
        Assert.Equal(orgId, result.OrganizationId);
        Assert.Equal("wf-abc", result.WorkflowId);
        Assert.Equal(1.5m, result.BlossomUnits); // (1200 + 300) / 1000 = 1.5
        Assert.Equal(0.005m, result.ActualCostUsd);

        // Verify summary reflects the increment
        var summary = await _service.GetUsageSummaryAsync(orgId);
        Assert.Equal(orgId, summary.OrganizationId);
        Assert.Equal(150m, summary.MonthlyBlossomLimit);
        Assert.Equal(1.5m, summary.BlossomUsed);
        Assert.Equal(148.5m, summary.BlossomRemaining);
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_EmptyOrgId_ThrowsInvalidUsageRecordException()
    {
        var request = new RecordUsageRequest(
            OrganizationId: Guid.Empty,
            RequestId: "req-1",
            WorkflowId: "wf-1",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 10,
            OutputTokens: 10,
            CachedTokens: 0,
            ActualCostUsd: 0.01m);

        await Assert.ThrowsAsync<InvalidUsageRecordException>(() =>
            _service.RecordWorkflowUsageAsync(request));
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_EmptyWorkflowId_ThrowsInvalidUsageRecordException()
    {
        var request = new RecordUsageRequest(
            OrganizationId: Guid.NewGuid(),
            RequestId: "req-1",
            WorkflowId: "",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 10,
            OutputTokens: 10,
            CachedTokens: 0,
            ActualCostUsd: 0.01m);

        await Assert.ThrowsAsync<InvalidUsageRecordException>(() =>
            _service.RecordWorkflowUsageAsync(request));
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_NegativeTokens_ThrowsInvalidUsageRecordException()
    {
        var request = new RecordUsageRequest(
            OrganizationId: Guid.NewGuid(),
            RequestId: "req-1",
            WorkflowId: "wf-1",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: -5,
            OutputTokens: 10,
            CachedTokens: 0,
            ActualCostUsd: 0.01m);

        await Assert.ThrowsAsync<InvalidUsageRecordException>(() =>
            _service.RecordWorkflowUsageAsync(request));
    }

    [Fact]
    public async Task RecordWorkflowUsageAsync_AbnormalCost_LogsWarning()
    {
        var request = new RecordUsageRequest(
            OrganizationId: Guid.NewGuid(),
            RequestId: "req-huge",
            WorkflowId: "wf-huge",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 100000,
            OutputTokens: 50000,
            CachedTokens: 0,
            ActualCostUsd: 2.50m); // Exceeds threshold of 1.00m

        await _service.RecordWorkflowUsageAsync(request);

        Assert.Contains(_logger.Logs, log =>
            log.Level == LogLevel.Warning && log.Message.Contains("[ABNORMAL_USAGE]"));
    }

    [Fact]
    public async Task GetUsageSummaryAsync_ReturnsSummaryForCurrentAccount()
    {
        var orgId = Guid.NewGuid();
        var summary = await _service.GetUsageSummaryAsync(orgId);

        Assert.Equal(orgId, summary.OrganizationId);
        Assert.Equal(150m, summary.MonthlyBlossomLimit);
        Assert.Equal(0m, summary.BlossomUsed);
        Assert.Equal(150m, summary.BlossomRemaining);
        Assert.Equal(UsageAccountStatus.Active, summary.Status);
    }
}

public class TestLogger<T> : ILogger<T>
{
    public List<(LogLevel Level, string Message)> Logs { get; } = new();

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Logs.Add((logLevel, formatter(state, exception)));
    }
}
