using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.SystemHealth.HealthChecks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #180 — the readiness payload shape (FR-7.2, FR-7.3, BR-7.3).
/// </summary>
public class HealthCheckResponseWriterTests
{
    /// <summary>An anonymous probe: the shape an external monitor sees.</summary>
    private static async Task<JsonElement> WriteAnonymousAsync(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        await HealthCheckResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    /// <summary>An authenticated internal service caller: the detailed operator view.</summary>
    private static async Task<JsonElement> WriteInternalAsync(HealthReport report)
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Role, InternalTokenAuthenticationHandler.RoleName)],
            authenticationType: InternalTokenAuthenticationHandler.SchemeName));

        await HealthCheckResponseWriter.WriteAsync(context, report);

        context.Response.Body.Position = 0;
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        var json = await reader.ReadToEndAsync();
        return JsonDocument.Parse(json).RootElement.Clone();
    }

    [Fact]
    public async Task WriteAsync_MapsStatusesAndDurations()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(
                    HealthStatus.Healthy, "Database reachable.", TimeSpan.FromMilliseconds(4), null, null),
                ["agent-service"] = new(
                    HealthStatus.Degraded, "Agent service is unreachable.", TimeSpan.FromMilliseconds(31), null, null),
            },
            TimeSpan.FromMilliseconds(41));

        var root = await WriteInternalAsync(report);

        Assert.Equal("Degraded", root.GetProperty("status").GetString());
        Assert.Equal(41, root.GetProperty("totalDurationMs").GetInt32());
        Assert.True(root.TryGetProperty("version", out var version));
        Assert.True(version.TryGetProperty("gitSha", out _));
        Assert.True(version.TryGetProperty("buildTime", out _));
        Assert.True(version.TryGetProperty("assemblyVersion", out _));
        Assert.True(version.TryGetProperty("environment", out _));
    }

    [Fact]
    public async Task WriteAsync_OrdersKnownChecksByName()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["clerk-jwks"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                ["database"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                ["agent-service"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
                ["redis"] = new(HealthStatus.Healthy, null, TimeSpan.Zero, null, null),
            },
            TimeSpan.Zero);

        var root = await WriteInternalAsync(report);
        var names = root.GetProperty("checks").EnumerateArray()
            .Select(c => c.GetProperty("name").GetString() ?? string.Empty)
            .ToArray();

        Assert.Equal(["database", "redis", "agent-service", "clerk-jwks"], names);
    }

    [Fact]
    public async Task WriteAsync_AnonymousProbe_WithholdsDependencyNamesAndVersion()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(
                    HealthStatus.Healthy, "Database reachable.", TimeSpan.FromMilliseconds(4), null, null),
                ["redis"] = new(
                    HealthStatus.Healthy, "Redis responded in 0 ms.", TimeSpan.Zero, null, null),
            },
            TimeSpan.FromMilliseconds(9));

        var root = await WriteAnonymousAsync(report);

        // The aggregate verdict still answers the orchestrator's question.
        Assert.Equal("Healthy", root.GetProperty("status").GetString());
        Assert.Equal(9, root.GetProperty("totalDurationMs").GetInt32());

        // ...but the internal topology is not on offer to an unauthenticated probe (F-1.5).
        Assert.False(root.TryGetProperty("checks", out _));
        Assert.False(root.TryGetProperty("version", out _));

        var body = root.GetRawText();
        Assert.DoesNotContain("database", body);
        Assert.DoesNotContain("redis", body);
        Assert.DoesNotContain("agent-service", body);
        Assert.DoesNotContain("clerk-jwks", body);
    }

    [Fact]
    public async Task WriteAsync_NeverLeaksExceptionDetails()
    {
        var report = new HealthReport(
            new Dictionary<string, HealthReportEntry>
            {
                ["database"] = new(
                    HealthStatus.Unhealthy,
                    "Database is unreachable.",
                    TimeSpan.Zero,
                    new InvalidOperationException("Host=secret-host;Password=hunter2"),
                    null),
            },
            TimeSpan.Zero);

        foreach (var root in new[] { await WriteAnonymousAsync(report), await WriteInternalAsync(report) })
        {
            var body = root.GetRawText();
            Assert.DoesNotContain("hunter2", body);
            Assert.DoesNotContain("secret-host", body);
            Assert.DoesNotContain("InvalidOperationException", body);
        }
    }
}
