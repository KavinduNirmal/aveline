using Aveline.Api.Modules.Payments;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Aveline.Api.Tests;

/// <summary>
/// Guardrail 1 and 2 of plan §7.4: a first request must never be the thing that discovers a
/// mis-configured payment provider. <c>Payments:*</c> has no <c>appsettings</c> section in this
/// repository, so every default has to be safe on its own and the mock guard must key off
/// <see cref="IHostEnvironment"/> rather than a config value someone has to remember to set.
/// </summary>
public class PaymentsOptionsTests
{
    private static IHostEnvironment Environment(string name) => new StubHostEnvironment(name);

    private static PaymentsOptions Options(Action<PaymentsOptions>? configure = null)
    {
        var options = new PaymentsOptions();
        configure?.Invoke(options);
        return options;
    }

    [Fact]
    public void Defaults_AreSafeForAnUnconfiguredDeployment()
    {
        var options = new PaymentsOptions();

        options.Provider.Should().Be("manual");
        options.Currency.Should().Be("LKR");
        options.Mock.Enabled.Should().BeFalse();
        options.Mock.AllowInNonDevelopment.Should().BeFalse();

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().NotThrow();
    }

    /// <summary>
    /// Phase 9's one-release rollback switch (plan §8.4 S8) must default to the provider-backed path,
    /// and must actually read <c>Payments:Commerce:UseProviderIntents</c> from configuration. A
    /// default of <c>false</c> would silently restore the fabricated checkout URL for every
    /// deployment that configures nothing, which is the opposite of a safe default.
    /// </summary>
    [Fact]
    public void CommerceRollbackSwitch_DefaultsOn_AndBindsFromConfiguration()
    {
        new PaymentsOptions().Commerce.UseProviderIntents.Should().BeTrue();

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Commerce:UseProviderIntents"] = "false",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddOptions<PaymentsOptions>().BindConfiguration(PaymentsOptions.SectionName);
        services.AddSingleton<IConfiguration>(configuration);

        using var provider = services.BuildServiceProvider();
        var bound = provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PaymentsOptions>>().Value;

        bound.Commerce.UseProviderIntents.Should().BeFalse();
    }

