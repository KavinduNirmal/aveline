using System.Net;
using Aveline.Api.Modules.Media;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

/// <summary>
/// S0 / U0.7 — <see cref="MediaModule"/> is the one place <c>IMediaStorage</c> is registered
/// (strategy §3.1, §5.1 S0). The provider is selected once, in DI, from <c>Media:Provider</c>;
/// no caller branches on it.
/// </summary>
/// <remarks>
/// U1.1 replaced the Wave 0 placeholder that refused a <c>cloudinary</c> provider with the real
/// <c>CloudinaryMediaStorage</c> registration, so this file's cloudinary expectation moved with
/// that unit. The invariant it guards is unchanged: a <c>cloudinary</c> configuration must never
/// resolve the database adapter.
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
    public void CloudinaryProvider_ResolvesTheCloudinaryStorage_NotTheDatabaseStorage()
    {
        // U1.1's real registration. The invariant is unchanged: a cloudinary configuration must
        // never quietly store bytes in the database.
        var provider = BuildProvider(
            ("Media:Provider", "cloudinary"),
            ("CLOUDINARY_URL", "cloudinary://a-module-test-key:a-module-test-secret@a-cloud"));

        using var scope = provider.CreateScope();

        var storage = scope.ServiceProvider.GetRequiredService<IMediaStorage>();

        storage.Should().BeOfType<CloudinaryMediaStorage>();
        storage.Should().NotBeOfType<DatabaseMediaStorage>();
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
        // Logging and the options registration mirror the two calls the host makes
        // (`AddMediaOptions` then `AddMediaModule`, Program.cs).
        services.AddLogging();
        services.AddMediaOptions(configuration);
        services.AddMediaModule(configuration);

        return services.BuildServiceProvider();
    }
}
