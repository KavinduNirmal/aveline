using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.Endpoints;
using Aveline.Api.Modules.Billing.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

public class UsageEndpointsIntegrationTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

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
                builder.UseSetting("Billing:AbnormalCostThresholdUsd", "1.00");
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
    public async Task RecordUsage_Without_Internal_Token_Returns_Unauthorized()
    {
        var orgId = Guid.NewGuid();
        var payload = new RecordUsageRequest(
            OrganizationId: orgId,
            RequestId: "req-unauth",
            WorkflowId: "wf-unauth",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 500,
            OutputTokens: 200,
            CachedTokens: 0,
            ActualCostUsd: 0.002m);

        var response = await _client.PostAsJsonAsync("/internal/usage/record", payload);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RecordUsage_With_Invalid_Internal_Token_Returns_Unauthorized()
    {
        var orgId = Guid.NewGuid();
        var payload = new RecordUsageRequest(
            OrganizationId: orgId,
            RequestId: "req-badtoken",
            WorkflowId: "wf-badtoken",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 500,
            OutputTokens: 200,
            CachedTokens: 0,
            ActualCostUsd: 0.002m);

        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/usage/record");
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, "wrong-token");
        request.Content = JsonContent.Create(payload);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task RecordUsage_With_Valid_Token_CreatesRecord_And_IncrementsLedger()
    {
        var orgId = Guid.NewGuid();
        var payload = new RecordUsageRequest(
            OrganizationId: orgId,
            RequestId: "req-success",
            WorkflowId: "wf-success",
            Provider: "openai",
            Model: "gpt-4o",
            InputTokens: 1200,
            OutputTokens: 300,
            CachedTokens: 0,
            ActualCostUsd: 0.005m);

        using var postRequest = new HttpRequestMessage(HttpMethod.Post, "/internal/usage/record");
        postRequest.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        postRequest.Content = JsonContent.Create(payload);

        var postResponse = await _client.SendAsync(postRequest);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var created = await postResponse.Content.ReadFromJsonAsync<AiUsageRecordResponse>();
        Assert.NotNull(created);
        Assert.Equal(orgId, created.OrganizationId);
        Assert.Equal("wf-success", created.WorkflowId);
        Assert.Equal(1.5m, created.BlossomUnits);

        // Verify summary reflects the increment
        using var summaryRequest = new HttpRequestMessage(HttpMethod.Get, $"/internal/usage/summary/{orgId}");
        summaryRequest.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);

        var summaryResponse = await _client.SendAsync(summaryRequest);
        Assert.Equal(HttpStatusCode.OK, summaryResponse.StatusCode);

        var summary = await summaryResponse.Content.ReadFromJsonAsync<UsageSummary>();
        Assert.NotNull(summary);
        Assert.Equal(orgId, summary.OrganizationId);
        Assert.Equal(150m, summary.MonthlyBlossomLimit);
        Assert.Equal(1.5m, summary.BlossomUsed);
        Assert.Equal(148.5m, summary.BlossomRemaining);
    }

    [Fact]
    public async Task GetUsageRecords_Returns_Paginated_Records()
    {
        var orgId = Guid.NewGuid();

        // Submit two records
        for (int i = 1; i <= 2; i++)
        {
            var payload = new RecordUsageRequest(
                OrganizationId: orgId,
                RequestId: $"req-{i}",
                WorkflowId: $"wf-{i}",
                Provider: "openai",
                Model: "gpt-4o",
                InputTokens: 1000 * i,
                OutputTokens: 200,
                CachedTokens: 0,
                ActualCostUsd: 0.003m * i);

            using var req = new HttpRequestMessage(HttpMethod.Post, "/internal/usage/record");
            req.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
            req.Content = JsonContent.Create(payload);
            var res = await _client.SendAsync(req);
            Assert.Equal(HttpStatusCode.Created, res.StatusCode);
        }

        using var getRecords = new HttpRequestMessage(HttpMethod.Get, $"/internal/usage/records/{orgId}?page=1&pageSize=10");
        getRecords.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);

        var response = await _client.SendAsync(getRecords);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var records = await response.Content.ReadFromJsonAsync<List<AiUsageRecordResponse>>();
        Assert.NotNull(records);
        Assert.Equal(2, records.Count);
    }
}
