using System.Reflection;
using Aveline.Api.Modules.Statistics.DTOs;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #212 — FR-5.9 privacy rule. The ingest contract may only carry a hash of tool
/// arguments and the size of a tool result; it must never grow a field that could carry
/// prompt text, tool arguments/results or customer content.
/// </summary>
public class AgentRunPrivacyTests
{
    private static readonly string[] ForbiddenConcepts =
    [
        "prompt", "content", "message", "args", "result", "text", "response", "body",
    ];

    // The only two sanctioned names: a one-way digest and a byte count.
    private static readonly HashSet<string> AllowedExceptions =
        new(StringComparer.OrdinalIgnoreCase) { "ArgsHash", "ResultBytes" };

    public static IEnumerable<object[]> IngestRequestTypes()
    {
        yield return [typeof(AgentRunReportRequest)];
        yield return [typeof(AgentStepReportRequest)];
        yield return [typeof(AgentStepAppendRequest)];
    }

    [Theory]
    [MemberData(nameof(IngestRequestTypes))]
    public void IngestDto_DoesNotExposeForbiddenContentFields(Type type)
    {
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var normalized = property.Name.ToLowerInvariant();
            var matchesForbidden = ForbiddenConcepts.Any(normalized.Contains);
            if (!matchesForbidden)
            {
                continue;
            }

            Assert.True(
                AllowedExceptions.Contains(property.Name),
                $"{type.Name}.{property.Name} exposes a forbidden telemetry concept.");
        }
    }

    [Fact]
    public void AgentStepDto_ExposesOnlyArgsHashAndResultBytesForToolData()
    {
        var names = typeof(AgentStepReportRequest)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .ToArray();

        Assert.Contains("ArgsHash", names);
        Assert.Contains("ResultBytes", names);
        Assert.DoesNotContain("Args", names);
        Assert.DoesNotContain("Result", names);
    }

    [Fact]
    public void ArgsHash_IsAStringDigestNotAContentBag()
    {
        var property = typeof(AgentStepReportRequest).GetProperty(nameof(AgentStepReportRequest.ArgsHash));

        Assert.NotNull(property);
        Assert.Equal(typeof(string), property!.PropertyType);
    }
}
