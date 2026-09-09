using System.Text.Json;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

public class ConversationServiceTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add(formatter(state, exception));
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        private readonly List<Conversation> _conversations = [];
        public int SaveCount { get; private set; }

        public Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken ct)
            => Task.FromResult(_conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.Id == id));

        public Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken ct)
            => Task.FromResult(_conversations.FirstOrDefault(c => c.ThreadId == threadId));

        public Task<(IReadOnlyList<Conversation> Items, int Total)> ListAsync(Guid orgId, int page, int pageSize, CancellationToken ct)
        {
            var scoped = _conversations.Where(c => c.OrganizationId == orgId).OrderByDescending(c => c.LastMessageAt).ToList();
            var items = scoped.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<Conversation>, int)>((items, scoped.Count));
        }

        public Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(Guid orgId, Guid? customerId, string threadId, CancellationToken ct)
        {
            var existing = _conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.CustomerId == customerId && c.Kind == ConversationKind.Salon);
            if (existing is not null)
            {
                return Task.FromResult<(Conversation, bool)>((existing, false));
            }

            var created = new Conversation
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = orgId,
                Kind = ConversationKind.Salon,
                CustomerId = customerId,
                ThreadId = threadId,
                Status = ConversationStatus.Active,
            };
            _conversations.Add(created);
            return Task.FromResult<(Conversation, bool)>((created, true));
        }

        public Task<Conversation> GetOrCreateSalonByExternalRefAsync(Guid orgId, string externalRef, string threadId, CancellationToken ct)
        {
            var existing = _conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.ExternalRef == externalRef && c.Kind == ConversationKind.Salon);
            if (existing is not null)
            {
                return Task.FromResult(existing);
            }

            var created = new Conversation
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = orgId,
                Kind = ConversationKind.Salon,
                ExternalRef = externalRef,
                ThreadId = threadId,
                Status = ConversationStatus.Active,
            };
            _conversations.Add(created);
            return Task.FromResult(created);
        }

        public Task SaveAsync(Conversation conversation, CancellationToken ct)
        {
            SaveCount++;
            var existing = _conversations.FirstOrDefault(c => c.Id == conversation.Id);
            if (existing is not null)
            {
                existing.Status = conversation.Status;
                existing.LastMessageAt = conversation.LastMessageAt;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMessageRepository : IMessageRepository
    {
        private readonly List<Message> _messages = [];
        public int SaveCount { get; private set; }

        public Task<Message?> GetAsync(Guid conversationId, Guid messageId, CancellationToken ct)
            => Task.FromResult(_messages.FirstOrDefault(m => m.ConversationId == conversationId && m.Id == messageId));

        public Task<(IReadOnlyList<Message> Items, int Total)> ListAsync(Guid conversationId, int page, int pageSize, Guid? around, CancellationToken ct)
        {
            var scoped = _messages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.CreatedAt).ToList();
            return Task.FromResult<(IReadOnlyList<Message>, int)>((scoped, scoped.Count));
        }

        public Task SaveAsync(Message message, CancellationToken ct)
        {
            SaveCount++;
            var existing = _messages.FirstOrDefault(m => m.Id == message.Id);
            if (existing is not null)
            {
                existing.Status = message.Status;
                existing.ContentBlocksJson = message.ContentBlocksJson;
                existing.ContentHash = message.ContentHash;
            }
            else
            {
                _messages.Add(message);
            }
            return Task.CompletedTask;
        }

        public Task UpdateAsync(Message message, CancellationToken ct)
            => SaveAsync(message, ct);
    }

    private sealed class FakeAgentClient : IAgentServiceClient
    {
        public int PostCount { get; private set; }
        public string? LastPath { get; private set; }
        public string? LastBody { get; private set; }

        public async Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            PostCount++;
            LastPath = path;
            LastBody = await content.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK);
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private sealed class FakeSignOffDecisionRepository : ISignOffDecisionRepository
    {
        private readonly List<SignOffDecision> _decisions = [];

        public Task<SignOffDecision?> GetByMessageIdAsync(Guid messageId, CancellationToken ct)
            => Task.FromResult(_decisions.LastOrDefault(d => d.MessageId == messageId));

        public Task SaveAsync(SignOffDecision decision, CancellationToken ct)
        {
            _decisions.Add(decision);
            return Task.CompletedTask;
        }
    }

    private readonly FakeConversationRepository _conversations = new();
    private readonly FakeMessageRepository _messages = new();
    private readonly FakeAgentClient _agent = new();
    private readonly FakeSignOffDecisionRepository _signOffDecisions = new();
    private readonly ConversationService _sut;

    public ConversationServiceTests()
    {
        _sut = new ConversationService(_conversations, _messages, _signOffDecisions, _agent, new CapturingLogger<ConversationService>());
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_ReturnsExistingSalon()
    {
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var first = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), customerId, CancellationToken.None);

        var second = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), customerId, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_SeedsAvelineGreeting_OnNewSalon()
    {
        var orgId = Guid.NewGuid();

        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);

        var (items, _) = await _messages.ListAsync(salon.Id, 1, 50, null, CancellationToken.None);
        var greeting = Assert.Single(items);
        Assert.Equal(AuthorKind.Agent, greeting.AuthorKind);
        Assert.Equal(AgentKeys.Aveline, greeting.AuthorAgentKey);
        Assert.Equal(MessageKind.Note, greeting.Kind);
        Assert.Contains("Aveline", greeting.ContentBlocksJson);
    }

    [Fact]
    public async Task GetOrCreateSalonAsync_DoesNotReseedGreeting_OnExistingSalon()
    {
        var orgId = Guid.NewGuid();
        await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);

        await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);

        var (items, _) = await _messages.ListAsync(
            (await _conversations.ListAsync(orgId, 1, 50, CancellationToken.None)).Items.Single().Id,
            1, 50, null, CancellationToken.None);
        Assert.Single(items);
    }

    [Fact]
    public async Task SendStaffNoteAsync_PersistsNote_AndTriggersAgent()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);

        var message = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "Does anything match Michael's request?", CancellationToken.None);

        Assert.Equal(MessageKind.Note, message.Kind);
        Assert.Equal("User", message.AuthorKind);
        Assert.Equal(userId, message.AuthorUserId);
        Assert.Equal(MessageStatus.Published, message.Status);
        // Greeting (seeded on salon creation) + the staff note.
        Assert.Equal(2, _messages.SaveCount);
        Assert.Equal(1, _agent.PostCount);
        Assert.Equal("/agents/query", _agent.LastPath);
    }

    [Fact]
    public async Task SendStaffNoteAsync_Throws_WhenConversationNotInOrg()
    {
        var orgId = Guid.NewGuid();
        var otherOrg = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(otherOrg, Guid.NewGuid(), null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.SendStaffNoteAsync(orgId, Guid.NewGuid(), salon.Id, "hello", CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAgentMessageAsync_PersistsAgentMessage()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);
        var evt = new AgentMessageEvent(
            salon.Id,
            salon.ThreadId,
            AgentKeys.Aveline,
            MessageKind.Note,
            JsonSerializer.SerializeToElement(new[] { new { type = "text", text = "I have asked Ava and Elle." } }),
            null,
            Guid.NewGuid());

        var message = await _sut.ApplyAgentMessageAsync(evt, CancellationToken.None);

        Assert.Equal("Agent", message.AuthorKind);
        Assert.Equal(AgentKeys.Aveline, message.AgentKey);
        Assert.Equal(MessageKind.Note, message.Kind);
        Assert.Equal(MessageStatus.Published, message.Status);
        // Greeting (seeded on salon creation) + the agent message.
        Assert.Equal(2, _messages.SaveCount);
    }

    [Fact]
    public async Task ApplyAgentMessageAsync_ResolvesConversationByThreadId_WhenEventHasEmptyConversationId()
    {
        // The agent's message.created payload only carries a thread_id (no conversation_id),
        // so the parsed event has Guid.Empty. The message must be persisted against the
        // conversation resolved from the thread id, not the empty event id.
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);
        var evt = new AgentMessageEvent(
            Guid.Empty,
            salon.ThreadId,
            AgentKeys.Aveline,
            MessageKind.Note,
            JsonSerializer.SerializeToElement(new[] { new { type = "text", text = "I have looked into this." } }),
            null,
            Guid.NewGuid());

        var message = await _sut.ApplyAgentMessageAsync(evt, CancellationToken.None);

        Assert.Equal(salon.Id, message.ConversationId);
        // Greeting (seeded on salon creation) + the agent message.
        Assert.Equal(2, _messages.SaveCount);
    }

    [Fact]
    public async Task ApplyAgentMessageAsync_Throws_WhenConversationNotFound()
    {
        var evt = new AgentMessageEvent(
            Guid.NewGuid(),
            "thread-unknown",
            AgentKeys.Ava,
            MessageKind.Note,
            JsonSerializer.SerializeToElement(new object[] { }),
            null,
            Guid.NewGuid());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.ApplyAgentMessageAsync(evt, CancellationToken.None));
    }

    private async Task<(Guid orgId, Guid conversationId, Guid messageId, string contentHash)> SeedSignOffAsync()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);
        var evt = new AgentMessageEvent(
            salon.Id,
            salon.ThreadId,
            AgentKeys.Lina,
            MessageKind.SignOff,
            JsonSerializer.SerializeToElement(new[] { new { type = "sign_off", approvalId = "a-1", amount = 48000 } }),
            null,
            Guid.NewGuid());
        var message = await _sut.ApplyAgentMessageAsync(evt, CancellationToken.None);
        // The agent publishes SignOff as AwaitingSignOff; simulate by flipping status.
        var stored = await _messages.GetAsync(salon.Id, message.Id, CancellationToken.None);
        stored!.Status = MessageStatus.AwaitingSignOff;
        await _messages.SaveAsync(stored, CancellationToken.None);
        return (orgId, salon.Id, message.Id, stored.ContentHash!);
    }

    [Fact]
    public async Task DecideSignOffAsync_Approve_PublishesMessage_AndActivatesConversation()
    {
        var (orgId, conversationId, messageId, contentHash) = await SeedSignOffAsync();

        var decided = await _sut.DecideSignOffAsync(orgId, Guid.NewGuid(), conversationId, messageId, true, contentHash, CancellationToken.None);

        Assert.Equal(MessageStatus.Published, decided.Status);
        var conversation = await _conversations.GetAsync(orgId, conversationId, CancellationToken.None);
        Assert.Equal(ConversationStatus.Active, conversation!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_Reject_CancelsMessage_AndResolvesConversation()
    {
        var (orgId, conversationId, messageId, contentHash) = await SeedSignOffAsync();

        var decided = await _sut.DecideSignOffAsync(orgId, Guid.NewGuid(), conversationId, messageId, false, contentHash, CancellationToken.None);

        Assert.Equal(MessageStatus.Cancelled, decided.Status);
        var conversation = await _conversations.GetAsync(orgId, conversationId, CancellationToken.None);
        Assert.Equal(ConversationStatus.Resolved, conversation!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_Throws_WhenMessageIsNotSignOff()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);
        var note = await _sut.SendStaffNoteAsync(orgId, Guid.NewGuid(), salon.Id, "hello", CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DecideSignOffAsync(orgId, Guid.NewGuid(), salon.Id, note.Id, true, "hash", CancellationToken.None));
    }

    [Fact]
    public async Task DecideSignOffAsync_Throws_WhenContentHashDoesNotMatch()
    {
        var (orgId, conversationId, messageId, _) = await SeedSignOffAsync();

        // A stale/wrong hash means the payload changed after display; the decision is void.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DecideSignOffAsync(orgId, Guid.NewGuid(), conversationId, messageId, true, "stale-hash", CancellationToken.None));

        // The message must remain awaiting a decision.
        var stored = await _messages.GetAsync(conversationId, messageId, CancellationToken.None);
        Assert.Equal(MessageStatus.AwaitingSignOff, stored!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_RecordsDecisionOutOfBand()
    {
        var (orgId, conversationId, messageId, contentHash) = await SeedSignOffAsync();
        var userId = Guid.NewGuid();

        await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, contentHash, CancellationToken.None);

        var decision = await _signOffDecisions.GetByMessageIdAsync(messageId, CancellationToken.None);
        Assert.NotNull(decision);
        Assert.Equal(messageId, decision!.MessageId);
        Assert.Equal(contentHash, decision.ContentHash);
        Assert.True(decision.Approved);
        Assert.Equal(userId, decision.DecidedBy);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_CreatesSalon_AndClientMessage()
    {
        var orgId = Guid.NewGuid();

        var message = await _sut.RecordInboundClientMessageAsync(orgId, "+94771234567", "+94771234567", "Do you have this in blue?", CancellationToken.None);

        Assert.Equal(MessageKind.ClientMessage, message.Kind);
        Assert.Equal("System", message.AuthorKind);
        Assert.Equal(1, _messages.SaveCount);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_ReusesExistingSalon_ForSameExternalRef()
    {
        var orgId = Guid.NewGuid();
        const string number = "+94771234567";

        await _sut.RecordInboundClientMessageAsync(orgId, number, number, "first", CancellationToken.None);
        await _sut.RecordInboundClientMessageAsync(orgId, number, number, "second", CancellationToken.None);

        Assert.Equal(2, _messages.SaveCount);
        var (items, _) = await _conversations.ListAsync(orgId, 1, 50, CancellationToken.None);
        Assert.Single(items);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_TriggersInboundDraft_WithPhoneContext()
    {
        var orgId = Guid.NewGuid();
        const string from = "+94771234567";

        await _sut.RecordInboundClientMessageAsync(orgId, from, from, "Do you have this in blue?", CancellationToken.None);

        // The inbound message must trigger an agent draft into the Salon (Issue #152), carrying
        // the client's phone so the memory agent can identify them.
        Assert.Equal(1, _agent.PostCount);
        Assert.Equal("/agents/query", _agent.LastPath);
        Assert.Contains("thread_id", _agent.LastBody);
        Assert.Contains("phone_number", _agent.LastBody);
        Assert.Contains("inbound", _agent.LastBody);
    }

    [Fact]
    public async Task ApplyAgentMessageUpdateAsync_UpdatesStatusAndBlocks()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);
        var created = await _sut.ApplyAgentMessageAsync(new AgentMessageEvent(
            salon.Id, salon.ThreadId, AgentKeys.Aveline, MessageKind.Note,
            JsonSerializer.SerializeToElement(new[] { new { type = "text", text = "Original" } }),
            null, Guid.NewGuid()), CancellationToken.None);

        var updated = await _sut.ApplyAgentMessageUpdateAsync(new AgentMessageUpdateEvent(
            salon.Id, created.Id, MessageStatus.Sent,
            JsonSerializer.SerializeToElement(new[] { new { type = "text", text = "Revised" } })),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.Equal(MessageStatus.Sent, updated!.Status);

        // The change is persisted, not just returned.
        var stored = await _messages.GetAsync(salon.Id, created.Id, CancellationToken.None);
        Assert.Equal(MessageStatus.Sent, stored!.Status);
        Assert.Contains("Revised", stored.ContentBlocksJson);
    }

    [Fact]
    public async Task ApplyAgentMessageUpdateAsync_RecomputesContentHash_ForSignOff()
    {
        var (orgId, conversationId, messageId, originalHash) = await SeedSignOffAsync();

        var updated = await _sut.ApplyAgentMessageUpdateAsync(new AgentMessageUpdateEvent(
            conversationId, messageId, null,
            JsonSerializer.SerializeToElement(new[] { new { type = "sign_off", approvalId = "a-1", amount = 99999 } })),
            CancellationToken.None);

        Assert.NotNull(updated);
        Assert.NotNull(updated!.ContentHash);
        // Editing a SignOff's payload re-binds it to a fresh hash so a later decision matches
        // exactly what the human sees.
        Assert.NotEqual(originalHash, updated.ContentHash);
    }

    [Fact]
    public async Task ApplyAgentMessageUpdateAsync_ReturnsNull_ForUnknownMessage()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);

        var result = await _sut.ApplyAgentMessageUpdateAsync(new AgentMessageUpdateEvent(
            salon.Id, Guid.NewGuid(), MessageStatus.Published, default), CancellationToken.None);

        Assert.Null(result);
    }
}
