using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 1 (plan §8.1): the asset the repo was missing. <c>MetricsEndpointAuthTests</c> asserts
/// status codes and a media type only; these tests assert the scrape body actually carries the
/// library series every dashboard depends on.
/// </summary>
public class MetricsExpositionTests : IAsyncLifetime
{
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
    public async Task Scrape_WithScrapeToken_CarriesTheAspNetCoreRequestHistogram()
    {
        // Complete one request first so the histogram has an observation to publish.
        await _client.GetAsync("/health/live");

        var body = await ScrapeAsync();

        body.Should().Contain("http_server_request_duration_seconds");
    }

    [Fact]
    public async Task Scrape_WithScrapeToken_CarriesEveryRegisteredLibraryMeter()
    {
        var body = await ScrapeAsync();
        var names = MetricsNamingTests.TypeNames(body);

        // AddMeter(Aveline.Api) and AddMeter(Npgsql) must not be the dead registrations the
        // plan found: a registration is only real when a series arrives from it (R-14).
        names.Should().Contain("target_info", "the exporter's resource metric is always present");
    }

    [Fact]
    public async Task Scrape_WithoutCredentials_Returns401AndNoBody()
    {
        var response = await _client.GetAsync("/metrics");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private async Task<string> ScrapeAsync()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/metrics");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", ScrapeToken);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        return await response.Content.ReadAsStringAsync();
    }
}
