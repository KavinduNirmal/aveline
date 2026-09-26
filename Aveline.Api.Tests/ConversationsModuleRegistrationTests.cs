using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations;
using Aveline.Api.Modules.Conversations.Attachments;
using Aveline.Api.Modules.Media;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.3 — <see cref="ConversationsModule"/> registers the <em>row</em> seam exactly once, selected
/// by <c>Media:Provider</c> (strategy §3.1, migration plan §6.2). The old unconditional
/// <c>AddScoped&lt;IAttachmentStore, DatabaseAttachmentStore&gt;()</c> line must be removed, not
/// supplemented: <c>AddScoped</c> resolves the last registration, so a second line would silently
/// win and the Cloudinary configuration would keep storing bytes in the database.
/// </summary>
public class ConversationsModuleRegistrationTests
{
    [Fact]
    public void DatabaseProvider_ResolvesTheDatabaseAdapter()
    {
        var provider = BuildProvider(("Media:Provider", "database"));

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAttachmentStore>()
            .Should().BeOfType<DatabaseAttachmentStore>();
    }

    [Fact]
    public void AbsentProviderKey_FallsBackToTheDocumentedSafeDefault()
    {
        var provider = BuildProvider();

        using var scope = provider.CreateScope();

        scope.ServiceProvider.GetRequiredService<IAttachmentStore>()
            .Should().BeOfType<DatabaseAttachmentStore>();
    }

    [Fact]
    public void CloudinaryProvider_ResolvesTheCloudinaryAdapter_NotTheDatabaseAdapter()
    {
        // If the removed line had only been supplemented rather than deleted, the last
        // registration would be the database adapter and this assertion would fail.
        var provider = BuildProvider(
            ("Media:Provider", "cloudinary"),
            ("CLOUDINARY_URL", "cloudinary://a-module-test-key:a-module-test-secret@a-cloud"));

        using var scope = provider.CreateScope();

        var store = scope.ServiceProvider.GetRequiredService<IAttachmentStore>();

        store.Should().BeOfType<CloudinaryAttachmentStore>();
        store.Should().NotBeOfType<DatabaseAttachmentStore>();
        store.Provider.Should().Be("cloudinary");
    }

    [Fact]
    public void TheRowSeam_IsScoped()
    {
        var provider = BuildProvider(("Media:Provider", "database"));

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        var one = first.ServiceProvider.GetRequiredService<IAttachmentStore>();
        var oneAgain = first.ServiceProvider.GetRequiredService<IAttachmentStore>();
        var other = second.ServiceProvider.GetRequiredService<IAttachmentStore>();

        one.Should().BeSameAs(oneAgain);
        one.Should().NotBeSameAs(other);
    }

    private static ServiceProvider BuildProvider(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(setting => setting.Key, setting => (string?)setting.Value))
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options =>
            options.UseInMemoryDatabase($"ConversationsModuleRegistration_{Guid.NewGuid()}"));
        services.AddMediaOptions(configuration);
        services.AddMediaModule(configuration);
        services.AddConversationsModule(configuration);

        return services.BuildServiceProvider();
    }
}
