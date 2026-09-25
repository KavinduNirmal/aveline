using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 3.3's concurrency control, verified on the provider that will run it. The in-memory
/// provider used by the service tests does not support <c>ExecuteUpdate</c>, so it exercises the
/// documented read-check-write fallback; this test proves the real PostgreSQL
/// <c>UPDATE ... WHERE "DisclosureShownAt" IS NULL</c> claim behaves as the exactly-once gate.
/// </summary>
public class DisclosureClaimPostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();
        _options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(
                _postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        await using var context = new AppDbContext(_options);
        await context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task<(Guid OrgId, Guid CustomerId)> SeedAsync()
    {
        await using var context = new AppDbContext(_options);
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"claim_pg_{ownerId:N}",
            Email = "claim-pg@aveline.lk",
            FirstName = "Claim",
            LastName = "Owner",
            Username = $"claim_pg_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Claim Pg Boutique",
            Slug = $"claim-pg-{ownerId:N}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = "+94779000222",
            FullName = "Claim Pg Client",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConsentStatus = ConsentStatuses.Pending,
        });
        await context.SaveChangesAsync();

        return (org.Id, customer.Id);
    }

    [Fact]
    public async Task TheConditionalClaimAdmitsExactlyOneCallerAndTheReleaseReArmsIt()
    {
        var (orgId, customerId) = await SeedAsync();
        var claimedAt = DateTime.UtcNow;

        await using (var context = new AppDbContext(_options))
        {
            var repository = new CustomerConsentRepository(context);

            var first = await repository.TryClaimDisclosureAsync(orgId, customerId, "v1", claimedAt);
            Assert.True(first);

            var second = await repository.TryClaimDisclosureAsync(orgId, customerId, "v1", claimedAt);
            Assert.False(second);

            await repository.ReleaseDisclosureAsync(orgId, customerId);

            // Releasing the claim is what makes the next inbound message retry.
            var third = await repository.TryClaimDisclosureAsync(orgId, customerId, "v1", claimedAt);
            Assert.True(third);

            // A different org's row is untouched: the claim is tenant-scoped.
            var (otherOrgId, otherCustomerId) = await SeedAsync();
            var otherRepository = new CustomerConsentRepository(context);
            var other = await otherRepository.TryClaimDisclosureAsync(otherOrgId, otherCustomerId, "v1", claimedAt);
            Assert.True(other);
        }

        await using var verify = new AppDbContext(_options);
        var row = await verify.CustomerConsents.AsNoTracking().SingleAsync(c => c.CustomerId == customerId);
        Assert.NotNull(row.DisclosureShownAt);
        Assert.Equal("v1", row.DisclosureVersion);
    }

    [Fact]
    public async Task ReleasingADisclosureClearsBothTheTimestampAndTheVersion()
    {
        var (orgId, customerId) = await SeedAsync();

        await using var context = new AppDbContext(_options);
        var repository = new CustomerConsentRepository(context);
        await repository.TryClaimDisclosureAsync(orgId, customerId, "v1", DateTime.UtcNow);

        await repository.ReleaseDisclosureAsync(orgId, customerId);
        context.ChangeTracker.Clear();

        var row = await context.CustomerConsents.AsNoTracking().SingleAsync(c => c.CustomerId == customerId);
        Assert.Null(row.DisclosureShownAt);
        Assert.Null(row.DisclosureVersion);
    }
}
