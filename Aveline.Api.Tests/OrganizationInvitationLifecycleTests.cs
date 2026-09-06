using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Tests for the staff-invitation lifecycle added in #72: listing pending invitations
/// and revoking them (including the cannot-revoke-accepted rule).
/// </summary>
public class OrganizationInvitationLifecycleTests
{
    private readonly AppDbContext _context;
    private readonly OrganizationService _sut;
    private readonly FakeInvitationCodeStore _codeStore;

    public OrganizationInvitationLifecycleTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OrganizationInvitationLifecycleTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        var memCacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        var distCache = new MemoryDistributedCache(memCacheOptions);
        var cacheService = new UserCacheService(distCache, NullLogger<UserCacheService>.Instance);
        _codeStore = new FakeInvitationCodeStore();
        _sut = new OrganizationService(
            new OrganizationRepository(_context),
            new InvitationRepository(_context),
            _codeStore,
            new UserRepository(_context),
            cacheService,
            NullLogger<OrganizationService>.Instance);
    }

    private async Task<(User owner, Organization org)> SeedAsync(string suffix)
    {
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"clerk_{suffix}",
            Email = $"{suffix}@aveline.lk",
            FirstName = "Owner",
            LastName = "User",
            Username = $"clerk_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
        };
        _context.Users.Add(owner);
        await _context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Boutique {suffix}",
            Slug = $"boutique-{suffix}",
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return (owner, org);
    }

    [Fact]
    public async Task ListPendingInvitationsAsync_ReturnsOnlyPending()
    {
        var (owner, org) = await SeedAsync("list");
        await _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: "a@aveline.lk"));
        await _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueManager, RecipientEmail: "b@aveline.lk"));

        var pending = await _sut.ListPendingInvitationsAsync(org.Id);

        Assert.Equal(2, pending.Count);
    }

    [Fact]
    public async Task RevokeInvitationAsync_MarksRevoked_AndCannotBeAccepted()
    {
        var (owner, org) = await SeedAsync("revoke");
        var invited = await _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueSupervisor, RecipientEmail: "staff@aveline.lk"));

        await _sut.RevokeInvitationAsync(org.Id, invited.Invitation.Id, owner.Id);

        var pendingAfter = await _sut.ListPendingInvitationsAsync(org.Id);
        Assert.Empty(pendingAfter);

        var staff = await AddStaffAsync("clerk_staff_revoke", "staff@aveline.lk");
        await Assert.ThrowsAsync<InvitationNotAcceptableException>(
            () => _sut.AcceptInvitationAsync(invited.Code, staff.Id, staff.Email));
    }

    [Fact]
    public async Task RevokeInvitationAsync_ForAnotherOrganization_ThrowsNotFound()
    {
        var (ownerA, orgA) = await SeedAsync("a");
        var (_, orgB) = await SeedAsync("b");
        var invited = await _sut.InviteMemberAsync(orgA.Id, ownerA.Id, new InviteMemberRequest(Roles.BoutiqueStaff));

        await Assert.ThrowsAsync<InvitationNotFoundException>(
            () => _sut.RevokeInvitationAsync(orgB.Id, invited.Invitation.Id, ownerA.Id));
    }

    [Fact]
    public async Task RevokeInvitationAsync_WhenAlreadyAccepted_Throws()
    {
        var (owner, org) = await SeedAsync("accepted");
        var invited = await _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: "staff@aveline.lk"));
        var staff = await AddStaffAsync("clerk_staff_accepted", "staff@aveline.lk");

        await _sut.AcceptInvitationAsync(invited.Code, staff.Id, staff.Email);

        await Assert.ThrowsAsync<CannotRevokeAcceptedInvitationException>(
            () => _sut.RevokeInvitationAsync(org.Id, invited.Invitation.Id, owner.Id));
    }

    [Fact]
    public async Task RevokeInvitationAsync_UnknownInvitation_ThrowsNotFound()
    {
        var (owner, org) = await SeedAsync("unknown");
        await Assert.ThrowsAsync<InvitationNotFoundException>(
            () => _sut.RevokeInvitationAsync(org.Id, Guid.NewGuid(), owner.Id));
    }

    [Fact]
    public async Task InviteMemberAsync_WritesCodeToStore_AndRemovesOnAccept()
    {
        var (owner, org) = await SeedAsync("codestore");
        var invited = await _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: "cs@aveline.lk"));
        var staff = await AddStaffAsync("clerk_staff_cs", "cs@aveline.lk");

        // The code was written to the transient store.
        Assert.True(_codeStore.Contains(invited.Code));

        await _sut.AcceptInvitationAsync(invited.Code, staff.Id, staff.Email);

        // Redeemed -> removed from the store (one-time use).
        Assert.False(_codeStore.Contains(invited.Code));
    }

    [Fact]
    public async Task AcceptInvitationAsync_FallsBackToHash_WhenStoreMisses()
    {
        var (owner, org) = await SeedAsync("fallback");
        var invited = await _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueManager, RecipientEmail: "fb@aveline.lk"));
        var staff = await AddStaffAsync("clerk_staff_fb", "fb@aveline.lk");

        Assert.True(_codeStore.Contains(invited.Code));

        // Simulate a Redis flush / TTL expiry: remove the transient mapping. The durable
        // Postgres hash must still allow the code to be redeemed (hybrid fallback).
        await _codeStore.RemoveAsync(invited.Code);

        var accepted = await _sut.AcceptInvitationAsync(invited.Code, staff.Id, staff.Email);

        Assert.Equal(org.Id, accepted.OrganizationId);
        Assert.Equal(Roles.BoutiqueManager, accepted.BoutiqueRole);
    }

    [Fact]
    public async Task InviteMemberAsync_FailsFast_WhenCodeStoreUnavailable_AndRollsBackInvitation()
    {
        var (owner, org) = await SeedAsync("failfast");
        var staff = await AddStaffAsync("clerk_staff_ff", "ff@aveline.lk");

        _codeStore.ThrowOnStore = true;

        await Assert.ThrowsAsync<InvitationCodeStoreUnavailableException>(() =>
            _sut.InviteMemberAsync(org.Id, owner.Id, new InviteMemberRequest(Roles.BoutiqueStaff, RecipientEmail: "ff@aveline.lk")));

        // The compensating delete removed the log row -> nothing pending.
        var pending = await _sut.ListPendingInvitationsAsync(org.Id);
        Assert.Empty(pending);
    }

    private async Task<User> AddStaffAsync(string clerkId, string email)
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = email,
            FirstName = "Staff",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }
}
