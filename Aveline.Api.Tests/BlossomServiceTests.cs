using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #191 — the credit/debit/revoke operations and the balance projection
/// (BR-2.1..BR-2.7, BR-2.13).
/// </summary>
public class BlossomServiceTests
{
    private readonly AppDbContext _context;

    public BlossomServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Blossom_{Guid.NewGuid()}")
            .Options);
    }

    private BlossomService CreateService(InMemoryEventBus? bus = null)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Billing:MaxAdjustmentBlossoms"] = "10000",
                ["Billing:LowBalanceThresholdPercent"] = "20",
            })
            .Build();

        return new BlossomService(
            new BlossomLedgerRepository(_context),
            new UsageRepository(_context),
            new EntitlementResolver(new EntitlementRepository(_context)),
            bus ?? new InMemoryEventBus(),
            new AuditService(
                new AuditRepository(_context), new AuditRedactor(),
                new HttpContextAccessor(), NullLogger<AuditService>.Instance),
            config,
            NullLogger<BlossomService>.Instance);
    }

    private static CreditBlossomsCommand Credit(decimal amount, DateTime? expiresAt = null, string? key = null) =>
        new(Guid.Empty, amount, "A valid credit reason.", expiresAt, BlossomSourceKind.Admin, "SUP-1",
            Guid.CreateVersion7(), key, key is null ? null : "admin.blossoms.credit");

    private async Task<Guid> SeedOrganizationAsync()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"o_{ownerId:N}", Email = "o@aveline.lk", FirstName = "O", LastName = "W",
            Username = $"o_{ownerId:N}", UserRole = "owner", OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Blossom Boutique", Slug = $"bb-{ownerId:N}", OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    [Fact]
    public async Task Credit_IncreasesBalance_AndWritesLedgerEntry()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        var entry = await service.CreditAsync(Credit(250m) with { OrganizationId = orgId });

        Assert.Equal(BlossomLedgerEntryType.AdminCredit, entry.EntryType);
        Assert.Equal(250m, entry.BlossomDelta);
        Assert.Equal(400m, entry.BlossomBalanceAfter); // 150 Seed allowance + 250

        var balance = await service.GetBalanceAsync(orgId);
        Assert.Equal(250m, balance.BlossomGranted);
        Assert.Equal(400m, balance.BlossomRemaining);
    }

    [Fact]
    public async Task Debit_DecreasesBalance()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();
        await service.CreditAsync(Credit(250m) with { OrganizationId = orgId });

        var entry = await service.DebitAsync(new DebitBlossomsCommand(
            orgId, 100m, "A valid debit reason.", AllowNegative: false, Guid.CreateVersion7(), null, null));

        Assert.Equal(-100m, entry.BlossomDelta);
        Assert.Equal(300m, entry.BlossomBalanceAfter);

        var balance = await service.GetBalanceAsync(orgId);
        Assert.Equal(100m, balance.BlossomAdjusted);
        Assert.Equal(300m, balance.BlossomRemaining);
    }

    [Fact]
    public async Task Debit_BeyondAvailable_ThrowsWithAvailableAndRequested()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        var exception = await Assert.ThrowsAsync<InsufficientBalanceException>(() =>
            service.DebitAsync(new DebitBlossomsCommand(
                orgId, 500m, "A valid debit reason.", AllowNegative: false, Guid.CreateVersion7(), null, null)));

        Assert.Equal(150m, exception.Available);
        Assert.Equal(500m, exception.Requested);
    }

    [Fact]
    public async Task Debit_BeyondAvailable_WithAllowNegative_Succeeds()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        var entry = await service.DebitAsync(new DebitBlossomsCommand(
            orgId, 500m, "A valid debit reason.", AllowNegative: true, Guid.CreateVersion7(), null, null));

        Assert.Equal(-500m, entry.BlossomDelta);
        Assert.Equal(-350m, entry.BlossomBalanceAfter);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Credit_NonPositiveAmount_Throws(decimal amount)
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<BlossomValidationException>(() =>
            service.CreditAsync(Credit(amount) with { OrganizationId = orgId }));
    }

    [Fact]
    public async Task Credit_AboveCap_Throws()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<BlossomValidationException>(() =>
            service.CreditAsync(Credit(10_001m) with { OrganizationId = orgId }));
    }

    [Fact]
    public async Task Credit_ShortReason_Throws()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        await Assert.ThrowsAsync<BlossomValidationException>(() =>
            service.CreditAsync(Credit(100m) with { OrganizationId = orgId, Reason = "short" }));
    }

    [Fact]
    public async Task Credit_ClosedPeriod_Throws()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();
        await service.GetBalanceAsync(orgId);

        var account = await _context.UsageAccounts.SingleAsync(a => a.OrganizationId == orgId);
        account.IsClosed = true;
        account.ClosedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        await Assert.ThrowsAsync<PeriodClosedException>(() =>
            service.CreditAsync(Credit(100m) with { OrganizationId = orgId }));
    }

    [Fact]
    public async Task Revoke_ReversesTheGrant()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();
        var grant = await service.CreditAsync(Credit(250m) with { OrganizationId = orgId });

        var revocation = await service.RevokeAsync(new RevokeBlossomsCommand(
            orgId, grant.Id, "Grant reversed after review.", Guid.CreateVersion7(), null, null));

        Assert.Equal(BlossomLedgerEntryType.TopUpRevocation, revocation.EntryType);
        Assert.Equal(-250m, revocation.BlossomDelta);
        Assert.Equal(grant.Id, revocation.SupersedesEntryId);

        var balance = await service.GetBalanceAsync(orgId);
        Assert.Equal(150m, balance.BlossomRemaining);
    }

    [Fact]
    public async Task Revoke_AlreadyRevoked_Throws()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();
        var grant = await service.CreditAsync(Credit(250m) with { OrganizationId = orgId });
        await service.RevokeAsync(new RevokeBlossomsCommand(
            orgId, grant.Id, "Grant reversed after review.", Guid.CreateVersion7(), null, null));

        var exception = await Assert.ThrowsAsync<GrantNotRevocableException>(() =>
            service.RevokeAsync(new RevokeBlossomsCommand(
                orgId, grant.Id, "Second reversal attempt here.", Guid.CreateVersion7(), null, null)));

        Assert.Equal(0m, exception.AvailableToRevoke);
    }

    [Fact]
    public async Task Credit_WithSameIdempotencyKey_DoesNotDoubleCredit()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService();

        var first = await service.CreditAsync(Credit(250m, key: "credit-1") with { OrganizationId = orgId });
        var second = await service.CreditAsync(Credit(250m, key: "credit-1") with { OrganizationId = orgId });

        Assert.Equal(first.Id, second.Id);
        var balance = await service.GetBalanceAsync(orgId);
        Assert.Equal(250m, balance.BlossomGranted);
    }
}
