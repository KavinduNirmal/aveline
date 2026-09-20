using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Tests;

/// <summary>
/// Slice 1, S-1/R-1: /metrics must not be readable with the committed internal service token in
/// Production. The guard mirrors <see cref="TelemetrySecurityGuard"/> and must stay permissive
/// outside Production so local development and the rest of the suite need no secret.
/// </summary>
public class MetricsSecurityGuardTests
{
    private static IConfiguration Configuration(string? scrapeToken) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Metrics:ScrapeToken"] = scrapeToken,
            })
            .Build();

    private static IHostEnvironment Environment(string name) =>
        new StubHostEnvironment { EnvironmentName = name };

    [Fact]
    public void Production_WithoutAToken_Throws()
    {
        var act = () => MetricsSecurityGuard.EnsureScrapeTokenForProduction(
            Environment(Environments.Production), Configuration(null));

        act.Should().Throw<InvalidOperationException>().WithMessage("*Metrics:ScrapeToken*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Production_WithAnEmptyToken_Throws(string token)
    {
        var act = () => MetricsSecurityGuard.EnsureScrapeTokenForProduction(
            Environment(Environments.Production), Configuration(token));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Production_WithAToken_Succeeds()
    {
        var act = () => MetricsSecurityGuard.EnsureScrapeTokenForProduction(
            Environment(Environments.Production), Configuration(new string('a', 64)));

        act.Should().NotThrow();
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Staging")]
    public void NonProduction_WithoutAToken_IsPermissive(string environment)
    {
        var act = () => MetricsSecurityGuard.EnsureScrapeTokenForProduction(
            Environment(environment), Configuration(null));

        act.Should().NotThrow();
    }

    private sealed class StubHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;

        public string ApplicationName { get; set; } = "Aveline.Api.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } =
            new Microsoft.Extensions.FileProviders.NullFileProvider();
    }
}
