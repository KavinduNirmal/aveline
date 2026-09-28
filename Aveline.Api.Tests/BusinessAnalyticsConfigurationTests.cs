using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Analytics;
using Aveline.Api.Modules.Analytics.Services;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Business KPIs phase 1 (§5.4.6, §5.4.7, §5.8): the three new EF indexes, the options record
/// and its configuration section, and the module registration that binds them.
/// </summary>
public class BusinessAnalyticsConfigurationTests
{
    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"AnalyticsConfig_{Guid.NewGuid()}")
            .Options);

    // ── The three indexes ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Users_HasAnIndexLeadingWithCreatedAt()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(User))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(User.CreatedAt) }));
    }

    [Fact]
    public void Organizations_HasAnIndexLeadingWithCreatedAt()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Organization))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Organization.CreatedAt) }));
    }

    [Fact]
    public void Messages_HasAnIndexLeadingWithCreatedAt()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Message))!;

        // The existing index is (ConversationId, CreatedAt, Id): no leading CreatedAt, so a
        // platform-wide day-range filter cannot use it.
        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(Message.CreatedAt), nameof(Message.AuthorUserId) }));
    }

    [Fact]
    public void TheExistingMessageIndexIsStillDeclared()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(Message))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.Properties.Select(property => property.Name).SequenceEqual(new[]
            {
                nameof(Message.ConversationId), nameof(Message.CreatedAt), nameof(Message.Id),
            }));
    }

    // ── Options ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void OptionsCarryTheDocumentedDefaults()
    {
        var options = new BusinessAnalyticsOptions();

        Assert.Equal("BusinessAnalytics", BusinessAnalyticsOptions.SectionName);
        Assert.Equal(400, options.MaxWindowDays);
        Assert.Equal(30, options.MaxRollingWindowDays);
        Assert.Equal(60, options.CacheSeconds);
        Assert.Equal(3600, options.MetricCacheSeconds);
        Assert.False(options.RequireSharedCache);
        Assert.Equal(100, options.MaxRankingLimit);
        Assert.Equal("day", options.DefaultGranularity);
    }

    [Fact]
    public void TheModuleBindsTheBusinessAnalyticsSection()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessAnalytics:MaxWindowDays"] = "365",
                ["BusinessAnalytics:CacheSeconds"] = "30",
                ["BusinessAnalytics:RequireSharedCache"] = "true",
                ["BusinessAnalytics:DefaultGranularity"] = "week",
                ["Redis:ConnectionString"] = "localhost:6379",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDistributedMemoryCache();
        services.AddAnalyticsModule(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<BusinessAnalyticsOptions>>().Value;

        Assert.Equal(365, options.MaxWindowDays);
        Assert.Equal(30, options.CacheSeconds);
        Assert.True(options.RequireSharedCache);
        Assert.Equal("week", options.DefaultGranularity);
    }

    [Fact]
    public void TheModuleFailsStartupWhenASharedCacheIsRequiredWithoutRedis()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["BusinessAnalytics:RequireSharedCache"] = "true",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDistributedMemoryCache();

        var exception = Assert.Throws<InvalidOperationException>(
            () => services.AddAnalyticsModule(configuration));

        Assert.Contains("RequireSharedCache", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheModuleRegistersTheValidationAndCacheServices()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddDistributedMemoryCache();
        services.AddAnalyticsModule(configuration);

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<BusinessKpiCache>());
    }

    [Fact]
    public void TheRepositoryConfigurationExposesTheBusinessAnalyticsSection()
    {
        // The section must exist in the shipped appsettings, not only in code, so an operator
        // can see and change the knobs.
        var path = Path.Combine(RepositoryRoot(), "Aveline.Api", "appsettings.json");
        var text = File.ReadAllText(path);

        Assert.Contains("\"BusinessAnalytics\"", text, StringComparison.Ordinal);
        Assert.Contains("\"MaxWindowDays\"", text, StringComparison.Ordinal);
        Assert.Contains("\"RequireSharedCache\"", text, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateSharedCacheFailsStartupOnlyWhenAsked()
    {
        var strict = new BusinessAnalyticsOptions { RequireSharedCache = true };
        var relaxed = new BusinessAnalyticsOptions { RequireSharedCache = false };

        Assert.NotNull(BusinessKpiCache.ValidateSharedCache(strict, null));
        Assert.Null(BusinessKpiCache.ValidateSharedCache(strict, "localhost:6379"));
        Assert.Null(BusinessKpiCache.ValidateSharedCache(relaxed, null));
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root.");
    }
}
