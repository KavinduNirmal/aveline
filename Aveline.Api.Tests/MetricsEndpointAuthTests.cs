using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Aveline.Api.Infrastructure.Integrations;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #181 — /metrics must never be anonymously readable (FR-7.4, BR-7.4).
/// </summary>
public class MetricsEndpointAuthTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";
    private const string ScrapeToken = "test-scrape-token";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
                builder.UseSetting("Metrics:ScrapeToken", ScrapeToken);
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    [Fact]
    public async Task Metrics_WithoutCredentials_Returns401()
    {
        var response = await _client.GetAsync("/metrics");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_WithInternalToken_ReturnsPrometheusText()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/plain", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task Metrics_WithScrapeToken_Returns200()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ScrapeToken);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Metrics_WithWrongScrapeToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "wrong-token");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
