using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

/// <summary>
/// The thread's own, per-user read state (D5 = B): one row per
/// (organization, user, conversation), with a marker that never moves backwards.
/// </summary>
public class ConversationReadStateRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly ConversationReadStateRepository _sut;
    private readonly Guid _orgId;
    private readonly Guid _conversationId;

    public ConversationReadStateRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"ReadStateTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new ConversationReadStateRepository(_context);
        _orgId = Guid.NewGuid();
        var conversations = new ConversationRepository(_context);
        _conversationId = conversations
            .GetOrCreateSalonAsync(_orgId, Guid.NewGuid(), null, "thread-read-1")
            .GetAwaiter().GetResult().Conversation.Id;
    }

    private ConversationReadState State(Guid userId, Guid? messageId) => new()
    {
        OrganizationId = _orgId,
        UserId = userId,
        ConversationId = _conversationId,
        LastReadMessageId = messageId,
        LastReadAtUtc = DateTime.UtcNow,
    };

    [Fact]
    public async Task SaveAsync_InsertsOneRowPerUserAndConversation()
    {
        var userId = Guid.NewGuid();

        await _sut.SaveAsync(State(userId, Guid.NewGuid()));
        await _sut.SaveAsync(State(userId, Guid.NewGuid()));

        var row = Assert.Single(await _context.ConversationReadStates.ToListAsync());
        Assert.Equal(userId, row.UserId);
    }

    [Fact]
    public async Task GetAsync_ReturnsTheCallersOwnRowOnly()
    {
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        await _sut.SaveAsync(State(mine, Guid.NewGuid()));
        await _sut.SaveAsync(State(theirs, Guid.NewGuid()));

        var found = await _sut.GetAsync(_orgId, mine, _conversationId);
        var missing = await _sut.GetAsync(_orgId, Guid.NewGuid(), _conversationId);

        Assert.NotNull(found);
        Assert.Equal(mine, found.UserId);
        Assert.Null(missing);
    }

    [Fact]
    public async Task GetAsync_ReturnsNullWhenNothingHasBeenRead()
    {
        // An absent row means nothing has been read. There is no coalesce to an epoch.
        Assert.Null(await _sut.GetAsync(_orgId, Guid.NewGuid(), _conversationId));
    }
}
