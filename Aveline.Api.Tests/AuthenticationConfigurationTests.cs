using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies <see cref="AuthenticationConfiguration.AddAvelineAuthentication"/> wires
/// the JwtBearer scheme and fails fast when the Clerk authority is missing.
/// </summary>
public class AuthenticationConfigurationTests
{
    [Fact]
    public void Registers_JwtBearer_Scheme()
    {
        var config = TestConfig(new Dictionary<string, string?>
        {
            ["Clerk:Authority"] = "https://test.clerk.accounts.dev",
        });

        var provider = new ServiceCollection()
            .AddAvelineAuthentication(config)
            .BuildServiceProvider();

        var schemes = provider.GetRequiredService<IAuthenticationSchemeProvider>();
        var scheme = schemes.GetSchemeAsync(JwtBearerDefaults.AuthenticationScheme).GetAwaiter().GetResult();

        Assert.NotNull(scheme);
        Assert.Equal(typeof(JwtBearerHandler), scheme!.HandlerType);
    }

    [Fact]
    public void Throws_When_Authority_Not_Configured()
    {
        var config = TestConfig(new Dictionary<string, string?>());
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddAvelineAuthentication(config));
    }

    private static IConfiguration TestConfig(IDictionary<string, string?> values) =>
        new ConfigurationBuilder().AddInMemoryCollection(values).Build();
}
