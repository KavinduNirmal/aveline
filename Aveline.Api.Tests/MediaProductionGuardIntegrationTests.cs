using System.Collections.Concurrent;
using System.Net;
using Aveline.Api.Modules.Media;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// S0 / U0.1a — the Production media-provider guard, asserted end to end through a real host
/// (strategy §3.4, Q11). <see cref="MediaOptionsValidatorTests"/> covers the rule against the
/// validator directly; these three cases prove the rule is actually wired into a booting host and
/// that the Production host factories only ever opt out by choosing the documented escape hatch.
/// <para>
/// That distinction is the whole point: setting <c>Media:AllowDatabaseProviderInProduction=true</c>
/// is a production-configuration choice that the guard logs at <see cref="LogLevel.Warning"/> — it
/// is not a disabled or test-aware guard.
/// </para>
/// </summary>
public class MediaProductionGuardIntegrationTests
{
    [Fact]
    public void ProductionDatabaseProvider_WithoutTheOverride_RefusesToStart()
    {
        // The safe default (Media:Provider=database) is exactly what must not boot a Production
        // host unless the operator has opted in.
        using var factory = ProductionFactory();

        var act = () => factory.CreateClient();

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:AllowDatabaseProviderInProduction*");
    }

    [Fact]
    public void ProductionDatabaseProvider_WithTheOverride_StartsAndLogsTheEscapeHatchAtWarning()
    {
        var sink = new CapturingLoggerProvider();
        using var factory = ProductionFactory(
            overrideSetting: ("Media:AllowDatabaseProviderInProduction", "true"),
            loggerProvider: sink);

        using var client = factory.CreateClient();

        sink.Entries.Should().Contain(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("Media:AllowDatabaseProviderInProduction", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TheDefaultHost_StartsWithNoMediaConfigurationAtAll()
    {
        // Development is WebApplicationFactory<Program>'s default environment. No Media:* key is
        // set here, so this pins the strategy's "safe default boots anywhere" promise.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:59999");
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
            });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = factory.Services.GetRequiredService<IOptions<MediaOptions>>().Value;
        options.Provider.Should().Be(MediaProvider.Database);
        options.AllowDatabaseProviderInProduction.Should().BeFalse();
    }

    private static WebApplicationFactory<Program> ProductionFactory(
        (string Key, string Value)? overrideSetting = null,
        ILoggerProvider? loggerProvider = null) =>
        new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.UseSetting("Clerk:Authority", "https://clerk.invalid");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                // Required by the Production startup guard (M-1).
                builder.UseSetting("Telemetry:IpHashSalt", "test-production-ip-salt");
                // Required by the Production scrape-token guard (S-1).
                builder.UseSetting("Metrics:ScrapeToken", "test-production-scrape-token");
                builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:59999");
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Observability:AgentIsCritical", "false");

                if (overrideSetting is { } setting)
                {
                    builder.UseSetting(setting.Key, setting.Value);
                }

                if (loggerProvider is not null)
                {
                    builder.ConfigureServices(services =>
                        services.AddLogging(logging => logging.AddProvider(loggerProvider)));
                }
            });

    /// <summary>Captures every log entry emitted by the booting host.</summary>
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Message)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
                => owner.Entries.Enqueue((logLevel, formatter(state, exception)));
        }
    }
}
