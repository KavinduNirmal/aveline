using Aveline.Api.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// F-2.6 — the internal service token must not be a value the repository publishes. The previous
/// guard compared against one literal (<c>change-me-internal-token</c>) and accepted
/// <c>aveline-local-development-secret-token-2026</c>, the fallback in <c>docker-compose.yml</c>.
/// </summary>
public class InsecureInternalTokensTests
{
    private static readonly string StrongToken =
        Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));

    [Theory]
    [InlineData("change-me-internal-token")]
    [InlineData("aveline-local-development-secret-token-2026")]
    [InlineData("AVELINE-LOCAL-DEVELOPMENT-SECRET-TOKEN-2026")]
    [InlineData("local-development-secret-token")]
    [InlineData("  change-me-internal-token  ")]
    [InlineData("my-local-development-token")]
    [InlineData("placeholder-value-for-internal-token")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void RejectKnownPlaceholder_FlagsEveryEnvironment(string? token)
    {
        // Refused in any environment: these are values the repository publishes.
        InsecureInternalTokens.RejectKnownPlaceholder(token).Should().NotBeNull();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("test-internal-analyze-key")]
    public void RejectKnownPlaceholder_AllowsAShortThrowawayToken(string token)
    {
        // Strength is a Production concern, not a per-request one. Test hosts legitimately set a
        // short token; refusing it at authentication time would break the suite without improving
        // production security, which the startup guard already enforces.
        InsecureInternalTokens.RejectKnownPlaceholder(token).Should().BeNull();
    }

    [Theory]
    [InlineData("short")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void Reject_StillFlagsWeakTokensForProduction(string token)
    {
        InsecureInternalTokens.Reject(token).Should().NotBeNull();
        InsecureInternalTokens.IsInsecure(token).Should().BeTrue();
    }

    [Fact]
    public void RejectKnownPlaceholder_FlagsTheComposeDefaultDespiteItsLength()
    {
        // The compose default is 43 characters - long enough to pass a naive length check, which is
        // exactly why the placeholder list matters more than the length rule. It matches the exact
        // placeholder entry, which is the more precise message; a variant matches the fragment.
        InsecureInternalTokens.RejectKnownPlaceholder("aveline-local-development-secret-token-2026")
            .Should().NotBeNull();

        InsecureInternalTokens.RejectKnownPlaceholder("some-other-local-development-token")
            .Should().Contain("development marker");

        InsecureInternalTokens.Reject("aveline-local-development-secret-token-2026")
            .Should().NotBeNull();
    }

    [Fact]
    public void Reject_AcceptsAHighEntropySecret()
    {
        InsecureInternalTokens.Reject(StrongToken).Should().BeNull();
    }

    [Fact]
    public void Reject_AcceptsALongTokenContainingTheWordTestButNotAsAPlaceholder()
    {
        // "test" is an exact-match placeholder, not a substring, so a real random token that
        // happens to contain it must still be accepted.
        var token = Convert.ToBase64String(
            System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)).Replace("a", "test");
        InsecureInternalTokens.Reject(token).Should().BeNull();
    }

    [Fact]
    public void ProductionGuard_RefusesTheComposeDefault()
    {
        var act = () => InternalTokenSecurityGuard.EnsureInternalTokenForProduction(
            HostEnvironment(Environments.Production),
            Configuration(("AgentService:InternalToken", "aveline-local-development-secret-token-2026")));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*insecure*");
    }

    [Fact]
    public void ProductionGuard_RefusesAMissingToken()
    {
        var act = () => InternalTokenSecurityGuard.EnsureInternalTokenForProduction(
            HostEnvironment(Environments.Production),
            Configuration());

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void ProductionGuard_AllowsAHighEntropyToken()
    {
        var act = () => InternalTokenSecurityGuard.EnsureInternalTokenForProduction(
            HostEnvironment(Environments.Production),
            Configuration(("AgentService:InternalToken", StrongToken)));

        act.Should().NotThrow();
    }

    [Fact]
    public void NonProductionHost_WarnsButDoesNotRefuse()
    {
        // Local development and the test suites must keep working without a secret.
        var act = () => InternalTokenSecurityGuard.EnsureInternalTokenForProduction(
            HostEnvironment(Environments.Development),
            Configuration(("AgentService:InternalToken", "change-me-internal-token")));

        act.Should().NotThrow();
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();

    private static IHostEnvironment HostEnvironment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }
}
