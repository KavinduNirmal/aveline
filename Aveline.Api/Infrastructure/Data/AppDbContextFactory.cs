using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Aveline.Api.Infrastructure.Data;

/// <summary>
/// Design-time factory used only by <c>dotnet ef</c> commands (migrations, bundles).
/// The running application resolves its connection through
/// <c>ConnectionStrings:DefaultConnection</c> in <c>DatabaseConfiguration</c>; this factory is
/// never consulted at runtime.
///
/// <para>
/// It used to hardcode <c>Host=localhost;Username=postgres;Password=postgres</c>. That put a
/// superuser credential in source - readable from the built artefact by anyone who has it, and
/// normalising a privileged default that invites reuse. The credential is gone.
/// </para>
///
/// <para>
/// Resolution order:
/// <list type="number">
///   <item><c>ConnectionStrings__DefaultConnection</c> / <c>ConnectionStrings:DefaultConnection</c>
///     when the caller supplies one (local work against a real database).</item>
///   <item>Otherwise a string composed from the same <c>POSTGRES_*</c> variables
///     <c>docker-compose.yml</c> uses, which keeps <c>dotnet ef migrations bundle</c> working in
///     CI where no database credential is exported. The result is only a *shape*: the bundle
///     receives its real connection at run time via <c>./efbundle --connection "$CONN"</c>
///     (see .github/workflows/deploy.yml), so nothing here reaches Production.</item>
///   <item>Otherwise a non-routable placeholder host, named so the failure is legible, rather than
///     a privileged local server the command might actually reach.</item>
/// </list>
/// </para>
/// </summary>
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    /// <summary>The configuration key shared with the runtime registration.</summary>
    public const string ConnectionStringKey = "ConnectionStrings:DefaultConnection";

    /// <summary>
    /// Host used when nothing is configured. Deliberately not <c>localhost</c>: this is not a
    /// database the command should ever reach, and a name that cannot resolve makes that obvious.
    /// </summary>
    public const string DesignTimePlaceholderHost = "design-time-placeholder.invalid";

    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString = ResolveConnectionString();
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseNpgsql(connectionString, npgsqlOptions =>
        {
            npgsqlOptions.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
        });

        return new AppDbContext(optionsBuilder.Options);
    }

    /// <summary>
    /// The connection string this factory hands to EF. <paramref name="read"/> defaults to the
    /// process environment and is injectable so tests can assert the resolution rules without
    /// mutating global state.
    /// </summary>
    public static string ResolveConnectionString(Func<string, string?>? read = null)
    {
        read ??= Environment.GetEnvironmentVariable;

        var configured = read("ConnectionStrings__DefaultConnection")
            ?? read(ConnectionStringKey);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        return ComposeFromComposeVariables(read);
    }

    private static string ComposeFromComposeVariables(Func<string, string?> read)
    {
        var host = read("POSTGRES_HOST");
        var database = read("POSTGRES_DB");
        var user = read("POSTGRES_USER");

        // Nothing configured at all: use the named placeholder so the failure mode is legible.
        if (string.IsNullOrWhiteSpace(host)
            && string.IsNullOrWhiteSpace(database)
            && string.IsNullOrWhiteSpace(user))
        {
            return $"Host={DesignTimePlaceholderHost};Port=5432;Database=aveline;"
                + "Username=design_time;Password=unset";
        }

        var port = read("POSTGRES_INTERNAL_PORT") ?? read("POSTGRES_PORT") ?? "5432";
        var password = read("POSTGRES_PASSWORD") ?? string.Empty;

        return $"Host={host ?? DesignTimePlaceholderHost};Port={port};"
            + $"Database={database ?? "aveline"};Username={user ?? "aveline"};Password={password}";
    }
}
