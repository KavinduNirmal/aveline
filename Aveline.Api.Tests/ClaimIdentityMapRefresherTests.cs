using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 1 of the Business KPIs plan (§5.5): the claim-map refresher rebuilds both
/// dictionaries in one swap, keeps the previous contents when a rebuild fails, and resolves a
/// soft-deleted user (the refresh deliberately ignores the soft-delete query filter, because a
/// soft-deleted user is still a real identity for requests already in flight).
/// </summary>
public class ClaimIdentityMapRefresherTests
{
    private sealed record Harness(
        ClaimIdentityMapRefresher Refresher,
        ClaimIdentityMap Map,
        string DatabaseName,
        ServiceProvider Provider);

    private static Harness Build(string? databaseName = null)
    {
        databaseName ??= $"ClaimMap_{Guid.NewGuid()}";

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        var provider = services.BuildServiceProvider();

        var map = new ClaimIdentityMap();
        var refresher = new ClaimIdentityMapRefresher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new InMemoryDistributedJobLock(),
            map,
            NullLogger<ClaimIdentityMapRefresher>.Instance);

        return new Harness(refresher, map, databaseName, provider);
    }

    private static AppDbContext Context(string databaseName) => new(
        new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(databaseName).Options);

    private static User AUser(string clerkId, Guid? id = null, DateTime? deletedAt = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        ClerkId = clerkId,
        FirstName = "Ada",
        LastName = "Lovelace",
        Email = $"{clerkId}@example.test",
        Username = clerkId,
        OrganizationId = string.Empty,
        UserRole = "owner",
        OrganizationRole = "boutique_owner",
        DeletedAt = deletedAt,
    };

    private static Organization AnOrganization(string clerkOrgId, Guid? id = null) => new()
    {
        Id = id ?? Guid.CreateVersion7(),
        Name = "Atelier",
        Slug = $"atelier-{Guid.NewGuid():N}",
        ClerkOrgId = clerkOrgId,
        OwnerUserId = Guid.CreateVersion7(),
    };

    [Fact]
    public async Task RebuildReplacesBothDictionaries()
    {
        var harness = Build();
        var user = AUser("user_first");
        var organization = AnOrganization("org_first");

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.Add(user);
            seed.Organizations.Add(organization);
            await seed.SaveChangesAsync();
        }

        var processed = await harness.Refresher.RunAsync(CancellationToken.None);

        Assert.Equal(2, processed);
        Assert.Equal(user.Id, harness.Map.ResolveUserId("user_first"));
        Assert.Equal(organization.Id, harness.Map.ResolveOrganizationId("org_first"));
        Assert.Equal(0, harness.Map.UnresolvedCount);
    }

    [Fact]
    public async Task ASecondRebuildReplacesTheFirst()
    {
        var harness = Build();
        var first = AUser("user_one");
        var second = AUser("user_two");

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.Add(first);
            await seed.SaveChangesAsync();
        }

        await harness.Refresher.RunAsync(CancellationToken.None);
        Assert.Equal(first.Id, harness.Map.ResolveUserId("user_one"));

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.Add(second);
            await seed.SaveChangesAsync();
        }

        await harness.Refresher.RunAsync(CancellationToken.None);

        Assert.Equal(first.Id, harness.Map.ResolveUserId("user_one"));
        Assert.Equal(second.Id, harness.Map.ResolveUserId("user_two"));
    }

    [Fact]
    public async Task ASoftDeletedUserStillResolves()
    {
        var harness = Build();
        var deleted = AUser("user_soft_deleted", deletedAt: DateTime.UtcNow.AddDays(-1));

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.Add(deleted);
            await seed.SaveChangesAsync();
        }

        await harness.Refresher.RunAsync(CancellationToken.None);

        Assert.Equal(deleted.Id, harness.Map.ResolveUserId("user_soft_deleted"));
    }

    [Fact]
    public async Task AFailedRebuildLeavesThePreviousContentsIntact()
    {
        var harness = Build();
        var user = AUser("user_kept");

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        await harness.Refresher.RunAsync(CancellationToken.None);
        Assert.Equal(user.Id, harness.Map.ResolveUserId("user_kept"));

        // Fail the next read by disposing the provider the refresher resolves its scope from.
        await harness.Provider.DisposeAsync();

        var processed = await harness.Refresher.RunAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        // The map did not become empty: a stale answer beats no answer for attribution.
        Assert.Equal(user.Id, harness.Map.ResolveUserId("user_kept"));
    }

    [Fact]
    public async Task AnEmptyDatabaseProducesAnEmptyMapRatherThanAThrow()
    {
        var harness = Build();

        var processed = await harness.Refresher.RunAsync(CancellationToken.None);

        Assert.Equal(0, processed);
        Assert.Null(harness.Map.ResolveUserId("user_absent"));
        Assert.Null(harness.Map.ResolveOrganizationId("org_absent"));
    }

    [Fact]
    public async Task RowsWithoutAClerkIdAreSkipped()
    {
        var harness = Build();
        var withId = AUser("user_present");
        var withoutId = AUser(clerkId: string.Empty);
        var organizationWithoutId = AnOrganization(clerkOrgId: "org_present");
        organizationWithoutId.ClerkOrgId = null;

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.AddRange(withId, withoutId);
            seed.Organizations.Add(organizationWithoutId);
            await seed.SaveChangesAsync();
        }

        var processed = await harness.Refresher.RunAsync(CancellationToken.None);

        Assert.Equal(1, processed);
        Assert.Equal(withId.Id, harness.Map.ResolveUserId("user_present"));
        Assert.Null(harness.Map.ResolveOrganizationId("org_present"));
    }

    [Fact]
    public async Task TheDistributedLockSerialisesTwoRefreshes()
    {
        // The refresher is a StatisticsJobBase, so two instances cannot rebuild the map
        // concurrently across a cluster (the lock name is asserted by the job's own registration).
        var harness = Build();
        var user = AUser("user_locked");

        await using (var seed = Context(harness.DatabaseName))
        {
            seed.Users.Add(user);
            await seed.SaveChangesAsync();
        }

        var first = harness.Refresher.RunAsync(CancellationToken.None);
        var second = harness.Refresher.RunAsync(CancellationToken.None);

        await Task.WhenAll(first, second);

        Assert.Equal(user.Id, harness.Map.ResolveUserId("user_locked"));
    }
}
