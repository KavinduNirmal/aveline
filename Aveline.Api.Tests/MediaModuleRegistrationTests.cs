using System.Net;
using Aveline.Api.Modules.Media;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aveline.Api.Tests;

/// <summary>
/// S0 / U0.7 — <see cref="MediaModule"/> is the one place <c>IMediaStorage</c> is registered
/// (strategy §3.1, §5.1 S0). The provider is selected once, in DI, from <c>Media:Provider</c>;
/// no caller branches on it.
/// </summary>
/// <remarks>
/// The Cloudinary implementation does not exist yet (unit U1.1), so this unit registers only what
/// exists: the <c>database</c> branch. A half-configured <c>cloudinary</c> provider is already
/// refused by <c>MediaOptionsValidator</c> at startup, and this unit does not weaken that.
/// </remarks>
public class MediaModuleRegistrationTests
{
    [Fact]
    public void DatabaseProvider_ResolvesTheDatabaseStorageConcretely()
    {
        var provider = BuildProvider(("Media:Provider", "database"));

        using var scope = provider.CreateScope();

        // The concrete type, not merely the interface: a mis-registration that resolved a fake or
        // a Cloudinary adapter must fail here.
        scope.ServiceProvider.GetRequiredService<IMediaStorage>()
            .Should().BeOfType<DatabaseMediaStorage>();
    }

    [Fact]
    public void AbsentProviderKey_FallsBackToTheDocumentedSafeDefault()
    {
        // The app must boot with no media configuration at all; `database` is that default.
        var provider = BuildProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IMediaStorage>()
            .Should().BeOfType<DatabaseMediaStorage>();
    }

    [Fact]
    public void StorageRegistration_IsScoped()
    {
        var provider = BuildProvider(("Media:Provider", "database"));

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var one = first.ServiceProvider.GetRequiredService<IMediaStorage>();
        var oneAgain = first.ServiceProvider.GetRequiredService<IMediaStorage>();
        var other = second.ServiceProvider.GetRequiredService<IMediaStorage>();

        one.Should().BeSameAs(oneAgain);
        one.Should().NotBeSameAs(other);
    }

    [Fact]
    public void CloudinaryProvider_DoesNotSilentlyResolveTheDatabaseStorage()
    {
        // CloudinaryMediaStorage is U1.1's. Until it exists, the seam must not quietly store
        // bytes in the database under a cloudinary configuration; a caller that resolves it gets
        // a refusal, not the wrong provider.
        var provider = BuildProvider(("Media:Provider", "cloudinary"));

        using var scope = provider.CreateScope();

        var act = () => scope.ServiceProvider.GetRequiredService<IMediaStorage>();

        act.Should().Throw<InvalidOperationException>().WithMessage("*U1.1*");
    }

    [Fact]
    public async Task TheDefaultHost_StartsAndResolvesTheDatabaseStorage()
    {
        // Development is WebApplicationFactory<Program>'s default environment and no Media:* key
        // is set, so this is the strategy's "safe default boots anywhere" promise, end to end.
        using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("AgentService:BaseUrl", "http://127.0.0.1:59999");
                builder.UseSetting("AgentService:InternalToken", "test-internal-token");
                builder.UseSetting("Observability:AgentIsCritical", "false");
            });

        using var client = factory.CreateClient();

        var response = await client.GetAsync("/health/live");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var scope = factory.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<IMediaStorage>()
            .Should().BeOfType<DatabaseMediaStorage>();
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();

        var services = new ServiceCollection();

        // The module under test, with no host around it: the registration is the behaviour.
        services.AddMediaModule(configuration);

        return services.BuildServiceProvider();
    }
}