    [Fact]
    public void Validate_MockOutsideDevelopment_Throws()
    {
        var options = Options(o =>
        {
            o.Provider = "mock";
            o.Mock.Enabled = true;
            o.Mock.WebhookSigningSecret = "test-secret";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Payments:Provider*mock*Development*");
    }

    [Fact]
    public void Validate_MockOutsideDevelopment_IsAllowedWhenExplicitlyPermitted()
    {
        var options = Options(o =>
        {
            o.Provider = "mock";
            o.Mock.Enabled = true;
            o.Mock.AllowInNonDevelopment = true;
            o.Mock.WebhookSigningSecret = "test-secret";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().NotThrow();
    }

    [Fact]
    public void Validate_MockInDevelopment_StillRequiresEnabled()
    {
        var options = Options(o =>
        {
            o.Provider = "mock";
            o.Mock.WebhookSigningSecret = "test-secret";
        });

        var validate = () => options.Validate(Environment(Environments.Development));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Mock:Enabled*");
    }

    [Fact]
    public void Validate_Mock_RequiresTheWebhookSigningSecret()
    {
        var options = Options(o =>
        {
            o.Provider = "mock";
            o.Mock.Enabled = true;
        });

        var validate = () => options.Validate(Environment(Environments.Development));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*WebhookSigningSecret*");
    }

    [Fact]
    public void Validate_StripeWithoutSecretKey_Throws()
    {
        var options = Options(o =>
        {
            o.Provider = "stripe";
            o.Stripe.WebhookSigningSecret = "whsec_test";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Stripe:SecretKey*");
    }

    [Fact]
    public void Validate_StripeWithoutWebhookSigningSecret_Throws()
    {
        var options = Options(o =>
        {
            o.Provider = "stripe";
            o.Stripe.SecretKey = "sk_test";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Stripe:WebhookSigningSecret*");
    }

    [Fact]
    public void Validate_StripeWithBothKeys_DoesNotThrow()
    {
        var options = Options(o =>
        {
            o.Provider = "stripe";
            o.Stripe.SecretKey = "sk_test";
            o.Stripe.WebhookSigningSecret = "whsec_test";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().NotThrow();
    }

    /// <summary>
    /// The guard has to run in <c>AddPaymentsModule</c>, not on the first request, mirroring
    /// <c>AnalyticsModule</c>'s fail-at-startup precedent.
    /// </summary>
    [Fact]
    public void AddPaymentsModule_FailsStartup_ForAMockOutsideDevelopment()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Provider"] = "mock",
                ["Payments:Mock:Enabled"] = "true",
                ["Payments:Mock:WebhookSigningSecret"] = "test-secret",
            })
            .Build();

        var register = () => new ServiceCollection()
            .AddPaymentsModule(configuration, Environment(Environments.Production));

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*mock*Development*");
    }

    [Fact]
    public void AddPaymentsModule_WithNoConfiguration_UsesTheManualProvider()
    {
        var configuration = new ConfigurationBuilder().Build();
        var services = new ServiceCollection();
        services.AddLogging();

        var register = () => services.AddPaymentsModule(
            configuration, Environment(Environments.Production));

        register.Should().NotThrow();
    }

    /// <summary>
    /// Plan Phase 8: the OnePay adapter is selected by <c>Payments:Provider = "onepay"</c>, and a
    /// deployment that sets it without the provider's credentials must fail startup rather than the
    /// first checkout. The two keys below are the whole required set.
    /// </summary>
    [Fact]
    public void Validate_OnePayWithoutAppId_Throws()
    {
        var options = Options(o =>
        {
            o.Provider = "onepay";
            o.OnePay.HashSalt = "salt";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Payments:OnePay:AppId*");
    }

    [Fact]
    public void Validate_OnePayWithoutHashSalt_Throws()
    {
        var options = Options(o =>
        {
            o.Provider = "onepay";
            o.OnePay.AppId = "80NR1189D04CD635D8ACD";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().Throw<InvalidOperationException>()
            .WithMessage("*Payments:OnePay:HashSalt*");
    }

    [Fact]
    public void Validate_OnePayWithBothKeys_DoesNotThrow()
    {
        var options = Options(o =>
        {
            o.Provider = "onepay";
            o.OnePay.AppId = "80NR1189D04CD635D8ACD";
            o.OnePay.HashSalt = "salt";
        });

        var validate = () => options.Validate(Environment(Environments.Production));

        validate.Should().NotThrow();
    }

    /// <summary>
    /// The same guard has to run in <c>AddPaymentsModule</c>, not on the first checkout, mirroring
    /// <c>AnalyticsModule</c>'s fail-at-startup precedent.
    /// </summary>
    [Fact]
    public void AddPaymentsModule_FailsStartup_ForOnePayWithoutItsCredentials()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Provider"] = "onepay",
            })
            .Build();

        var register = () => new ServiceCollection()
            .AddPaymentsModule(configuration, Environment(Environments.Production));

        register.Should().Throw<InvalidOperationException>()
            .WithMessage("*OnePay*");
    }

    [Fact]
    public void AddPaymentsModule_WithOnePayConfigured_RegistersTheAdapterWithoutThrowing()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Provider"] = "onepay",
                ["Payments:OnePay:AppId"] = "80NR1189D04CD635D8ACD",
                ["Payments:OnePay:HashSalt"] = "salt",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();

        var register = () => services.AddPaymentsModule(
            configuration, Environment(Environments.Production));

        register.Should().NotThrow();
    }

    /// <summary>
    /// The swap surface itself (plan §8.2): with <c>Payments:Provider = "onepay"</c> the factory must
    /// resolve the external adapter for new intents, and the adapter must be able to construct from
    /// the container (its named <see cref="System.Net.Http.IHttpClientFactory"/> included).
    /// </summary>
    [Fact]
    public void AddPaymentsModule_WithOnePayConfigured_ResolvesTheOnePayAdapter()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Payments:Provider"] = "onepay",
                ["Payments:OnePay:AppId"] = "80NR1189D04CD635D8ACD",
                ["Payments:OnePay:HashSalt"] = "salt",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPaymentsModule(configuration, Environment(Environments.Production));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var adapter = scope.ServiceProvider
            .GetRequiredService<Aveline.Api.Modules.Payments.Domain.IPaymentProviderFactory>()
            .Active;

        adapter.Key.Should().Be(Aveline.Api.Modules.Payments.PaymentsOptions.OnePayProviderKey);
        adapter.Capabilities.SupportsHostedCheckout.Should().BeTrue();
    }

    private sealed class StubHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Aveline.Api.Tests";

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
