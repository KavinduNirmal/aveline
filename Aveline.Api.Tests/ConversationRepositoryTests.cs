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
        var customerId = Guid.NewGuid();

        var (conversation, created) = await _sut.GetOrCreateSalonAsync(orgId, customerId, "thread-1");

        Assert.True(created);
        Assert.Equal(ConversationKind.Salon, conversation.Kind);
        Assert.Equal(orgId, conversation.OrganizationId);
        Assert.Equal(customerId, conversation.CustomerId);
        Assert.Equal("thread-1", conversation.ThreadId);
        Assert.Equal(ConversationStatus.Active, conversation.Status);
        Assert.Equal(1, await _context.Conversations.CountAsync());
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_WhenExists_ReturnsExisting()
    {
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        await _sut.GetOrCreateSalonAsync(orgId, customerId, "thread-1");

        var (second, created) = await _sut.GetOrCreateSalonAsync(orgId, customerId, "thread-1");

        Assert.False(created);
        Assert.Equal(1, await _context.Conversations.CountAsync());
        Assert.Equal("thread-1", second.ThreadId);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_IsScopedPerOrganization()
    {
        var orgA = Guid.NewGuid();
        var orgB = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await _sut.GetOrCreateSalonAsync(orgA, customerId, "thread-a");
        var (orgBConversation, _) = await _sut.GetOrCreateSalonAsync(orgB, customerId, "thread-b");

        Assert.Equal(2, await _context.Conversations.CountAsync());
        Assert.Equal(orgB, orgBConversation.OrganizationId);
        Assert.Equal("thread-b", orgBConversation.ThreadId);
    }

    [Fact]
    public async Task GetAsync_ReturnsConversation_ForMatchingOrg()
    {
        var orgId = Guid.NewGuid();
        var (created, _) = await _sut.GetOrCreateSalonAsync(orgId, null, "thread-1");

        var found = await _sut.GetAsync(orgId, created.Id);

        Assert.NotNull(found);
        Assert.Equal(created.Id, found.Id);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForWrongOrg()
    {
        var orgId = Guid.NewGuid();
        var (created, _) = await _sut.GetOrCreateSalonAsync(orgId, null, "thread-1");

        var found = await _sut.GetAsync(Guid.NewGuid(), created.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task ListAsync_ReturnsOnlyOrgConversations_Paginated()
    {
        var orgId = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        for (var i = 0; i < 5; i++)
        {
            await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), $"thread-{i}");
        }
        await _sut.GetOrCreateSalonAsync(otherOrg, Guid.NewGuid(), "other-thread");

        var (items, total) = await _sut.ListAsync(orgId, 1, 2);

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
        var (created, _) = await _sut.GetOrCreateSalonAsync(orgId, null, "thread-1");
        created.Status = ConversationStatus.Resolved;
        created.LastMessageAt = DateTime.UtcNow;

        await _sut.SaveAsync(created);

        var reloaded = await _sut.GetAsync(orgId, created.Id);
        Assert.Equal(ConversationStatus.Resolved, reloaded!.Status);
        Assert.NotNull(reloaded.LastMessageAt);
    }
}
