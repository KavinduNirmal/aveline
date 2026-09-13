using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #242 / M-1 — an empty <c>Telemetry:IpHashSalt</c> in Production must fail fast at
/// startup; other environments keep the previous permissive behaviour.
/// </summary>
public class TelemetrySecurityGuardTests
{
    private static IConfiguration Configuration(string? salt) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Telemetry:IpHashSalt"] = salt,
            })
            .Build();

    private static IHostEnvironment Environment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_WithEmptySalt_Throws(string? salt)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            TelemetrySecurityGuard.EnsureIpHashSaltForProduction(
                Environment("Production"), Configuration(salt)));

        Assert.Contains("Telemetry:IpHashSalt", exception.Message);
    }

    [Fact]
    public void Production_WithSalt_DoesNotThrow()
    {
        TelemetrySecurityGuard.EnsureIpHashSaltForProduction(
            Environment("Production"), Configuration("a-real-salt"));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    [InlineData("Test")]
    public void NonProduction_WithEmptySalt_DoesNotThrow(string environmentName)
    {
        TelemetrySecurityGuard.EnsureIpHashSaltForProduction(
            Environment(environmentName), Configuration(""));
    }

    [Theory]
    [InlineData("PRODUCTION")]
    [InlineData("production")]
    public void ProductionMatch_IsCaseInsensitive(string environmentName)
    {
        Assert.Throws<InvalidOperationException>(() =>
            TelemetrySecurityGuard.EnsureIpHashSaltForProduction(
                Environment(environmentName), Configuration("")));
    }
}
