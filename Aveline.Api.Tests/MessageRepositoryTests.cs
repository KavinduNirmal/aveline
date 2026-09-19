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
        _conversationId = _conversations.GetOrCreateSalonAsync(_orgId, Guid.NewGuid(), null, "thread-1").GetAwaiter().GetResult().Conversation.Id;
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

        var (items, total, _) = await _sut.ListAsync(_conversationId, 1, 2);

        Assert.Equal(5, total);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task ListAsync_DoesNotReturnMessages_FromOtherConversations()
    {
        var (otherConversation, _) = await _conversations.GetOrCreateSalonAsync(_orgId, Guid.NewGuid(), Guid.NewGuid(), "thread-2");
        await _sut.SaveAsync(new Message
        {
            ConversationId = otherConversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = AgentKeys.Aveline,
            Kind = MessageKind.Note,
            ContentBlocksJson = "[]",
            Status = MessageStatus.Published,
        });

        var (items, total, _) = await _sut.ListAsync(_conversationId, 1, 50);

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

        var (items, _, _) = await _sut.ListAsync(_conversationId, 1, 5, around: target.Id);

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

    [Fact]
    public async Task ListAsync_BreaksTiedTimestampsOnId_SoPagesDoNotDuplicateOrSkip()
    {
        // SaveAsync stamps CreatedAt, so a tie is written directly. The inserted ids are
        // deliberately not in Guid comparison order: without a tiebreaker the query falls back
        // to the store's arbitrary order, which is what lets a message fall between two pages.
        var tied = DateTime.UtcNow;
        var inserted = new[]
        {
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("01000000-0000-0000-0000-000000000001"),
            Guid.Parse("20000000-0000-0000-0000-000000000001"),
            Guid.Parse("02000000-0000-0000-0000-000000000001"),
        };
        foreach (var id in inserted)
        {
            _context.Messages.Add(new Message
            {
                Id = id,
                ConversationId = _conversationId,
                AuthorKind = AuthorKind.User,
                AuthorUserId = Guid.NewGuid(),
                Kind = MessageKind.Note,
                ContentBlocksJson = "[]",
                Status = MessageStatus.Published,
                CreatedAt = tied,
            });
        }
        await _context.SaveChangesAsync();

        var first = await _sut.ListAsync(_conversationId, 1, 2);
        var second = await _sut.ListAsync(_conversationId, 2, 2);
        var seen = first.Items.Concat(second.Items).Select(m => m.Id).ToList();

        Assert.Equal(4, seen.Count);
        Assert.Equal(4, seen.Distinct().Count());
        Assert.Equal(inserted.OrderBy(id => id).ToList(), seen);
    }

    [Fact]
    public async Task ListAsync_WithAround_EchoesThePageThatHoldsTheAnchor()
    {
        // A deep-link must open on the anchor's page, and the response must say which page that
        // was: an off-grid half-page window could not be expressed in the page envelope, and a
        // client could not then tell whether earlier history exists.
        var baseTime = DateTime.UtcNow;
        var ids = new List<Guid>();
        for (var i = 0; i < 12; i++)
        {
            var id = Guid.CreateVersion7();
            _context.Messages.Add(new Message
            {
                Id = id,
                ConversationId = _conversationId,
                AuthorKind = AuthorKind.User,
                AuthorUserId = Guid.NewGuid(),
                Kind = MessageKind.Note,
                ContentBlocksJson = "[]",
                Status = MessageStatus.Published,
                CreatedAt = baseTime.AddMinutes(i),
            });
            ids.Add(id);
        }
        await _context.SaveChangesAsync();

        var (items, total, page) = await _sut.ListAsync(_conversationId, 1, 5, around: ids[7]);

        Assert.Equal(12, total);
        Assert.Equal(2, page);
        Assert.Equal(5, items.Count);
        Assert.Contains(items, m => m.Id == ids[7]);
        Assert.Equal(ids[5], items.First().Id);
    }

    [Fact]
    public async Task ListAsync_WithAnUnknownAround_ServesTheFirstPage()
    {
        for (var i = 0; i < 3; i++)
        {
            await _sut.SaveAsync(CreateMessage($"msg-{i}"));
        }

        var (items, _, page) = await _sut.ListAsync(_conversationId, 1, 2, around: Guid.NewGuid());

        Assert.Equal(1, page);
        Assert.Equal(2, items.Count);
    }

    [Fact]
    public async Task GetByClientMessageIdAsync_ReturnsTheStoredMessage()
    {
        var key = Guid.NewGuid();
        var message = CreateMessage("On my way.");
        message.ClientMessageId = key;
        await _sut.SaveAsync(message);
        await _sut.SaveAsync(CreateMessage("Something else."));

        var found = await _sut.GetByClientMessageIdAsync(_conversationId, key);

        Assert.NotNull(found);
        Assert.Equal(message.Id, found.Id);
    }

    [Fact]
    public async Task GetByClientMessageIdAsync_ReturnsNull_ForAnUnknownKeyOrAnotherConversation()
    {
        var key = Guid.NewGuid();
        var message = CreateMessage("On my way.");
        message.ClientMessageId = key;
        await _sut.SaveAsync(message);

        Assert.Null(await _sut.GetByClientMessageIdAsync(_conversationId, Guid.NewGuid()));
        Assert.Null(await _sut.GetByClientMessageIdAsync(Guid.NewGuid(), key));
    }
}
