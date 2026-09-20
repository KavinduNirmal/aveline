using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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

    public static IServiceCollection AddAvelineDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Reads Npgsql's pool instruments for the collector's saturation emission (Slice 5).
        services.AddSingleton<NpgsqlPoolMetricsListener>();

        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? configuration["ConnectionStrings:DefaultConnection"];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // For environments where PostgreSQL isn't configured, e.g. tests or local dev without PostgreSQL
            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase("AvelineInMemoryDb");
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
    /// Builds the shared <see cref="NpgsqlDataSource"/> with an explicit
    /// <see cref="NpgsqlDataSourceBuilder.Name"/>, so the pool-name metric label is a bounded
    /// identifier rather than the connection string.
    /// </summary>
    internal static NpgsqlDataSource CreateDataSource(string connectionString)
        => new NpgsqlDataSourceBuilder(connectionString) { Name = PoolName }.Build();
}
