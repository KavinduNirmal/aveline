using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Models;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 5 item 5.7 — the Q-4 "consent survives erasure" decision, tested explicitly (risk R-3).
///
/// <para>
/// The chosen semantics: erasure retains an anonymised <see cref="PrivacyErasureTombstone"/> keyed by
/// the salted phone fingerprint with the terminal <c>revoked</c> status. Because the inbound path
/// re-creates "identified but not yet answered" as a fresh <c>pending</c> row, deleting the consent
/// row alone would silently lapse the opt-out on the very next message. The gate therefore treats a
/// tombstone as a revocation whenever the live status is not an explicit <c>granted</c>.
/// </para>
/// </summary>
public class ConsentTombstoneTests
{
    private sealed class NoopCacheInvalidator : ICustomerCacheInvalidator
    {
        public Task<int> InvalidateAsync(
            Guid organizationId, Guid customerId, string phoneE164,
            string? fullName = null, string? email = null, CancellationToken cancellationToken = default)
            => Task.FromResult(0);
    }

    private static AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"tombstone-{Guid.NewGuid():N}")
            .Options);

    private static async Task<Guid> SeedOrganizationAsync(AppDbContext context)
    {
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"tomb_{Guid.NewGuid():N}",
            Email = $"tomb_{Guid.NewGuid():N}@aveline.lk",
            FirstName = "Tomb",
            LastName = "Owner",
            Username = $"tomb_{Guid.NewGuid():N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        };
        context.Users.Add(owner);
        var org = new Organization
        {
            Name = "Tombstone Boutique",
            Slug = $"tombstone-{Guid.NewGuid():N}",
            OwnerUserId = owner.Id,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();
        return org.Id;
    }

    private static async Task<Guid> SeedCustomerAsync(
        AppDbContext context, Guid orgId, string phone, string status)
    {
        var customer = new Customer
        {
            OrganizationId = orgId,
            PhoneNumber = phone,
            FullName = "Sarah Perera",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = orgId,
            CustomerId = customer.Id,
            ConsentStatus = status,
            ConsentRevokedAt = status == ConsentStatuses.Revoked ? DateTime.UtcNow : null,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
        return customer.Id;
    }

    private static ErasureService CreateErasure(AppDbContext context)
        => new(
            context,
            new InMemoryDistributedJobLock(),
            new NoopCacheInvalidator(),
            NullAuditService.Instance,
            TimeProvider.System,
            NullLogger<ErasureService>.Instance);

    private static ConsentGateService CreateGate(AppDbContext context)
        => new(
            new CustomerConsentRepository(context),
            NullLogger<ConsentGateService>.Instance,
            new ErasureTombstoneStore(context));

    [Fact]
    public async Task ARepeatInboundMessageAfterErasureIsNotReprocessed()
    {
        await using var context = CreateContext();
        var orgId = await SeedOrganizationAsync(context);
        var customerId = await SeedCustomerAsync(context, orgId, "+94771234567", ConsentStatuses.Granted);

        await CreateErasure(context).EraseAsync(new ErasureRequest(
            orgId, "+94771234567", ErasureScope.Org, "tombstone-key", ConsentActor.System, "test"));

        // This is exactly what the next inbound message does: the customer row is gone, so the
        // identify path creates a new one with a fresh `pending` consent row.
        var recreatedId = await SeedCustomerAsync(context, orgId, "+94771234567", ConsentStatuses.Pending);
        Assert.NotEqual(customerId, recreatedId);

        // Without the tombstone this is the R-3 failure: the fresh pending row would be processed.
        var gate = CreateGate(context);
        var decision = await gate.CheckAsync(orgId, recreatedId, "+94771234567");

        Assert.False(decision.ShouldProcess);
        Assert.Equal(ConsentStatuses.Revoked, decision.Status);
        Assert.Equal(ConsentGateReasons.ConsentRevoked, decision.Reason);
    }

    [Fact]
    public async Task TheGateProcessesNormallyWhenNoTombstoneExists()
    {
        await using var context = CreateContext();
        var orgId = await SeedOrganizationAsync(context);
        var customerId = await SeedCustomerAsync(context, orgId, "+94771234567", ConsentStatuses.Pending);

        var decision = await CreateGate(context).CheckAsync(orgId, customerId, "+94771234567");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(ConsentStatuses.Pending, decision.Status);
    }

    [Fact]
    public async Task AnExplicitGrantAfterErasureWinsOverTheTombstone()
    {
        await using var context = CreateContext();
        var orgId = await SeedOrganizationAsync(context);
        await SeedCustomerAsync(context, orgId, "+94771234567", ConsentStatuses.Granted);

        await CreateErasure(context).EraseAsync(new ErasureRequest(
            orgId, "+94771234567", ErasureScope.Org, "tombstone-key-2", ConsentActor.System, "test"));

        // A customer who later actively re-consents must not be locked out forever. `granted` is
        // the only status the tombstone does not override.
        var regrantedId = await SeedCustomerAsync(context, orgId, "+94771234567", ConsentStatuses.Granted);

        var decision = await CreateGate(context).CheckAsync(orgId, regrantedId, "+94771234567");

        Assert.True(decision.ShouldProcess);
        Assert.Equal(ConsentStatuses.Granted, decision.Status);
    }

    [Fact]
    public async Task TheTombstoneIsScopedToTheOrganisationThatErased()
    {
        await using var context = CreateContext();
        var orgA = await SeedOrganizationAsync(context);
        var orgB = await SeedOrganizationAsync(context);
        await SeedCustomerAsync(context, orgA, "+94771234567", ConsentStatuses.Granted);
        await SeedCustomerAsync(context, orgB, "+94771234567", ConsentStatuses.Pending);

        await CreateErasure(context).EraseAsync(new ErasureRequest(
            orgA, "+94771234567", ErasureScope.Org, "tombstone-key-3", ConsentActor.System, "test"));

        var store = new ErasureTombstoneStore(context);
        Assert.True(await store.WasErasedAsync(orgA, "+94771234567"));
        Assert.False(await store.WasErasedAsync(orgB, "+94771234567"));
    }

    [Fact]
    public async Task TheTombstoneStoresNoPhoneNumberInClearText()
    {
        await using var context = CreateContext();
        var orgId = await SeedOrganizationAsync(context);
        await SeedCustomerAsync(context, orgId, "+94771234567", ConsentStatuses.Granted);

        await CreateErasure(context).EraseAsync(new ErasureRequest(
            orgId, "+94771234567", ErasureScope.Org, "tombstone-key-4", ConsentActor.System, "test"));

        var tombstone = await context.PrivacyErasureTombstones.SingleAsync(t => t.OrganizationId == orgId);
        Assert.DoesNotContain("+94771234567", tombstone.PhoneHash, StringComparison.Ordinal);
        Assert.Equal(PhoneFingerprint.Of("+94771234567"), tombstone.PhoneHash);
        Assert.Equal(ConsentStatuses.Revoked, tombstone.Status);
    }
}
