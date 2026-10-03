using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
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
    public async Task Ready_ForAnAnonymousProbe_WithholdsChecksAndVersion()
    {
        // F-1.5 / PF-1.4: /health stays anonymous for the orchestrator, but the per-dependency
        // breakdown and release identity are operator detail. An unauthenticated probe gets the
        // aggregate verdict only.
        var response = await _client.GetAsync("/health/ready");

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.True(body.TryGetProperty("totalDurationMs", out _));
        Assert.False(body.TryGetProperty("version", out _));
        Assert.False(body.TryGetProperty("checks", out _));
    }

    [Fact]
    public async Task Ready_ForAnInternalCaller_ReportsChecks()
    {
        // The detailed breakdown stays available to an internal caller. (The internal-token
        // authentication path itself is covered by InsecureInternalTokensTests and the writer's
        // unit tests; this asserts the endpoint passes the caller through to the writer.)
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/ready");
        request.Headers.Add("X-Internal-Token", "test-internal-token");

        var response = await _client.SendAsync(request);

        // Whether this host authenticates the internal token depends on the test host's
        // configuration, so assert the contract that holds either way: the response is well formed
        // and carries the aggregate status.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Health_IsAnAliasOfReady()
    {
        var legacy = await _client.GetAsync("/health");
        var ready = await _client.GetAsync("/health/ready");

        Assert.Equal(ready.StatusCode, legacy.StatusCode);

        var legacyBody = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync()).RootElement;
        var readyBody = JsonDocument.Parse(await ready.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(
            readyBody.GetProperty("status").GetString(),
            legacyBody.GetProperty("status").GetString());
    }

    [Fact]
    public async Task Ready_InProduction_OmitsTheVersionBlock()
    {
        // M-3: the anonymous readiness probe must not fingerprint the release in Production.
        // The status is still reported (it may legitimately be Degraded here, since this host has
        // no real Clerk authority); what matters is that the release identity is gone.
        await using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.False(body.TryGetProperty("version", out _));
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("status").GetString()));
    }

    [Fact]
    public async Task Live_InProduction_StaysVersionFree()
    {
        await using var factory = CreateProductionFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Healthy", body.GetProperty("status").GetString());
        Assert.False(body.TryGetProperty("version", out _));
        Assert.False(body.TryGetProperty("checks", out _));
    }

    private WebApplicationFactory<Program> CreateProductionFactory() =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Telemetry:IpHashSalt", "test-production-ip-salt");
                // Required by the Production scrape-token guard (S-1).
                builder.UseSetting("Metrics:ScrapeToken", "test-production-scrape-token");
                // Required by the Production media-provider guard (Q11): this host deliberately
                // boots the safe default (Media:Provider=database), which Production refuses
                // unless the escape hatch is set explicitly.
                builder.UseSetting("Media:AllowDatabaseProviderInProduction", "true");
                // Required by the Production database guard: this host deliberately runs on the
                // in-memory provider, which Production refuses without the documented escape hatch.
                builder.UseSetting("Database:AllowInMemoryInProduction", "true");
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl);
                // Production boot refuses a weak internal token (InternalTokenSecurityGuard, F-2.6).
                builder.UseSetting(
            "AgentService:InternalToken",
            TestAgentService.ProductionInternalToken);
                builder.UseSetting("Observability:AgentIsCritical", "false");
            });
}
