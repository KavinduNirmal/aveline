using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #212 — the internal agent run ingest endpoints (FR-5.1–FR-5.12,
/// BR-5.1–BR-5.9): idempotency, the terminal conflict, the step cap and step append.
/// </summary>
public class AgentRunIngestTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _internalToken = string.Empty;

    public Task InitializeAsync()
    {
        // The scanner flags a literal secret, so the internal token is generated at runtime.
        _internalToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AgentService:InternalToken", _internalToken);
                builder.UseSetting("AgentStats:MaxStepsPerRun", "200");
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
            });

        _client = _factory.CreateClient();
        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private HttpRequestMessage Request(HttpMethod method, string path, object? body = null)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.TryAddWithoutValidation(InternalServiceAuthHandler.HeaderName, _internalToken);
        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        return request;
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

    private static object RunBody(
        Guid? organizationId, string workflowId, string status,
        DateTime? startedAt = null, DateTime? completedAt = null, object[]? steps = null) => new
    {
        organizationId,
        workflowId,
        triggerKind = "ApiRequest",
        status,
        startedAt = startedAt ?? DateTime.UtcNow.AddMinutes(-5),
        completedAt,
        inputTokens = 10,
        outputTokens = 5,
        steps = steps ?? [],
    };

    private static object Step(int index, string status = "Succeeded") => new
    {
        stepIndex = index,
        agentKey = "orchestrator",
        nodeName = $"node-{index}",
        stepKind = "LlmCall",
        status,
        attemptNumber = 1,
        startedAt = DateTime.UtcNow.AddMinutes(-5),
    };

    [Fact]
    public async Task Report_WithoutInternalToken_Returns401()
    {
        var response = await _client.PostAsJsonAsync(
            "/internal/agent-runs", RunBody(Guid.CreateVersion7(), $"wf-{Guid.NewGuid():N}", "Running"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Report_WithSkippedStatus_IsPersistedAsSkipped()
    {
        // Item 1.6: a consent skip is reported by the agent as `Skipped`. It is terminal (it needs
        // CompletedAt) but must not be accepted as, or coerced to, Succeeded.
        var workflowId = $"wf-skip-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();
        var body = RunBody(
            organizationId, workflowId, "Skipped",
            DateTime.UtcNow.AddMinutes(-5), completedAt: DateTime.UtcNow);

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", body));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        await using var context = Context();
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
        Assert.Equal(AgentRunStatus.Skipped, run.Status);
    }

    [Fact]
    public async Task Report_ThenIdenticalRepeat_IsIdempotent()
    {
        var workflowId = $"wf-idem-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var body = RunBody(organizationId, workflowId, "Running", startedAt);

        var first = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", body));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        var firstId = JsonDocument.Parse(await first.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        var second = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", body));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondId = JsonDocument.Parse(await second.Content.ReadAsStringAsync())
            .RootElement.GetProperty("id").GetString();

        Assert.Equal(firstId, secondId);

        await using var context = Context();
        Assert.Equal(1, await context.AgentWorkflowRuns.CountAsync(run => run.WorkflowId == workflowId));
    }

    [Fact]
    public async Task Report_StartedThenCompleted_CompletesOneRowWithItsSteps()
    {
        // The production shape since ADR-027: the agent opens the row when a run starts and closes
        // the same row when it finishes. Two reports, one run - if they did not upsert on
        // WorkflowId, every query would leave an orphan `Running` row behind and
        // `agent.runs_running` would climb forever.
        var workflowId = $"wf-lifecycle-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();
        var startedAt = DateTime.UtcNow.AddMinutes(-5);

        var opened = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Running", startedAt)));
        Assert.Equal(HttpStatusCode.Created, opened.StatusCode);

        await using (var between = Context())
        {
            var running = await between.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
            Assert.Equal(AgentRunStatus.Running, running.Status);
            Assert.Null(running.CompletedAt);
        }

        var closed = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Succeeded", startedAt, startedAt.AddSeconds(3),
                steps: new[] { Step(0), Step(1) })));
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);

        await using var context = Context();
        Assert.Equal(1, await context.AgentWorkflowRuns.CountAsync(r => r.WorkflowId == workflowId));

        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
        Assert.NotNull(run.CompletedAt);
        Assert.Equal(2, await context.AgentStepRuns.CountAsync(step => step.WorkflowRunId == run.Id));
    }

    [Fact]
    public async Task Report_StartedAfterTheRunHasFinished_IsRefusedRatherThanReopeningIt()
    {
        // Why the agent awaits its start report instead of firing and forgetting: a start that lost
        // the race would arrive at a terminal row. Refusing it is what keeps the run's status
        // history honest - a `Running` row must never exist for a run that has already finished.
        var workflowId = $"wf-late-start-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();
        var startedAt = DateTime.UtcNow.AddMinutes(-5);

        await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Succeeded", startedAt, startedAt.AddSeconds(2))));

        var late = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Running", startedAt)));

        Assert.Equal(HttpStatusCode.Conflict, late.StatusCode);

        await using var context = Context();
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
        Assert.Equal(AgentRunStatus.Succeeded, run.Status);
    }

    [Fact]
    public async Task Report_WhenTerminalRunConflicts_Returns409()
    {
        var workflowId = $"wf-conflict-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();
        var startedAt = DateTime.UtcNow.AddMinutes(-5);

        var completed = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Succeeded", startedAt, startedAt.AddSeconds(2))));
        Assert.Equal(HttpStatusCode.Created, completed.StatusCode);

        var conflict = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Failed", startedAt, startedAt.AddSeconds(2))));

        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
        var body = JsonDocument.Parse(await conflict.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("message", out _));
    }

    [Fact]
    public async Task Report_WhenTerminalRunIsRepeated_IsIdempotent()
    {
        var workflowId = $"wf-terminal-repeat-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var body = RunBody(organizationId, workflowId, "Succeeded", startedAt, startedAt.AddSeconds(2));

        var first = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", body));
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);

        var second = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", body));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
    }

    [Fact]
    public async Task Report_WithMoreStepsThanTheCap_Returns413NamingTheLimit()
    {
        var workflowId = $"wf-cap-{Guid.NewGuid():N}";
        var steps = Enumerable.Range(0, 201).Select(index => Step(index)).ToArray();

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(Guid.CreateVersion7(), workflowId, "Running", steps: steps)));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains("200", body.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Report_SucceededWithoutCompletedAt_Returns400()
    {
        var response = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(Guid.CreateVersion7(), $"wf-bad-{Guid.NewGuid():N}", "Succeeded")));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Report_RecomputesDurationWhenInconsistent()
    {
        var workflowId = $"wf-duration-{Guid.NewGuid():N}";
        var startedAt = DateTime.UtcNow.AddMinutes(-5);
        var body = new
        {
            organizationId = Guid.CreateVersion7(),
            workflowId,
            triggerKind = "ApiRequest",
            status = "Succeeded",
            startedAt,
            completedAt = startedAt.AddSeconds(3),
            durationMs = 999999,
            steps = Array.Empty<object>(),
        };

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", body));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var context = Context();
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
        Assert.InRange(run.DurationMs!.Value, 2995, 3005);
    }

    [Fact]
    public async Task AppendSteps_AddsStepsToAnExistingRun()
    {
        var workflowId = $"wf-append-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();

        var created = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Running")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var append = await _client.SendAsync(Request(HttpMethod.Post, $"/internal/agent-runs/{workflowId}/steps",
            new { organizationId, steps = new[] { Step(0), Step(1) } }));
        Assert.Equal(HttpStatusCode.OK, append.StatusCode);

        await using var context = Context();
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
        Assert.Equal(2, await context.AgentStepRuns.CountAsync(step => step.WorkflowRunId == run.Id));
    }

    [Fact]
    public async Task Get_UnknownWorkflow_Returns404()
    {
        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/internal/agent-runs/wf-missing-{Guid.NewGuid():N}"));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_KnownWorkflow_ReturnsRunWithOrderedSteps()
    {
        var workflowId = $"wf-get-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();

        await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Running", steps: new[] { Step(1), Step(0) })));

        var response = await _client.SendAsync(
            Request(HttpMethod.Get, $"/internal/agent-runs/{workflowId}?organizationId={organizationId}"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(workflowId, body.GetProperty("run").GetProperty("workflowId").GetString());
        var steps = body.GetProperty("steps").EnumerateArray().ToArray();
        Assert.Equal(0, steps[0].GetProperty("stepIndex").GetInt32());
        Assert.Equal(1, steps[1].GetProperty("stepIndex").GetInt32());
    }

    [Fact]
    public async Task AppendSteps_WithNullStepsArray_Returns400()
    {
        var workflowId = $"wf-null-steps-{Guid.NewGuid():N}";
        var organizationId = Guid.CreateVersion7();

        var created = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs",
            RunBody(organizationId, workflowId, "Running")));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var append = await _client.SendAsync(Request(HttpMethod.Post, $"/internal/agent-runs/{workflowId}/steps",
            new { organizationId, steps = (object[]?)null }));
        Assert.Equal(HttpStatusCode.BadRequest, append.StatusCode);
    }
}
