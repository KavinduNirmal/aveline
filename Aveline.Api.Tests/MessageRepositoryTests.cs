using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Tests;

public class MessageRepositoryTests
{
    private readonly AppDbContext _context;
    private readonly MessageRepository _sut;
    private readonly ConversationRepository _conversations;
    private readonly Guid _orgId;
    private readonly Guid _conversationId;

    public MessageRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MessageRepositoryTests_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);
        _sut = new MessageRepository(_context);
        _conversations = new ConversationRepository(_context);
        _orgId = Guid.NewGuid();
        _conversationId = _conversations.GetOrCreateSalonAsync(_orgId, null, "thread-1").GetAwaiter().GetResult().Conversation.Id;
    }

    private Message CreateMessage(string text = "Hello", MessageKind kind = MessageKind.Note)
    {
        return new Message
        {
            ConversationId = _conversationId,
            AuthorKind = AuthorKind.User,
            AuthorUserId = Guid.NewGuid(),
            Kind = kind,
            ContentBlocksJson = JsonSerializer.Serialize(new[]
            {
                new { type = "text", text },
            }),
            Status = MessageStatus.Published,
        };
    }

    [Fact]
    public async Task SaveAsync_PersistsMessage()
    {
        var message = CreateMessage();

        await _sut.SaveAsync(message);

        Assert.NotEqual(default, message.Id);
        Assert.Equal(1, await _context.Messages.CountAsync());
    }

    [Fact]
    public async Task GetAsync_ReturnsMessage_ForConversation()
    {
        var message = CreateMessage();
        await _sut.SaveAsync(message);

        var found = await _sut.GetAsync(_conversationId, message.Id);

        Assert.NotNull(found);
        Assert.Equal(message.Id, found.Id);
        Assert.Equal(AuthorKind.User, found.AuthorKind);
    }

    [Fact]
    public async Task GetAsync_ReturnsNull_ForWrongConversation()
    {
        var message = CreateMessage();
        await _sut.SaveAsync(message);

        var found = await _sut.GetAsync(Guid.NewGuid(), message.Id);

        Assert.Null(found);
    }

    [Fact]
    public async Task ListAsync_ReturnsMessages_Paginated()
    {
        for (var i = 0; i < 5; i++)
        {
            await _sut.SaveAsync(CreateMessage($"msg-{i}"));
        }

        var (items, total) = await _sut.ListAsync(_conversationId, 1, 2);

        Assert.Equal(5, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ListAsync_DoesNotReturnMessages_FromOtherConversations()
    {
        var (otherConversation, _) = await _conversations.GetOrCreateSalonAsync(_orgId, Guid.NewGuid(), "thread-2");
        await _sut.SaveAsync(new Message
        {
            ConversationId = otherConversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = AgentKeys.Aveline,
            Kind = MessageKind.Note,
            ContentBlocksJson = "[]",
            Status = MessageStatus.Published,
        });

        var (items, total) = await _sut.ListAsync(_conversationId, 1, 50);

        Assert.Equal(0, total);
        Assert.Empty(items);
    }

    [Fact]
    public async Task ListAsync_WithAround_ReturnsPageContainingMessage()
    {
        var target = CreateMessage("target");
        await _sut.SaveAsync(target);
        for (var i = 0; i < 10; i++)
        {
            await _sut.SaveAsync(CreateMessage($"msg-{i}"));
        }

        var (items, _) = await _sut.ListAsync(_conversationId, 1, 5, around: target.Id);

        Assert.Contains(items, m => m.Id == target.Id);
    }

    [Fact]
    public async Task UpdateAsync_PersistsStatusAndBlocks_ForExistingMessage()
    {
        var message = CreateMessage("Original");
        await _sut.SaveAsync(message);

        var loaded = await _sut.GetAsync(_conversationId, message.Id);
        loaded!.Status = MessageStatus.Sent;
        loaded.ContentBlocksJson = JsonSerializer.Serialize(new[]
        {
            new { type = "text", text = "Revised" },
        });

        await _sut.UpdateAsync(loaded);

        var reloaded = await _sut.GetAsync(_conversationId, message.Id);
        Assert.Equal(MessageStatus.Sent, reloaded!.Status);
        Assert.Contains("Revised", reloaded.ContentBlocksJson);
        Assert.Equal(1, await _context.Messages.CountAsync());
    }
}
