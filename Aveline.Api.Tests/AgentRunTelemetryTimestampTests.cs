using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Regression coverage for the timestamp-kind defect in agent run ingest: every run report
/// returned 500 and no run telemetry was ever persisted.
/// </summary>
/// <remarks>
/// The agent service serializes timestamps with <c>datetime.now(UTC).isoformat()</c>, producing an
/// offset-bearing string such as <c>2026-09-22T20:47:56.123456+00:00</c>. System.Text.Json
/// materializes that into <see cref="DateTimeKind.Local"/>, and Npgsql refuses non-UTC kinds for
/// <c>timestamp with time zone</c>:
/// <c>Cannot write DateTime with Kind=Local to PostgreSQL type 'timestamp with time zone'</c>.
///
/// <para>
/// These assertions require a real PostgreSQL container: the in-memory provider performs no
/// timestamp-kind validation at all, so it cannot reproduce the failure and would pass against
/// the broken code. The body is posted as a raw JSON string on purpose, so the wire format matches
/// what the Python service actually emits.
/// </para>
/// </remarks>
public class AgentRunTelemetryTimestampTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

    /// <summary>A fixed instant with a zero UTC offset, and its offsetless equivalent.</summary>
    private const string OffsetBearingStartedAt = "2026-09-22T20:47:56.123456+00:00";

    private const string OffsetlessStartedAt = "2026-09-22T20:47:56.123456";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using (var bootstrap = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql(_postgres.GetConnectionString())
                .Options))
        {
            await bootstrap.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
            await bootstrap.Database.MigrateAsync();
        }

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
                builder.UseSetting("ConnectionStrings:DefaultConnection", _postgres.GetConnectionString());
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task RunReport_WithOffsetBearingTimestamp_IsPersistedAsUtc()
    {
        const string workflowId = "wf-offset-bearing";
        var expected = DateTime.Parse(
            OffsetBearingStartedAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AdjustToUniversal
            | System.Globalization.DateTimeStyles.AssumeUniversal);

        var response = await PostReportAsync(RunJson(workflowId, OffsetBearingStartedAt));

        // The defect surfaced here as 500 from the GlobalExceptionHandler.
        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        await using var context = new AppDbContext(Options());
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);

        run.StartedAt.Kind.Should().Be(DateTimeKind.Utc);
        run.StartedAt.Should().Be(expected);
        run.CompletedAt!.Value.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task RunReport_WithOffsetlessTimestamp_IsTreatedAsUtc()
    {
        const string workflowId = "wf-offsetless";
        var expected = DateTime.Parse(
            OffsetlessStartedAt,
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal
            | System.Globalization.DateTimeStyles.AdjustToUniversal);

        var response = await PostReportAsync(RunJson(workflowId, OffsetlessStartedAt));

        response.StatusCode.Should().BeOneOf(HttpStatusCode.Created, HttpStatusCode.OK);

        await using var context = new AppDbContext(Options());
        var run = await context.AgentWorkflowRuns.SingleAsync(r => r.WorkflowId == workflowId);

        // No offset means no information; the documented contract is that the agent reports UTC,
        // so the wall-clock value is taken as UTC rather than silently reinterpreted as local time.
        run.StartedAt.Kind.Should().Be(DateTimeKind.Utc);
        run.StartedAt.Should().Be(expected);
    }

    /// <summary>
    /// Demonstrates the underlying provider constraint that made the defect bite, and so keeps the
    /// harness honest: if this stopped throwing, the two tests above could pass without the
    /// normalization actually doing anything and would cease to be regression coverage.
    /// </summary>
    [Fact]
    public async Task NonUtcDateTime_IsRejectedByTheProvider()
    {
        await using var context = new AppDbContext(Options());
        context.AgentWorkflowRuns.Add(new AgentWorkflowRun
        {
            Id = Guid.CreateVersion7(),
            WorkflowId = "wf-probe-non-utc",
            Status = AgentRunStatus.Succeeded,
            StartedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 22, 20, 47, 56), DateTimeKind.Local),
            CompletedAt = DateTime.SpecifyKind(new DateTime(2026, 9, 22, 20, 47, 56), DateTimeKind.Local),
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
    }

    private DbContextOptions<AppDbContext> Options() =>
        new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;

    /// <summary>
    /// Posts a report as a raw JSON string so the timestamp reaches the server exactly as written,
    /// rather than being re-serialized from a <see cref="DateTime"/> by the test client.
    /// </summary>
    private async Task<HttpResponseMessage> PostReportAsync(string json)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/agent-runs")
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add(InternalServiceAuthHandler.HeaderName, InternalToken);
        return await _client.SendAsync(request);
    }

    private static string RunJson(string workflowId, string startedAt) => $$"""
        {
          "workflowId": "{{workflowId}}",
          "triggerKind": "ApiRequest",
          "status": "Succeeded",
          "startedAt": "{{startedAt}}",
          "completedAt": "{{startedAt}}",
          "toolCallCount": 0,
          "retryCount": 0,
          "inputTokens": 0,
          "outputTokens": 0,
          "cachedTokens": 0,
          "actualCostUsd": 0,
          "blossomUnits": 0
        }
        """;
}
