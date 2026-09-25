using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Testcontainers.PostgreSql;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.3, verified the only way the write path can be: against real PostgreSQL with the real
/// migrations. The in-memory provider accepts almost any value, so it cannot prove that a revocation
/// persists <c>ConsentSource</c>/<c>GlobalSubjectId</c>, that <c>EvidenceJson</c> lands in a
/// <c>jsonb</c> column, or that the bounded varchars (<c>ActorKind</c> 16, <c>Source</c> 16) accept
/// the constants this phase writes. Follows <c>ConsentFoundationPostgresTests</c>.
/// </summary>
public class ConsentRevocationPostgresTests : IAsyncLifetime
{
    private const string Phone = "+94779000123";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private DbContextOptions<AppDbContext> _options = null!;
    private Guid _orgA;
    private Guid _orgB;
    private Guid _customerA;
    private Guid _customerB;

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
        await SeedAsync(context);
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task SeedAsync(AppDbContext context)
    {
        _orgA = await AddOrganizationAsync(context, "revocation-a");
        _orgB = await AddOrganizationAsync(context, "revocation-b");

        // The same human at two boutiques, which is the case identity-by-phone exists for.
        _customerA = await AddCustomerAsync(context, _orgA, "Revocation Client A");
        _customerB = await AddCustomerAsync(context, _orgB, "Revocation Client B");
    }

    private static async Task<Guid> AddOrganizationAsync(AppDbContext context, string suffix)
    {
        var ownerId = Guid.CreateVersion7();
        context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"{suffix}_{ownerId:N}",
            Email = $"{suffix}@aveline.lk",
            FirstName = "Revocation",
            LastName = "Owner",
            Username = $"{suffix}_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = $"Revocation Boutique {suffix}",
            Slug = $"{suffix}-{ownerId:N}",
            OwnerUserId = ownerId,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private static async Task<Guid> AddCustomerAsync(AppDbContext context, Guid orgId, string name)
    {
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = Phone,
            FullName = name,
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            ConsentStatus = ConsentStatuses.Pending,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private ConsentRevoker CreateRevoker(AppDbContext context)
        => new(
            new CustomerRepository(context),
            new CustomerConsentRepository(context),
            new PhoneSubjectLocator(context),
            new AuditService(
                new Aveline.Api.Modules.Audit.Repositories.AuditRepository(context),
                new AuditRedactor(),
                new Microsoft.AspNetCore.Http.HttpContextAccessor(),
                NullLogger<AuditService>.Instance),
            context,
            TimeProvider.System,
            NullLogger<ConsentRevoker>.Instance);

    [Fact]
    public async Task AGlobalRevocationPersistsThePrivacyColumnsAndBothAuditRows()
    {
        await using var context = new AppDbContext(_options);
        var revoker = CreateRevoker(context);

        var result = await revoker.RevokeAsync(new ConsentRevocationRequest(
            _orgA,
            CustomerId: null,
            PhoneE164: Phone,
            Scope: ConsentRevocationScope.All,
            Actor: new ConsentActor(
                ConsentActorKinds.Customer,
                ConsentSources.OtpLink,
                ActorRef: PhoneFingerprint.Of(Phone)),
            Reason: "customer_otp_opt_out"));

        Assert.Equal(2, result.RowsRevoked);
        Assert.Equal(2, result.OrganizationsAffected.Count);

        await using var read = new AppDbContext(_options);
        var rowA = await read.CustomerConsents.SingleAsync(c => c.CustomerId == _customerA);
        var rowB = await read.CustomerConsents.SingleAsync(c => c.CustomerId == _customerB);

        Assert.Equal(ConsentStatuses.Revoked, rowA.ConsentStatus);
        Assert.Equal(ConsentStatuses.Revoked, rowB.ConsentStatus);
        Assert.Equal(ConsentSources.OtpLink, rowA.ConsentSource);
        Assert.NotNull(rowA.ConsentRevokedAt);
        // The global opt-out annotates the cross-org subject from the phone fingerprint.
        Assert.NotNull(rowA.GlobalSubjectId);
        Assert.Equal(rowA.GlobalSubjectId, rowB.GlobalSubjectId);

        // Both audit tables, and the bounded varchars accepted the constants.
        var consentAudit = await read.ConsentAuditEntries
            .Where(a => a.CustomerId == _customerA)
            .ToListAsync();
        var entry = Assert.Single(consentAudit);
        Assert.Equal(AuditAction.ConsentRevoked, entry.Action);
        Assert.Equal(ConsentActorKinds.Customer, entry.ActorKind);
        Assert.Equal(ConsentSources.OtpLink, entry.Source);
        Assert.Equal(ConsentStatuses.Pending, entry.PreviousStatus);
        Assert.Equal(ConsentStatuses.Revoked, entry.NewStatus);
        Assert.DoesNotContain(Phone, entry.EvidenceJson, StringComparison.Ordinal);

        // The generic audit trail carries the same revocation, attributed to the customer.
        var log = await read.AuditLogEntries.SingleAsync(
            a => a.OrganizationId == _orgA && a.Action == AuditAction.ConsentRevoked);
        Assert.Equal(ConsentActorKinds.Customer, log.ActorKind);
        Assert.Equal(PhoneFingerprint.Of(Phone), log.ActorRef);
        Assert.DoesNotContain(Phone, log.ActorRef, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOrgScopedRevocationTouchesOnlyThatOrganization()
    {
        await using var context = new AppDbContext(_options);
        var revoker = CreateRevoker(context);

        var result = await revoker.RevokeAsync(new ConsentRevocationRequest(
            _orgA,
            CustomerId: _customerA,
            PhoneE164: Phone,
            Scope: ConsentRevocationScope.Org,
            Actor: new ConsentActor(ConsentActorKinds.User, ConsentSources.Staff)));

        Assert.Equal(1, result.RowsRevoked);

        await using var read = new AppDbContext(_options);
        Assert.Equal(
            ConsentStatuses.Revoked,
            (await read.CustomerConsents.SingleAsync(c => c.CustomerId == _customerA)).ConsentStatus);
        Assert.Equal(
            ConsentStatuses.Pending,
            (await read.CustomerConsents.SingleAsync(c => c.CustomerId == _customerB)).ConsentStatus);
        // An org-scoped action is not a global one, so no cross-org subject is inferred.
        Assert.Null((await read.CustomerConsents.SingleAsync(c => c.CustomerId == _customerA)).GlobalSubjectId);
    }
}
