using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Aveline.Api.Configurations;

public static class DatabaseConfiguration
{
    /// <summary>
    /// Explicit data-source name (S-11/R-16). Npgsql's
    /// <c>db.client.connection.pool.name</c> label defaults to the connection string with the
    /// password stripped, which would put the host, database and username into every pool series
    /// (and the connection string in the compose file carries <c>${POSTGRES_PASSWORD}</c>).
    /// </summary>
    public const string PoolName = "aveline";

    /// <summary>
    /// The documented escape hatch that lets a Production host keep the in-memory provider. It
    /// exists for test and demo hosts only, and mirrors
    /// <c>Media:AllowDatabaseProviderInProduction</c>: the safe default is refused loudly rather
    /// than accepted silently.
    /// </summary>
    public const string AllowInMemoryInProductionKey = "Database:AllowInMemoryInProduction";

    /// <summary>
    /// Overrides the EF Core in-memory store name. EF Core keys its internal service provider on
    /// the options fingerprint, so two contexts built with the SAME store name share one store for
    /// the life of the process. Test hosts set this per class so they cannot read each other's rows
    /// (see <c>Aveline.Api.Tests.TestDatabase</c>); every other host keeps the default.
    /// </summary>
    public const string InMemoryNameKey = "Database:InMemoryName";

    /// <summary>The store name used when nothing overrides it.</summary>
    public const string DefaultInMemoryName = "AvelineInMemoryDb";

    public static IServiceCollection AddAvelineDatabase(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        // Reads Npgsql's pool instruments for the collector's saturation emission (Slice 5).
        services.AddSingleton<NpgsqlPoolMetricsListener>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // For environments where PostgreSQL isn't configured, e.g. tests or local dev without
            // PostgreSQL. Production must opt in explicitly first: without a connection string the
            // API boots cleanly, answers every request, returns empty results and loses every write
            // on restart, which is indistinguishable from a data bug.
            EnsureInMemoryIsAllowed(environment, configuration);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(ResolveInMemoryName(configuration));
            });
            return services;
        }

        var dataSource = CreateDataSource(connectionString);
        services.AddSingleton(dataSource);

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(dataSource, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
        });

        return services;
    }

    /// <summary>
    /// Resolves the in-memory store name, falling back to <see cref="DefaultInMemoryName"/> when no
    /// override is configured.
    /// </summary>
    internal static string ResolveInMemoryName(IConfiguration configuration)
        => configuration[InMemoryNameKey] is { Length: > 0 } name ? name : DefaultInMemoryName;

    /// <summary>
    /// Builds the shared <see cref="NpgsqlDataSource"/> with an explicit
    /// <see cref="NpgsqlDataSourceBuilder.Name"/>, so the pool-name metric label is a bounded
    /// identifier rather than the connection string.
    /// </summary>
    internal static NpgsqlDataSource CreateDataSource(string connectionString)
        => new NpgsqlDataSourceBuilder(connectionString) { Name = PoolName }.Build();

    /// <summary>
    /// Fails fast when Production would run with no connection string. Other environments stay
    /// permissive so local development and tests need no database.
    /// </summary>
    private static void EnsureInMemoryIsAllowed(
        IHostEnvironment environment, IConfiguration configuration)
    {
        var isProduction = string.Equals(
            environment.EnvironmentName, Environments.Production, StringComparison.OrdinalIgnoreCase);

        if (!isProduction)
        {
            return;
        }

        if (bool.TryParse(configuration[AllowInMemoryInProductionKey], out var allowed) && allowed)
        {
            return;
        }

        throw new InvalidOperationException(
            "ConnectionStrings:DefaultConnection must be configured in Production. Without it the "
            + "API would boot against an EF Core in-memory database, serve empty results and lose "
            + "every write on restart without reporting an error. Set the "
            + "ConnectionStrings__DefaultConnection environment variable, or set "
            + "Database:AllowInMemoryInProduction=true to accept a non-persistent database "
            + "(test and demo hosts only).");
    }
}
