using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies <see cref="CorsConfiguration.AddAvelineCors"/> registers the named CORS
/// policy with exactly the configured allow-list of origins.
/// </summary>
public class CorsConfigurationTests
{
    [Fact]
    public async Task Registers_Policy_With_Configured_Origins()
    {
        var config = TestConfig(new Dictionary<string, string?>
        {
            ["Cors:AllowedOrigins:0"] = "http://localhost:5173",
            ["Cors:AllowedOrigins:1"] = "https://app.aveline.dev",
        });

        var provider = new ServiceCollection()
            .AddAvelineCors(config)
            .BuildServiceProvider();

        var policyProvider = provider.GetRequiredService<ICorsPolicyProvider>();
        var policy = await policyProvider.GetPolicyAsync(new DefaultHttpContext(), CorsConfiguration.DefaultPolicy);

        Assert.NotNull(policy);
        Assert.Equal(
            new[] { "http://localhost:5173", "https://app.aveline.dev" },
            policy!.Origins);
    }

    [Fact]
    public void Throws_When_Origins_Not_Configured()
    {
        var config = TestConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddAvelineCors(config));
    }

    [Fact]
    public void Throws_When_Origins_Empty()
    {
        var config = TestConfig(new Dictionary<string, string?> { ["Cors:AllowedOrigins:0"] = "" });
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddAvelineCors(config));
    }

    private static IConfiguration TestConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
