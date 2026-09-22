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
/// Issue #211 / the tenant lockout — the team-only agent statistics subset
/// (docs/api/README.md §C.6). Verifies that the org-scoped routes are gone and that the admin
/// subset still enforces <c>stats:system</c>.
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

    /// <summary>
    /// The lockout. A boutique reads its usage in Blossoms; the org-scoped agent statistics routes
    /// were removed rather than merely denied, so every one of them answers 404 for an owner token
    /// — not 401, not 403, and not 200. Only the team-only admin subset remains.
    /// </summary>
    [Theory]
    [InlineData("runs")]
    [InlineData("reliability")]
    [InlineData("latency")]
    [InlineData("steps")]
    [InlineData("tokens")]
    [InlineData("cost")]
    [InlineData("tools")]
    [InlineData("failures")]
    [InlineData("approvals")]
    public async Task OrgAgentStatisticsRoutes_AreNotMounted(string segment)
    {
        var suffix = Guid.NewGuid().ToString("N");
        var (orgA, ownerA) = await SeedOwnerAsync($"gone{suffix}");
        var token = CreateToken(ownerA, orgRole: Roles.BoutiqueOwner);

        var response = await GetAsync($"/api/v1/orgs/{orgA}/statistics/agents/{segment}", token);

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
