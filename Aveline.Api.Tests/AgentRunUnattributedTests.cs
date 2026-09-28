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
/// Issue #212 — defect D-7 / gap G-14. A workflow that fails before an organisation is
/// resolvable is still recorded, attributed to <c>NULL</c>, flagged unattributed and
/// excluded from every organisation-scoped query.
/// </summary>
public class AgentRunUnattributedTests : IAsyncLifetime
{
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private string _internalToken = string.Empty;

    public Task InitializeAsync()
    {
        _internalToken = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AgentService:InternalToken", _internalToken);
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

    [Fact]
    public async Task Report_WithoutAnOrganization_IsStoredUnattributed()
    {
        var workflowId = $"wf-unattributed-{Guid.NewGuid():N}";

        var response = await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", new
        {
            workflowId,
            triggerKind = "WhatsAppInbound",
            status = "Failed",
            startedAt = DateTime.UtcNow.AddSeconds(-3),
            completedAt = DateTime.UtcNow,
            errorCode = "org_unresolved",
            steps = Array.Empty<object>(),
        }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        await using var context = Context();
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);
        Assert.Null(run.OrganizationId);
        Assert.True(run.IsUnattributed);
    }

    [Fact]
    public async Task UnattributedRun_IsExcludedFromAnOrganizationScopedLookup()
    {
        var workflowId = $"wf-unattributed-scope-{Guid.NewGuid():N}";

        await _client.SendAsync(Request(HttpMethod.Post, "/internal/agent-runs", new
        {
            workflowId,
            triggerKind = "ApiRequest",
            status = "Running",
            startedAt = DateTime.UtcNow.AddMinutes(-1),
            steps = Array.Empty<object>(),
        }));

        var scoped = await _client.SendAsync(Request(
            HttpMethod.Get, $"/internal/agent-runs/{workflowId}?organizationId={Guid.CreateVersion7()}"));
        Assert.Equal(HttpStatusCode.NotFound, scoped.StatusCode);

        var systemWide = await _client.SendAsync(Request(HttpMethod.Get, $"/internal/agent-runs/{workflowId}"));
        Assert.Equal(HttpStatusCode.OK, systemWide.StatusCode);
        var body = JsonDocument.Parse(await systemWide.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("run").GetProperty("isUnattributed").GetBoolean());
    }
}
