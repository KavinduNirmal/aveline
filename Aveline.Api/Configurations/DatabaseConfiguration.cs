using Aveline.Api.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Configurations;

public static class DatabaseConfiguration
{
    public static IServiceCollection AddAvelineDatabase(
        this IServiceCollection services,
        IConfiguration configuration)
    {
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

        services.AddDbContext<AppDbContext>(options =>
        {
            options.UseNpgsql(connectionString, npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
            });
        });

        return services;
    }
}
