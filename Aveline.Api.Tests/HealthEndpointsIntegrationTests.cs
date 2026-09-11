using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #180 — liveness is dependency-free; readiness reports dependencies and the
/// version block, and <c>/health</c> stays an alias of readiness (FR-7.1..FR-7.4).
/// </summary>
public class HealthEndpointsIntegrationTests : IAsyncLifetime
{
    private StubAgentServer _agentServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _agentServer = new StubAgentServer();
        await _agentServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl);
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                // The stub agent has no Clerk authority; the JWKS check degrades rather
                // than fails, so readiness stays 200.
                builder.UseSetting("Observability:AgentIsCritical", "false");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _agentServer.DisposeAsync();
    }

    [Fact]
    public async Task Live_ReturnsHealthy_WithoutDependencies()
    {
        var response = await _client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_ReportsChecksAndVersionBlock()
    {
        var response = await _client.GetAsync("/health/ready");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(body.TryGetProperty("version", out var version));
        Assert.True(version.TryGetProperty("gitSha", out _));
        Assert.True(version.TryGetProperty("assemblyVersion", out _));
        Assert.True(version.TryGetProperty("environment", out _));

        var names = body.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString())
            .ToArray();
        Assert.Contains("database", names);
    }

    [Fact]
    public async Task Health_IsAnAliasOfReady()
    {
        var legacy = await _client.GetAsync("/health");
        var ready = await _client.GetAsync("/health/ready");

        Assert.Equal(ready.StatusCode, legacy.StatusCode);
        var body = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("checks", out _));
    }
}
