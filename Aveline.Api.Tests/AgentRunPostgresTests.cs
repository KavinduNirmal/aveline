using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #209 — PostgreSQL-backed invariants for the agentic statistics schema the
/// in-memory provider cannot exercise: the <c>NULLS NOT DISTINCT</c> run uniqueness, the
/// step ordering index and the step cascade.
/// </summary>
public class AgentRunPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgId;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();

        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"ar_{ownerId:N}", Email = "ar@aveline.lk",
            FirstName = "Agent", LastName = "Owner", Username = $"ar_{ownerId:N}",
            UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization { Name = "Agent Boutique", Slug = $"ar-{ownerId:N}", OwnerUserId = ownerId };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        _orgId = org.Id;
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static AgentWorkflowRun Run(Guid? organizationId, string workflowId) => new()
    {
        OrganizationId = organizationId,
        WorkflowId = workflowId,
        StartedAt = DateTime.UtcNow,
        Status = AgentRunStatus.Succeeded,
        CompletedAt = DateTime.UtcNow,
        IsUnattributed = organizationId is null,
    };

    [Fact]
    public async Task DuplicateOrganizationAndWorkflow_IsRejected()
    {
        await using var context = new AppDbContext(_options);
        context.AgentWorkflowRuns.Add(Run(_orgId, "wf-duplicate"));
        await context.SaveChangesAsync();

        context.AgentWorkflowRuns.Add(Run(_orgId, "wf-duplicate"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task TwoUnattributedRunsWithTheSameWorkflow_AreRejected()
    {
        await using var context = new AppDbContext(_options);
        context.AgentWorkflowRuns.Add(Run(null, "wf-unattributed"));
        await context.SaveChangesAsync();

        context.AgentWorkflowRuns.Add(Run(null, "wf-unattributed"));
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DuplicateStepOrderWithinARun_IsRejected()
    {
        await using var context = new AppDbContext(_options);
        var run = Run(_orgId, "wf-steps");
        context.AgentWorkflowRuns.Add(run);
        await context.SaveChangesAsync();

        context.AgentStepRuns.Add(new AgentStepRun
        {
            WorkflowRunId = run.Id, OrganizationId = _orgId, StepIndex = 0,
            AgentKey = "orchestrator", NodeName = "plan", StartedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        context.AgentStepRuns.Add(new AgentStepRun
        {
            WorkflowRunId = run.Id, OrganizationId = _orgId, StepIndex = 0,
            AgentKey = "orchestrator", NodeName = "plan", StartedAt = DateTime.UtcNow,
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingARun_CascadesItsSteps()
    {
        Guid runId;
        await using (var context = new AppDbContext(_options))
        {
            var run = Run(_orgId, "wf-cascade");
            context.AgentWorkflowRuns.Add(run);
            context.AgentStepRuns.Add(new AgentStepRun
            {
                WorkflowRunId = run.Id, OrganizationId = _orgId, StepIndex = 0,
                AgentKey = "customer_memory", NodeName = "recall", StartedAt = DateTime.UtcNow,
            });
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        await using (var context = new AppDbContext(_options))
        {
            var run = await context.AgentWorkflowRuns.SingleAsync(r => r.Id == runId);
            context.AgentWorkflowRuns.Remove(run);
            await context.SaveChangesAsync();
        }

        await using (var context = new AppDbContext(_options))
        {
            Assert.Empty(await context.AgentStepRuns.Where(s => s.WorkflowRunId == runId).ToListAsync());
        }
    }

    [Fact]
    public async Task AiUsageRecord_CanLinkToAtMostOneRun()
    {
        Guid runId;
        await using (var context = new AppDbContext(_options))
        {
            var run = Run(_orgId, "wf-usage");
            context.AgentWorkflowRuns.Add(run);
            await context.SaveChangesAsync();
            runId = run.Id;
        }

        await using (var context = new AppDbContext(_options))
        {
            context.AiUsageRecords.Add(new Modules.Billing.Models.AiUsageRecord
            {
                OrganizationId = _orgId, RequestId = "r1", WorkflowId = "wf-usage",
                Provider = "openai", Model = "gpt-4o", AgentWorkflowRunId = runId,
            });
            await context.SaveChangesAsync();
        }

        await using (var context = new AppDbContext(_options))
        {
            context.AiUsageRecords.Add(new Modules.Billing.Models.AiUsageRecord
            {
                OrganizationId = _orgId, RequestId = "r2", WorkflowId = "wf-usage",
                Provider = "openai", Model = "gpt-4o", AgentWorkflowRunId = runId,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }
}
