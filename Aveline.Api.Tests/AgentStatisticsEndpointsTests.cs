using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #211 — the agent statistics endpoints (docs/api/README.md §C.6). Verifies
/// organisation scoping, the <c>dataQuality</c> envelope, input validation and policy
/// enforcement including the team-only admin subset.
/// </summary>
public class AgentStatisticsEndpointsTests : IAsyncLifetime
{
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
                builder.UseSetting("AgentStats:MinSampleForPercentile", "3");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId, string? userRole = null, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private Task<HttpResponseMessage> GetAsync(string path, string token)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
        return _client.SendAsync(request);
    }

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    private static async Task<(Guid OrgId, string OwnerClerkId)> SeedOwnerAsync(string suffix)
    {
        await using var context = Context();

        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"agentstats_owner_{suffix}",
            Email = $"agentstats_{suffix}@aveline.lk",
            FirstName = "Stats",
            LastName = "Owner",
            Username = $"agentstats_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);

        var org = new Organization
        {
            Name = $"Agent Stats Org {suffix}",
            Slug = $"agent-stats-{suffix}",
            OwnerUserId = owner.Id,
        };
        context.Organizations.Add(org);

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
        });

        await context.SaveChangesAsync();
        return (org.Id, owner.ClerkId);
    }

    private static async Task SeedAdminAsync(string clerkId)
    {
        await using var context = Context();
        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Admin",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    private static async Task<Guid> SeedRunAsync(
        Guid? organizationId, string workflowId, AgentRunStatus status, int durationMs,
        string[] agents, string? errorCode = null, AgentStepStatus stepStatus = AgentStepStatus.Succeeded)
    {
        await using var context = Context();

        var run = new AgentWorkflowRun
        {
            OrganizationId = organizationId,
            WorkflowId = workflowId,
            Status = status,
            TriggerKind = AgentRunTriggerKind.ApiRequest,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            CompletedAt = status is AgentRunStatus.Running or AgentRunStatus.PausedForApproval
                ? null
                : DateTime.UtcNow.AddMinutes(-10).AddMilliseconds(durationMs),
            DurationMs = durationMs,
            ErrorCode = errorCode,
            AgentsInvolved = [.. agents],
            IsUnattributed = organizationId is null,
        };
        context.AgentWorkflowRuns.Add(run);

        context.AgentStepRuns.Add(new AgentStepRun
        {
            WorkflowRunId = run.Id,
            OrganizationId = organizationId,
            StepIndex = 0,
            AgentKey = agents[0],
            NodeName = "plan",
            StepKind = AgentStepKind.LlmCall,
            Status = stepStatus,
            StartedAt = DateTime.UtcNow.AddMinutes(-10),
            DurationMs = durationMs,
            Provider = "openai",
            Model = "gpt-4o",
            InputTokens = 10,
            OutputTokens = 5,
        });

        await context.SaveChangesAsync();
        return run.Id;
    }

    [Fact]
    public async Task Runs_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync(
            $"/api/v1/orgs/{Guid.CreateVersion7()}/statistics/agents/runs");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Runs_ReturnOnlyTheCallersOrganizationAndDataQualityFlags()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"a{suffix}");
        var (orgB, _) = await SeedOwnerAsync($"b{suffix}");
        await SeedRunAsync(orgA, $"wf-a-{suffix}", AgentRunStatus.Succeeded, 120, ["customer_memory"]);
        await SeedRunAsync(orgB, $"wf-b-{suffix}", AgentRunStatus.Failed, 500, ["visual_insight"], "llm_error");
        await SeedRunAsync(null, $"wf-u-{suffix}", AgentRunStatus.Succeeded, 90, ["commerce"]);

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/agents/runs", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = body.GetProperty("items").EnumerateArray().ToArray();

        Assert.Single(items);
        Assert.Equal($"wf-a-{suffix}", items[0].GetProperty("workflowId").GetString());
        Assert.Equal(1, body.GetProperty("total").GetInt32());

        var quality = body.GetProperty("dataQuality");
        Assert.False(quality.GetProperty("latencyInstrumented").GetBoolean());
        Assert.False(quality.GetProperty("nodeFailuresObserved").GetBoolean());
        Assert.False(quality.GetProperty("perStepAttribution").GetBoolean());
        Assert.False(quality.GetProperty("toolInstrumented").GetBoolean());
        Assert.False(quality.GetProperty("costInstrumented").GetBoolean());
    }

    [Fact]
    public async Task Runs_AsOutsider_Returns403()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, _) = await SeedOwnerAsync($"owner{suffix}");
        await SeedAdminAsync($"agentstats_outsider_{suffix}");
        var token = CreateToken($"agentstats_outsider_{suffix}", orgRole: Roles.BoutiqueOwner);

        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/agents/runs", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("status=not-a-status")]
    [InlineData("triggerKind=not-a-trigger")]
    public async Task Runs_WithAnInvalidEnum_Returns400WithMessage(string query)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"invalid{suffix}");
        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);

        var response = await GetAsync(
            $"/api/v1/orgs/{orgA}/statistics/agents/runs?{query}", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("message", out var message));
        Assert.False(string.IsNullOrWhiteSpace(message.GetString()));
    }

    [Fact]
    public async Task Reliability_ExcludesRunningAndPausedFromTheDenominator()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"rel{suffix}");
        await SeedRunAsync(orgA, $"wf-rel-ok-{suffix}", AgentRunStatus.Succeeded, 100, ["orchestrator"]);
        await SeedRunAsync(orgA, $"wf-rel-bad-{suffix}", AgentRunStatus.Failed, 200, ["orchestrator"], "agent_error");
        await SeedRunAsync(orgA, $"wf-rel-run-{suffix}", AgentRunStatus.Running, 0, ["orchestrator"]);

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/agents/reliability", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(2, body.GetProperty("denominator").GetInt32());
        Assert.Equal(0.5, body.GetProperty("successRate").GetDouble());
        Assert.False(body.GetProperty("dataQuality").GetProperty("nodeFailuresObserved").GetBoolean());
    }

    [Fact]
    public async Task Latency_IsAllNullWhenDurationsAreNotInstrumented()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"lat{suffix}");
        await SeedRunAsync(orgA, $"wf-lat-{suffix}", AgentRunStatus.Succeeded, 120, ["orchestrator"]);

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/agents/latency", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(JsonValueKind.Null, body.GetProperty("p50Ms").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.GetProperty("p95Ms").ValueKind);
        Assert.False(body.GetProperty("dataQuality").GetProperty("latencyInstrumented").GetBoolean());
    }

    [Fact]
    public async Task RunDetail_ForAnotherOrganization_Returns404()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"detail{suffix}");
        var (orgB, _) = await SeedOwnerAsync($"other{suffix}");
        var runId = await SeedRunAsync(orgB, $"wf-secret-{suffix}", AgentRunStatus.Succeeded, 100, ["orchestrator"]);

        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);
        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/agents/runs/{runId}", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task AdminRuns_AsAdmin_ReturnsSystemWideResults()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, _) = await SeedOwnerAsync($"adminruns{suffix}");
        await SeedRunAsync(orgA, $"wf-admin-{suffix}", AgentRunStatus.Succeeded, 100, ["orchestrator"]);
        await SeedAdminAsync($"agentstats_admin_{suffix}");
        var token = CreateToken($"agentstats_admin_{suffix}", userRole: Roles.Admin);

        var response = await GetAsync("/api/v1/admin/statistics/agents/overview", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.GetProperty("totalRuns").GetInt32() >= 1);
        Assert.False(body.GetProperty("dataQuality").GetProperty("costInstrumented").GetBoolean());
    }

    [Fact]
    public async Task AdminRuns_AsBoutiqueOwner_Returns403()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (_, owner) = await SeedOwnerAsync($"adminforbidden{suffix}");
        var token = CreateToken(owner, orgRole: Roles.BoutiqueOwner);

        var response = await GetAsync("/api/v1/admin/statistics/agents/runs", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
