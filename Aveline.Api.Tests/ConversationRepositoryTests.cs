using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
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
        Assert.DoesNotContain(items, r => r.Conversation.ThreadId == "colleague-general");
        Assert.Contains(items, r => r.Conversation.ThreadId == "caller-general");
        Assert.Contains(items, r => r.Conversation.ThreadId == "shared-customer");
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
        Assert.All(items, r => Assert.Equal(orgId, r.Conversation.OrganizationId));
    }

    [Fact]
    public async Task ListAsync_IsTotalAcrossPages_WhenEffectiveTimestampsTie()
    {
        // A page boundary on a non-total order duplicates one row and skips another. The id
        // tiebreak is what makes the ordering total.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var shared = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc);
        for (var i = 0; i < 5; i++)
        {
            var (conversation, _) = await _sut.GetOrCreateSalonAsync(orgId, userId, Guid.NewGuid(), $"tie-thread-{i}");
            conversation.LastMessageAt = shared;
        }
        await _context.SaveChangesAsync();

        var (first, _) = await _sut.ListAsync(orgId, userId, 1, 2);
        var (second, _) = await _sut.ListAsync(orgId, userId, 2, 2);

        var firstIds = first.Select(r => r.Conversation.Id).ToList();
        var secondIds = second.Select(r => r.Conversation.Id).ToList();

        Assert.Equal(2, firstIds.Count);
        Assert.Equal(2, secondIds.Count);
        Assert.Empty(firstIds.Intersect(secondIds));
        Assert.Equal(4, firstIds.Concat(secondIds).Distinct().Count());
    }

    [Fact]
    public async Task ListAsync_JoinsTheNewestMessage_CustomerNameAndPendingSignOff()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        _context.Customers.Add(new Aveline.Api.Modules.CustomerConcierge.Models.Customer
        {
            Id = customerId,
            OrganizationId = orgId,
            PhoneNumber = "+94771234567",
            FullName = "Nadeesha Perera",
        });

        var (conversation, _) = await _sut.GetOrCreateSalonAsync(orgId, userId, customerId, "thread-1");
        _context.Messages.AddRange(
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.Agent,
                Kind = MessageKind.Note,
                ContentBlocksJson = "[{\"type\":\"text\",\"text\":\"older\"}]",
                Status = MessageStatus.Published,
                CreatedAt = new DateTime(2026, 9, 18, 11, 0, 0, DateTimeKind.Utc),
            },
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.System,
                Kind = MessageKind.ClientMessage,
                ContentBlocksJson = "[{\"type\":\"client_message\",\"from\":\"+94\",\"text\":\"newest\"}]",
                Status = MessageStatus.Published,
                CreatedAt = new DateTime(2026, 9, 18, 12, 0, 0, DateTimeKind.Utc),
            },
            new Message
            {
                ConversationId = conversation.Id,
                AuthorKind = AuthorKind.Agent,
                Kind = MessageKind.SignOff,
                ContentBlocksJson = "[{\"type\":\"sign_off\",\"reason\":\"discount\"}]",
                Status = MessageStatus.AwaitingSignOff,
                CreatedAt = new DateTime(2026, 9, 18, 10, 0, 0, DateTimeKind.Utc),
            });
        await _context.SaveChangesAsync();

        var (items, _) = await _sut.ListAsync(orgId, userId, 1, 50);

        var row = Assert.Single(items);
        Assert.Equal("Nadeesha Perera", row.CustomerName);
        Assert.NotNull(row.LastMessage);
        Assert.Equal(MessageKind.ClientMessage, row.LastMessage!.Kind);
        Assert.True(row.HasPendingSignOff);
    }

    [Fact]
    public async Task GetRowAsync_MatchesTheListRow_SoABroadcastTileEqualsAReRead()
    {
        // The realtime tile and the list row must be derived the same way, or a client that
        // receives a broadcast and then re-reads would see two different rows for one thread.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var (conversation, _) = await _sut.GetOrCreateSalonAsync(orgId, userId, Guid.NewGuid(), "thread-1");
        _context.Messages.Add(new Message
        {
            ConversationId = conversation.Id,
            AuthorKind = AuthorKind.Agent,
            AuthorAgentKey = "ava",
            Kind = MessageKind.Note,
            ContentBlocksJson = "[{\"type\":\"suggestion\",\"text\":\"A draft\"}]",
            Status = MessageStatus.Published,
            CreatedAt = DateTime.UtcNow,
        });
        await _context.SaveChangesAsync();

        var (items, _) = await _sut.ListAsync(orgId, userId, 1, 50);
        var listed = ConversationTileMapper.ToDto(Assert.Single(items));

        var row = await _sut.GetRowAsync(conversation.Id);
        Assert.NotNull(row);
        var broadcast = ConversationTileMapper.ToDto(row!);

        Assert.Equal(listed.Id, broadcast.Id);
        Assert.Equal(listed.CustomerName, broadcast.CustomerName);
        Assert.Equal(listed.LastMessagePreview, broadcast.LastMessagePreview);
        Assert.Equal(listed.LastMessageBlock, broadcast.LastMessageBlock);
        Assert.Equal(listed.LastMessageAuthor, broadcast.LastMessageAuthor);
        Assert.Equal(listed.LastMessageAgentKey, broadcast.LastMessageAgentKey);
        Assert.Equal(listed.Markers, broadcast.Markers);
    }

    [Fact]
    public async Task GetOrCreateSalonByExternalRefAsync_IsIdempotent_PerExternalRef()
    {
        var orgId = Guid.NewGuid();
        const string number = "+94771234567";

        var first = await _sut.GetOrCreateSalonByExternalRefAsync(orgId, number, "thread-1", null);
        var second = await _sut.GetOrCreateSalonByExternalRefAsync(orgId, number, "thread-2", null);

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
