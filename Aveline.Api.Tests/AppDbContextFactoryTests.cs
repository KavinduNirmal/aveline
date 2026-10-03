using Aveline.Api.Infrastructure.Data;

namespace Aveline.Api.Tests;

/// <summary>
/// F-1.6 / PF-1.3 — the design-time factory must not carry a privileged credential in source.
/// It previously hardcoded <c>Host=localhost;Username=postgres;Password=postgres</c>, a superuser
/// pair anyone holding the built artefact could read. These tests pass a stub reader, so they
/// never mutate the process environment.
/// </summary>
public class AppDbContextFactoryTests
{
    private static Func<string, string?> Reader(params (string Key, string? Value)[] settings)
    {
        var map = settings.ToDictionary(s => s.Key, s => s.Value, StringComparer.Ordinal);
        return key => map.GetValueOrDefault(key);
    }

    [Fact]
    public void Resolve_WithExplicitConnectionString_UsesIt()
    {
        var resolved = AppDbContextFactory.ResolveConnectionString(
            Reader(("ConnectionStrings__DefaultConnection", "Host=explicit.example;Database=db")));

        resolved.Should().Be("Host=explicit.example;Database=db");
    }

    [Fact]
    public void Resolve_WithNothingConfigured_UsesTheNonRoutablePlaceholder()
    {
        var resolved = AppDbContextFactory.ResolveConnectionString(Reader());

        resolved.Should().Contain(AppDbContextFactory.DesignTimePlaceholderHost);
        // Never a server the command could reach by accident.
        resolved.Should().NotContain("Host=localhost");
    }

    [Fact]
    public void Resolve_FromComposeVariables_ComposesTheExpectedShape()
    {
        var resolved = AppDbContextFactory.ResolveConnectionString(Reader(
            ("POSTGRES_HOST", "postgres"),
            ("POSTGRES_DB", "aveline"),
            ("POSTGRES_USER", "aveline"),
            ("POSTGRES_INTERNAL_PORT", "5432"),
            ("POSTGRES_PASSWORD", "s3cret")));

        resolved.Should().Be(
            "Host=postgres;Port=5432;Database=aveline;Username=aveline;Password=s3cret");
    }

    [Fact]
    public void Resolve_NeverProducesTheRetiredSuperuserCredential()
    {
        // The regression this guards: the factory must not *invent* postgres/postgres when nothing
        // is configured. If the environment explicitly supplies that pair, using it is correct -
        // the finding was a hardcoded credential in source, not a refusal to honour configuration.
        var placeholder = AppDbContextFactory.ResolveConnectionString(Reader());

        placeholder.Should().NotContain("Username=postgres");
        placeholder.Should().NotContain("Password=postgres");
        placeholder.Should().NotContain("Username=postgres;Password=postgres");
    }

    [Fact]
    public void Source_DoesNotHardcodeTheRetiredSuperuserCredential()
    {
        // Belt-and-braces over the resolution logic: the literal must be absent from real code.
        // Comments are stripped first, because the class documentation quotes the retired
        // credential when explaining why it was removed.
        var code = StripComments(File.ReadAllText(SourcePath()));

        // Environment variable *names* legitimately contain "POSTGRES"; only the credential
        // assignments matter.
        code.Should().NotContain("Username=postgres");
        code.Should().NotContain("Password=postgres");
    }

    /// <summary>Removes <c>//</c> line comments and <c>/* */</c> blocks so only code is asserted on.</summary>
    private static string StripComments(string source)
    {
        var withoutBlocks = System.Text.RegularExpressions.Regex.Replace(
            source, @"/\*.*?\*/", string.Empty,
            System.Text.RegularExpressions.RegexOptions.Singleline);
        return System.Text.RegularExpressions.Regex.Replace(
            withoutBlocks, @"//[^\n]*", string.Empty);
    }

    private static string SourcePath()
    {
        // Walk up from the test assembly to the repository root. `dotnet-tools.json` sits at the
        // root; the solution file sits under Aveline.Api/, so it is not a usable marker here.
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "dotnet-tools.json")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run inside the repository");
        return Path.Combine(
            directory!.FullName, "Aveline.Api", "Infrastructure", "Data", "AppDbContextFactory.cs");
    }

    [Fact]
    public void Resolve_PrefersTheExplicitStringOverComposeVariables()
    {
        var resolved = AppDbContextFactory.ResolveConnectionString(Reader(
            ("ConnectionStrings__DefaultConnection", "Host=explicit.example;Database=db"),
            ("POSTGRES_HOST", "postgres")));

        resolved.Should().Be("Host=explicit.example;Database=db");
    }
}
