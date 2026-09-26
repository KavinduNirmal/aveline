using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Tests;

/// <summary>
/// D5 (plan §6.7) and guardrail 2 of plan §7.4: the factory resolves the adapter registered under
/// the key stored on an intent, so flipping <c>Payments:Provider</c> cannot orphan an open intent;
/// and it refuses to resolve <c>"mock"</c> when the mock is not enabled, so a config mistake cannot
/// activate a fake gateway even though the keyed registration exists.
/// </summary>
public class PaymentProviderFactoryTests
{
    private static ServiceProvider Build(IDictionary<string, string?>? values = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(values ?? new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentsModule(configuration, new StubHostEnvironment(Environments.Production));
        return services.BuildServiceProvider();
    }

    [Fact]
    public void Active_ReturnsTheManualAdapterByDefault()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        factory.Active.Key.Should().Be("manual");
    }

    [Fact]
    public void Resolve_ReturnsTheAdapterRegisteredUnderTheStoredKey()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        factory.Resolve("manual").Key.Should().Be("manual");
    }

    [Fact]
    public void Resolve_Mock_WhenDisabled_ThrowsNotConfigured()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        var resolve = () => factory.Resolve("mock");

        resolve.Should().Throw<PaymentProviderNotConfiguredException>();
    }

    [Fact]
    public void Resolve_UnknownKey_ThrowsNotConfigured()
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        var resolve = () => factory.Resolve("payhere");

        resolve.Should().Throw<PaymentProviderNotConfiguredException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_BlankKey_ThrowsNotConfigured(string key)
    {
        using var provider = Build();
        using var scope = provider.CreateScope();

        var factory = scope.ServiceProvider.GetRequiredService<IPaymentProviderFactory>();

        var resolve = () => factory.Resolve(key);

        resolve.Should().Throw<PaymentProviderNotConfiguredException>();
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Aveline.Api.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
