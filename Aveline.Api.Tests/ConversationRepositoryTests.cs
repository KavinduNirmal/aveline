using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class ConversationRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly ConversationRepository _sut;

    public ConversationRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ConversationRepositoryTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new ConversationRepository(_context);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_WhenNoneExists_CreatesSalon()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var (conversation, created) = await _sut.GetOrCreateSalonAsync(orgId, userId, customerId, "thread-1");

        Assert.True(created);
        Assert.Equal(ConversationKind.Salon, conversation.Kind);
        Assert.Equal(orgId, conversation.OrganizationId);
        Assert.Equal(customerId, conversation.CustomerId);
        // A customer-bound Salon is organization-shared, so it carries no owner (ADR-021).
        Assert.Null(conversation.OwnerUserId);
        Assert.Equal("thread-1", conversation.ThreadId);
        Assert.Equal(ConversationStatus.Active, conversation.Status);
        Assert.Equal(1, await _context.Conversations.CountAsync());
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_WhenExists_ReturnsExisting()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _sut.GetOrCreateSalonAsync(orgId, userId, customerId, "thread-1");

        var (second, created) = await _sut.GetOrCreateSalonAsync(orgId, userId, customerId, "thread-1");

        Assert.False(created);
        Assert.Equal(1, await _context.Conversations.CountAsync());
        Assert.Equal("thread-1", second.ThreadId);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_IsScopedPerOrganization()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await _sut.GetOrCreateSalonAsync(orgA, userId, customerId, "thread-a");
        var (orgBConversation, _) = await _sut.GetOrCreateSalonAsync(orgB, userId, customerId, "thread-b");

        Assert.Equal(2, await _context.Conversations.CountAsync());
        Assert.Equal(orgB, orgBConversation.OrganizationId);
        Assert.Equal("thread-b", orgBConversation.ThreadId);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_GeneralSalon_IsScopedPerUser()
    {
        var orgId = Guid.NewGuid();
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        var (salonA, createdA) = await _sut.GetOrCreateSalonAsync(orgId, userA, null, "thread-a");
        var (salonB, createdB) = await _sut.GetOrCreateSalonAsync(orgId, userB, null, "thread-b");

        Assert.True(createdA);
        Assert.True(createdB);
        Assert.NotEqual(salonA.Id, salonB.Id);
        Assert.Equal(userA, salonA.OwnerUserId);
        Assert.Equal(userB, salonB.OwnerUserId);
        Assert.Equal(2, await _context.Conversations.CountAsync());

        // The same user resolves back to their own Salon on a repeat call.
        var (again, createdAgain) = await _sut.GetOrCreateSalonAsync(orgId, userA, null, "thread-ignored");
        Assert.False(createdAgain);
        Assert.Equal(salonA.Id, again.Id);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_CustomerSalon_IsSharedAcrossUsers()
    {
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        var (first, _) = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), customerId, "thread-1");
        var (second, createdSecond) = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), customerId, "thread-2");

        Assert.False(createdSecond);
        Assert.Equal(first.Id, second.Id);
        Assert.Null(second.OwnerUserId);
        Assert.Equal(1, await _context.Conversations.CountAsync());
    }

    [Fact]
    public async Task GetAsync_ReturnsConversation_ForMatchingOrg()
    {
        var orgId = Guid.NewGuid();
        var (created, _) = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, "thread-1");

        var found = await _sut.GetAsync(orgId, created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForWrongOrg()
    {
        var orgId = Guid.NewGuid();
        var (created, _) = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, "thread-1");

        var found = await _sut.GetAsync(Guid.NewGuid(), created.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task GetVisibleToUserAsync_HidesAnotherUsersGeneralSalon()
    {
        var orgId = Guid.NewGuid();
        var owner = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        var (salon, _) = await _sut.GetOrCreateSalonAsync(orgId, owner, null, "thread-1");

        Assert.NotNull(await _sut.GetVisibleToUserAsync(orgId, salon.Id, owner));
        Assert.Null(await _sut.GetVisibleToUserAsync(orgId, salon.Id, colleague));
    }

    [Fact]
    public async Task GetVisibleToUserAsync_ReturnsSharedSalon_ForAnyMember()
    {
        var orgId = Guid.NewGuid();
        var (salon, _) = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), Guid.NewGuid(), "thread-1");

        Assert.NotNull(await _sut.GetVisibleToUserAsync(orgId, salon.Id, Guid.NewGuid()));
    }

    [Fact]
    public async Task ListAsync_ReturnsSharedSalonsAndOwnGeneralSalon_Only()
    {
        var orgId = Guid.NewGuid();
        var caller = Guid.NewGuid();
        var colleague = Guid.NewGuid();

        await _sut.GetOrCreateSalonAsync(orgId, caller, null, "caller-general");
        await _sut.GetOrCreateSalonAsync(orgId, colleague, null, "colleague-general");
        await _sut.GetOrCreateSalonAsync(orgId, caller, Guid.NewGuid(), "shared-customer");

        var (items, total) = await _sut.ListAsync(orgId, caller, 1, 50);

        Assert.Equal(2, total);
        Assert.DoesNotContain(items, c => c.ThreadId == "colleague-general");
        Assert.Contains(items, c => c.ThreadId == "caller-general");
        Assert.Contains(items, c => c.ThreadId == "shared-customer");
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyOrgConversations_Paginated()
    {
        var orgId = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var userId = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await _sut.GetOrCreateSalonAsync(orgId, userId, Guid.NewGuid(), $"thread-{i}");
        }
        await _sut.GetOrCreateSalonAsync(otherOrg, Guid.NewGuid(), Guid.NewGuid(), "other-thread");

        var (items, total) = await _sut.ListAsync(orgId, userId, 1, 2);

        Assert.Equal(5, total);
        Assert.Equal(2, items.Count);
        Assert.All(items, c => Assert.Equal(orgId, c.OrganizationId));
    }

    [Fact]
    public async Task GetOrCreateSalonByExternalRefAsync_IsIdempotent_PerExternalRef()
    {
        var orgId = Guid.NewGuid();
        const string number = "+94771234567";

        var first = await _sut.GetOrCreateSalonByExternalRefAsync(orgId, number, "thread-1");
        var second = await _sut.GetOrCreateSalonByExternalRefAsync(orgId, number, "thread-2");

        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await _context.Conversations.CountAsync());
        Assert.Equal(number, second.ExternalRef);
    }

    [Fact]
    public async Task SaveAsync_UpdatesConversation()
    {
        var orgId = Guid.NewGuid();
        var (created, _) = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, "thread-1");
        created.Status = ConversationStatus.Resolved;
        created.LastMessageAt = DateTime.UtcNow;

        await _sut.SaveAsync(created);

        var reloaded = await _sut.GetAsync(orgId, created.Id);
        Assert.Equal(ConversationStatus.Resolved, reloaded!.Status);
        Assert.NotNull(reloaded.LastMessageAt);
    }
}
