using System.Text.Json;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Conversations.Attachments;
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

    [Fact]
    public async Task SelectCustomerAsync_BindsCustomer_AndReTriggersAgentWithContext()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var customerId = Guid.NewGuid();

        var selected = await _sut.SelectCustomerAsync(orgId, userId, salon.Id, customerId, "Any events for Samantha?", CancellationToken.None);

        Assert.NotNull(selected);
        Assert.Equal(customerId, selected!.CustomerId);

        // The re-triggered agent call carries the resolved customer id.
        Assert.Equal("/agents/query", _agent.LastPath);
        Assert.Contains("\"query\":\"Any events for Samantha?\"", _agent.LastBody);
        Assert.Contains("\"customer_id\":\"" + customerId + "\"", _agent.LastBody);

        // The binding persisted on the conversation row.
        var reloaded = await _conversations.GetAsync(orgId, salon.Id, CancellationToken.None);
        Assert.Equal(customerId, reloaded!.CustomerId);
    }

    [Fact]
    public async Task SelectCustomerAsync_ReturnsNull_WhenConversationMissing()
    {
        var orgId = Guid.NewGuid();

        var result = await _sut.SelectCustomerAsync(orgId, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);

        Assert.Null(result);
        Assert.Equal(0, _agent.PostCount);
    }

    private sealed class FakeConversationRepository : IConversationRepository
    {
        private readonly List<Conversation> _conversations = [];
        public int SaveCount { get; private set; }

        public Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken ct)
            => Task.FromResult(_conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.Id == id));

        public Task<Conversation?> GetVisibleToUserAsync(Guid orgId, Guid id, Guid userId, CancellationToken ct)
            => Task.FromResult(_conversations.FirstOrDefault(
                c => c.OrganizationId == orgId
                     && c.Id == id
                     && (c.OwnerUserId == null || c.OwnerUserId == userId)));

        public Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken ct)
            => Task.FromResult(_conversations.FirstOrDefault(c => c.ThreadId == threadId));

        public Task<(IReadOnlyList<ConversationListRow> Items, int Total)> ListAsync(Guid orgId, Guid userId, int page, int pageSize, CancellationToken ct)
        {
            var scoped = _conversations
                .Where(c => c.OrganizationId == orgId && (c.OwnerUserId == null || c.OwnerUserId == userId))
                .OrderByDescending(c => c.LastMessageAt).ToList();
            var rows = scoped
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(c => new ConversationListRow(c, null, null, false))
                .ToList();
            return Task.FromResult<(IReadOnlyList<ConversationListRow>, int)>((rows, scoped.Count));
        }

        public Task<ConversationListRow?> GetRowAsync(Guid conversationId, CancellationToken ct)
            => Task.FromResult(
                _conversations
                    .Where(c => c.Id == conversationId)
                    .Select(c => new ConversationListRow(c, null, null, false))
                    .FirstOrDefault());

        public Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(Guid orgId, Guid userId, Guid? customerId, string threadId, CancellationToken ct)
        {
            var existing = customerId is null
                ? _conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.OwnerUserId == userId && c.CustomerId == null && c.Kind == ConversationKind.Salon)
                : _conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.CustomerId == customerId && c.Kind == ConversationKind.Salon);
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
                OwnerUserId = customerId is null ? userId : null,
                ThreadId = threadId,
                Status = ConversationStatus.Active,
            };
            _conversations.Add(created);
            return Task.FromResult<(Conversation, bool)>((created, true));
        }

        public Task<Conversation> GetOrCreateSalonByExternalRefAsync(Guid orgId, string externalRef, string threadId, Guid? customerId, CancellationToken ct)
        {
            var existing = _conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.ExternalRef == externalRef && c.Kind == ConversationKind.Salon);
            if (existing is not null)
            {
                if (customerId is not null && existing.CustomerId is null)
                {
                    existing.CustomerId = customerId;
                }
                return Task.FromResult(existing);
            }

            var created = new Conversation
            {
                Id = Guid.CreateVersion7(),
                OrganizationId = orgId,
                Kind = ConversationKind.Salon,
                CustomerId = customerId,
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
                existing.CustomerId = conversation.CustomerId;
                existing.OwnerUserId = conversation.OwnerUserId;
                existing.LastMessageAt = conversation.LastMessageAt;
            }
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMessageRepository : IMessageRepository
    {
        private readonly List<Message> _messages = [];
        private DateTime _clock = DateTime.UtcNow;
        public int SaveCount { get; private set; }

        public Task<Message?> GetAsync(Guid conversationId, Guid messageId, CancellationToken ct)
            => Task.FromResult(_messages.FirstOrDefault(m => m.ConversationId == conversationId && m.Id == messageId));

        public Task<Message?> GetByClientMessageIdAsync(Guid conversationId, Guid clientMessageId, CancellationToken ct)
            => Task.FromResult(_messages.FirstOrDefault(m =>
                m.ConversationId == conversationId && m.ClientMessageId == clientMessageId));

        public Task<(IReadOnlyList<Message> Items, int Total, int Page)> ListAsync(Guid conversationId, int page, int pageSize, Guid? around, CancellationToken ct)
        {
            var scoped = _messages.Where(m => m.ConversationId == conversationId).OrderBy(m => m.CreatedAt).ToList();
            // The fake serves the whole thread at page 1; the deep-link page arithmetic is the
            // real repository's, and is covered by MessageRepositoryTests.
            return Task.FromResult<(IReadOnlyList<Message>, int, int)>((scoped, scoped.Count, 1));
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
                // Strictly increasing stamps, so a test that marks a newer message then an older
                // one is deterministic rather than dependent on clock resolution.
                _clock = _clock.AddSeconds(1);
                message.CreatedAt = _clock;
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

        public IReadOnlyList<SignOffDecision> AllFor(Guid messageId)
            => _decisions.Where(d => d.MessageId == messageId).ToList();

        public Task<SignOffDecision?> GetByMessageIdAsync(Guid messageId, CancellationToken ct)
            => Task.FromResult(_decisions.LastOrDefault(d => d.MessageId == messageId));

        public Task SaveAsync(SignOffDecision decision, CancellationToken ct)
        {
            _decisions.Add(decision);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeMessageAttachmentRepository : IMessageAttachmentRepository
    {
        private readonly List<MessageAttachment> _rows = [];

        public IReadOnlyList<MessageAttachment> All => _rows;

        public void Seed(MessageAttachment attachment) => _rows.Add(attachment);

        public Task<MessageAttachment?> GetAsync(Guid organizationId, Guid conversationId, Guid attachmentId, CancellationToken ct)
            => Task.FromResult(_rows.FirstOrDefault(a =>
                a.OrganizationId == organizationId && a.ConversationId == conversationId && a.Id == attachmentId));

        public Task<IReadOnlyList<MessageAttachment>> GetBindableAsync(
            Guid organizationId, Guid conversationId, IReadOnlyCollection<Guid> attachmentIds, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MessageAttachment>>(_rows
                .Where(a => a.OrganizationId == organizationId
                            && a.ConversationId == conversationId
                            && a.MessageId == null
                            && attachmentIds.Contains(a.Id))
                .ToList());

        public Task SaveAsync(MessageAttachment attachment, CancellationToken ct)
        {
            if (!_rows.Contains(attachment))
            {
                _rows.Add(attachment);
            }
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<MessageAttachment>> ListOrphansAsync(DateTime cutoff, CancellationToken ct)
            => Task.FromResult<IReadOnlyList<MessageAttachment>>(
                _rows.Where(a => a.MessageId == null && a.CreatedAtUtc < cutoff).ToList());

        public Task<int> DeleteOrphansAsync(DateTime cutoff, CancellationToken ct)
        {
            var removed = _rows.RemoveAll(a => a.MessageId == null && a.CreatedAtUtc < cutoff);
            return Task.FromResult(removed);
        }
    }

    /// <summary>The store boundary, faked: bytes in the row, a URL to the authenticated route.</summary>
    private sealed class FakeAttachmentStore(FakeMessageAttachmentRepository repository) : IAttachmentStore
    {
        public string Provider => "database";

        public async Task<MessageAttachment> StoreAsync(AttachmentStoreRequest request, CancellationToken ct)
        {
            var attachment = new MessageAttachment
            {
                OrganizationId = request.OrganizationId,
                ConversationId = request.ConversationId,
                UploadedByUserId = request.UploadedByUserId,
                StorageProvider = Provider,
                ImageData = request.Bytes,
                ContentType = request.ContentType,
                FileName = request.FileName,
                SizeBytes = request.Bytes.LongLength,
                Width = request.Width,
                Height = request.Height,
                CreatedAtUtc = DateTime.UtcNow,
            };
            attachment.StorageKey = attachment.Id.ToString();
            attachment.Url = $"/api/v1/orgs/{request.OrganizationId}/conversations/{request.ConversationId}/attachments/{attachment.Id}";
            // The database adapter writes the row itself; the fake has to register it so the
            // (faked) repository can find it, or binding would never see it.
            await repository.SaveAsync(attachment, ct);
            return attachment;
        }

        public Task<Stream?> OpenReadAsync(MessageAttachment attachment, CancellationToken ct)
            => Task.FromResult<Stream?>(attachment.ImageData is null
                ? null
                : new MemoryStream(attachment.ImageData, writable: false));

        public Task DeleteAsync(MessageAttachment attachment, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class FakeConversationReadStateRepository : IConversationReadStateRepository
    {
        private readonly List<ConversationReadState> _states = [];
        public int SaveCount { get; private set; }

        public IReadOnlyList<ConversationReadState> All => _states;

        public Task<ConversationReadState?> GetAsync(
            Guid organizationId, Guid userId, Guid conversationId, CancellationToken ct)
            => Task.FromResult(_states.FirstOrDefault(s =>
                s.OrganizationId == organizationId
                && s.UserId == userId
                && s.ConversationId == conversationId));

        public Task SaveAsync(ConversationReadState state, CancellationToken ct)
        {
            SaveCount++;
            var existing = _states.FirstOrDefault(s => s.Id == state.Id);
            if (existing is null)
            {
                _states.Add(state);
            }
            else
            {
                existing.LastReadMessageId = state.LastReadMessageId;
                existing.LastReadAtUtc = state.LastReadAtUtc;
            }
            return Task.CompletedTask;
        }
    }

    private readonly FakeConversationRepository _conversations = new();
    private readonly FakeMessageRepository _messages = new();
    private readonly FakeAgentClient _agent = new();
    private readonly FakeSignOffDecisionRepository _signOffDecisions = new();
    private readonly FakeConversationReadStateRepository _readStates = new();
    private readonly FakeMessageAttachmentRepository _attachments = new();
    private readonly FakeAttachmentStore _attachmentStore;
    private readonly ConversationService _sut;

    public ConversationServiceTests()
    {
        _attachmentStore = new FakeAttachmentStore(_attachments);
        _sut = new ConversationService(
            _conversations, _messages, _signOffDecisions, _readStates,
            _attachments, _attachmentStore, _agent, new CapturingLogger<ConversationService>());
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

        var (items, _, _) = await _messages.ListAsync(salon.Id, 1, 50, null, CancellationToken.None);
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
        var userId = Guid.NewGuid();
        await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);

        await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);

        var (items, _, _) = await _messages.ListAsync(
            (await _conversations.ListAsync(orgId, userId, 1, 50, CancellationToken.None)).Items.Single().Conversation.Id,
            1, 50, null, CancellationToken.None);
        Assert.Single(items);
    }

    [Fact]
    public async Task SendStaffNoteAsync_PersistsNote_AndTriggersAgent()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);

        var message = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "Does anything match Michael's request?", null, null, CancellationToken.None);

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
            _sut.SendStaffNoteAsync(orgId, Guid.NewGuid(), salon.Id, "hello", null, null, CancellationToken.None));
    }

    [Fact]
    public async Task SendStaffNoteAsync_ReplaysOneClientMessageId_WithoutASecondRowOrTrigger()
    {
        // The window between "row persisted" and "client gave up" is wide: the service does
        // its database work and then calls the agent. A retry after a timeout must land on the
        // stored row rather than write the sentence twice and brief the agent twice.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var key = Guid.NewGuid();

        var first = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "On my way.", key, null, CancellationToken.None);
        var second = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "On my way.", key, null, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("On my way.", second.ContentBlocks.EnumerateArray().First().GetProperty("text").GetString());
        // The greeting save + exactly one staff note.
        Assert.Equal(2, _messages.SaveCount);
        Assert.Equal(1, _agent.PostCount);
    }

    [Fact]
    public async Task SendStaffNoteAsync_RejectsAReusedKeyWithDifferentText()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var key = Guid.NewGuid();
        await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "On my way.", key, null, CancellationToken.None);

        await Assert.ThrowsAsync<MessageIdempotencyConflictException>(() =>
            _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "Something else entirely.", key, null, CancellationToken.None));

        Assert.Equal(2, _messages.SaveCount);
        Assert.Equal(1, _agent.PostCount);
    }

    [Fact]
    public async Task SendStaffNoteAsync_WithoutAKey_KeepsWritingEveryDistinctNote()
    {
        // The key is optional: a caller that does not send one keeps today's behaviour.
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);

        var first = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "Same words.", null, null, CancellationToken.None);
        var second = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "Same words.", null, null, CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.Equal(2, _agent.PostCount);
    }

    [Fact]
    public async Task SendStaffNoteAsync_StoresTheClientMessageId()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var key = Guid.NewGuid();

        var message = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "On my way.", key, null, CancellationToken.None);

        var stored = await _messages.GetAsync(salon.Id, message.Id, CancellationToken.None);
        Assert.Equal(key, stored!.ClientMessageId);
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

    private async Task<(Guid orgId, Guid userId, Guid conversationId, Guid messageId, string contentHash)> SeedSignOffAsync()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var evt = new AgentMessageEvent(
            salon.Id,
            salon.ThreadId,
            AgentKeys.Lina,
            MessageKind.SignOff,
            JsonSerializer.SerializeToElement(new[] { new { type = "sign_off", approvalId = "a-1", amount = 48000 } }),
            null,
            Guid.NewGuid());
        var message = await _sut.ApplyAgentMessageAsync(evt, CancellationToken.None);
        // The write path stages a SignOff itself now; nothing is simulated by hand.
        var stored = await _messages.GetAsync(salon.Id, message.Id, CancellationToken.None);
        return (orgId, userId, salon.Id, message.Id, stored!.ContentHash!);
    }

    [Fact]
    public async Task ApplyAgentMessageAsync_SignOff_StagesTheMessageAndPausesTheConversation()
    {
        // This is the whole point of building the marker infra before the producer: the two
        // inputs the `approval` marker reads, and the guard DecideSignOffAsync uses, are set by
        // the write path rather than faked.
        var (orgId, _, conversationId, messageId, _) = await SeedSignOffAsync();

        var stored = await _messages.GetAsync(conversationId, messageId, CancellationToken.None);
        Assert.Equal(MessageStatus.AwaitingSignOff, stored!.Status);

        var conversation = await _conversations.GetAsync(orgId, conversationId, CancellationToken.None);
        Assert.Equal(ConversationStatus.AwaitingSignOff, conversation!.Status);
    }

    [Fact]
    public async Task ApplyAgentMessageAsync_Note_StaysPublishedAndLeavesTheConversationActive()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), null, CancellationToken.None);

        var message = await _sut.ApplyAgentMessageAsync(new AgentMessageEvent(
            salon.Id,
            salon.ThreadId,
            AgentKeys.Ava,
            MessageKind.Note,
            JsonSerializer.SerializeToElement(new[] { new { type = "text", text = "A note" } }),
            null,
            Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(MessageStatus.Published, message.Status);
        var conversation = await _conversations.GetAsync(orgId, salon.Id, CancellationToken.None);
        Assert.Equal(ConversationStatus.Active, conversation!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_Approve_PublishesMessage_AndActivatesConversation()
    {
        var (orgId, userId, conversationId, messageId, contentHash) = await SeedSignOffAsync();

        var decided = await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, contentHash, CancellationToken.None);

        Assert.Equal(MessageStatus.Published, decided.Status);
        var conversation = await _conversations.GetAsync(orgId, conversationId, CancellationToken.None);
        Assert.Equal(ConversationStatus.Active, conversation!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_Reject_CancelsMessage_AndResolvesConversation()
    {
        var (orgId, userId, conversationId, messageId, contentHash) = await SeedSignOffAsync();

        var decided = await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, false, contentHash, CancellationToken.None);

        Assert.Equal(MessageStatus.Cancelled, decided.Status);
        var conversation = await _conversations.GetAsync(orgId, conversationId, CancellationToken.None);
        Assert.Equal(ConversationStatus.Resolved, conversation!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_Throws_WhenMessageIsNotSignOff()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var note = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "hello", null, null, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DecideSignOffAsync(orgId, userId, salon.Id, note.Id, true, "hash", CancellationToken.None));
    }

    [Fact]
    public async Task DecideSignOffAsync_Throws_WhenContentHashDoesNotMatch()
    {
        var (orgId, userId, conversationId, messageId, _) = await SeedSignOffAsync();

        // A stale/wrong hash means the payload changed after display; the decision is void.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, "stale-hash", CancellationToken.None));

        // The message must remain awaiting a decision.
        var stored = await _messages.GetAsync(conversationId, messageId, CancellationToken.None);
        Assert.Equal(MessageStatus.AwaitingSignOff, stored!.Status);
    }

    [Fact]
    public async Task DecideSignOffAsync_RecordsDecisionOutOfBand()
    {
        var (orgId, userId, conversationId, messageId, contentHash) = await SeedSignOffAsync();

        await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, contentHash, CancellationToken.None);

        var decision = await _signOffDecisions.GetByMessageIdAsync(messageId, CancellationToken.None);
        Assert.NotNull(decision);
        Assert.Equal(messageId, decision!.MessageId);
        Assert.Equal(contentHash, decision.ContentHash);
        Assert.Equal(SignOffDecisionKind.Approved, decision.Kind);
        Assert.Equal(userId, decision.DecidedBy);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_CreatesSalon_AndClientMessage()
    {
        var orgId = Guid.NewGuid();

        var message = await _sut.RecordInboundClientMessageAsync(orgId, "+94771234567", "+94771234567", "Do you have this in blue?", null, null, CancellationToken.None);

        Assert.Equal(MessageKind.ClientMessage, message.Kind);
        Assert.Equal("System", message.AuthorKind);
        Assert.Equal(1, _messages.SaveCount);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_BindsTheResolvedCustomer_OnTheThread()
    {
        // D1: the thread exists for the identified customer, so the caller's phone lookup is
        // bound at creation rather than left to the agent to attempt later.
        var orgId = Guid.NewGuid();
        var customerId = Guid.NewGuid();

        await _sut.RecordInboundClientMessageAsync(orgId, "+94771234567", "+94771234567", "Hello", customerId, null, CancellationToken.None);

        var (rows, _) = await _conversations.ListAsync(orgId, Guid.NewGuid(), 1, 50, CancellationToken.None);
        var conversation = Assert.Single(rows).Conversation;
        Assert.Equal(customerId, conversation.CustomerId);
        Assert.Equal("+94771234567", conversation.ExternalRef);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_LeavesAnUnknownCustomerUnbound_ButIdentifiable()
    {
        // A phone that is not on file is a rendered state, not a silent one: the thread keeps its
        // external ref so the client can show it as an unnamed client rather than pinning it.
        var orgId = Guid.NewGuid();

        await _sut.RecordInboundClientMessageAsync(orgId, "+94770000000", "+94770000000", "Hello", null, null, CancellationToken.None);

        var (rows, _) = await _conversations.ListAsync(orgId, Guid.NewGuid(), 1, 50, CancellationToken.None);
        var conversation = Assert.Single(rows).Conversation;
        Assert.Null(conversation.CustomerId);
        Assert.Equal("+94770000000", conversation.ExternalRef);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_ReusesExistingSalon_ForSameExternalRef()
    {
        var orgId = Guid.NewGuid();
        const string number = "+94771234567";

        await _sut.RecordInboundClientMessageAsync(orgId, number, number, "first", null, null, CancellationToken.None);
        await _sut.RecordInboundClientMessageAsync(orgId, number, number, "second", null, null, CancellationToken.None);

        Assert.Equal(2, _messages.SaveCount);
        var (items, _) = await _conversations.ListAsync(orgId, Guid.NewGuid(), 1, 50, CancellationToken.None);
        Assert.Single(items);
    }

    [Fact]
    public async Task RecordInboundClientMessageAsync_TriggersInboundDraft_WithPhoneContext()
    {
        var orgId = Guid.NewGuid();
        const string from = "+94771234567";

        await _sut.RecordInboundClientMessageAsync(orgId, from, from, "Do you have this in blue?", null, null, CancellationToken.None);

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
    public async Task RevokeSignOffAsync_AppendsARevokedRowAndReturnsTheSignOffToAwaiting()
    {
        var (orgId, userId, conversationId, messageId, contentHash) = await SeedSignOffAsync();
        await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, contentHash, CancellationToken.None);

        var revoked = await _sut.RevokeSignOffAsync(orgId, userId, conversationId, messageId, "customer changed their mind", CancellationToken.None);

        Assert.Equal(MessageStatus.AwaitingSignOff, revoked.Status);
        var message = await _messages.GetAsync(conversationId, messageId, CancellationToken.None);
        Assert.Equal(MessageStatus.AwaitingSignOff, message!.Status);
        // The hash is retained, so the same payload is decidable again.
        Assert.Equal(contentHash, message.ContentHash);
        var conversation = await _conversations.GetAsync(orgId, conversationId, CancellationToken.None);
        Assert.Equal(ConversationStatus.AwaitingSignOff, conversation!.Status);

        var newest = await _signOffDecisions.GetByMessageIdAsync(messageId, CancellationToken.None);
        Assert.Equal(SignOffDecisionKind.Revoked, newest!.Kind);
    }

    [Fact]
    public async Task RevokeSignOffAsync_LeavesTheOriginalApprovalRowUntouched()
    {
        var (orgId, userId, conversationId, messageId, contentHash) = await SeedSignOffAsync();
        var approved = await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, contentHash, CancellationToken.None);
        Assert.Equal(MessageStatus.Published, approved.Status);

        await _sut.RevokeSignOffAsync(orgId, userId, conversationId, messageId, null, CancellationToken.None);

        // The log is append-only: the approval row is still there, and the newest row is the
        // revocation. A re-approval is then an ordinary decide.
        var decisions = _signOffDecisions.AllFor(messageId);
        Assert.Equal(2, decisions.Count);
        Assert.Equal(SignOffDecisionKind.Approved, decisions[0].Kind);
        Assert.Equal(SignOffDecisionKind.Revoked, decisions[1].Kind);

        var reapproved = await _sut.DecideSignOffAsync(orgId, userId, conversationId, messageId, true, contentHash, CancellationToken.None);
        Assert.Equal(MessageStatus.Published, reapproved.Status);
    }

    [Fact]
    public async Task RevokeSignOffAsync_RefusesASignOffThatIsNotApproved()
    {
        var (orgId, userId, conversationId, messageId, _) = await SeedSignOffAsync();

        // Still awaiting a decision, so there is no approval to take back.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _sut.RevokeSignOffAsync(orgId, userId, conversationId, messageId, null, CancellationToken.None));
    }

    [Fact]
    public async Task RevokeSignOffAsync_ReportsAnUnknownMessage()
    {
        var (orgId, userId, conversationId, _, _) = await SeedSignOffAsync();

        await Assert.ThrowsAsync<SignOffNotFoundException>(() =>
            _sut.RevokeSignOffAsync(orgId, userId, conversationId, Guid.NewGuid(), null, CancellationToken.None));
    }

    [Fact]
    public async Task ApplyAgentMessageUpdateAsync_RecomputesContentHash_ForSignOff()
    {
        var (_, _, conversationId, messageId, originalHash) = await SeedSignOffAsync();

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

    [Fact]
    public async Task CreateAttachmentAsync_StoresAnUnboundRowForAVisibleConversation()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);

        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Id, [1, 2, 3], "image/png", "p.png", 10, 20, CancellationToken.None);

        Assert.NotNull(attachment);
        Assert.Equal("database", attachment!.StorageProvider);
        Assert.Null(attachment.MessageId);
        Assert.Equal(10, attachment.Width);
        Assert.Equal(3, attachment.SizeBytes);
    }

    [Fact]
    public async Task CreateAttachmentAsync_ReturnsNullForAnInvisibleConversation()
    {
        var salon = await _sut.GetOrCreateSalonAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);

        var attachment = await _sut.CreateAttachmentAsync(
            Guid.NewGuid(), Guid.NewGuid(), salon.Id, [1], "image/png", "p.png", null, null, CancellationToken.None);

        Assert.Null(attachment);
    }

    [Fact]
    public async Task SendStaffNoteAsync_BindsTheNamedAttachmentsAndCarriesTheirBlocks()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Id, [1, 2, 3], "image/png", "photo.png", 800, 600, CancellationToken.None);

        var message = await _sut.SendStaffNoteAsync(
            orgId, userId, salon.Id, "Here it is.", null, [attachment!.Id], CancellationToken.None);

        // The text block first, then the attachment block.
        Assert.Equal("text", message.ContentBlocks[0].GetProperty("type").GetString());
        Assert.Equal("Here it is.", message.ContentBlocks[0].GetProperty("text").GetString());
        Assert.Equal("attachment", message.ContentBlocks[1].GetProperty("type").GetString());
        Assert.Equal(attachment.Id, message.ContentBlocks[1].GetProperty("attachmentId").GetGuid());
        Assert.Equal(800, message.ContentBlocks[1].GetProperty("width").GetInt32());

        // Bound to exactly this message.
        var stored = Assert.Single(_attachments.All);
        Assert.Equal(message.Id, stored.MessageId);
        Assert.NotNull(stored.BoundAtUtc);
    }

    [Fact]
    public async Task SendStaffNoteAsync_RefusesAForeignOrAlreadyBoundAttachment()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Id, [1], "image/png", "photo.png", null, null, CancellationToken.None);
        await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "First.", null, [attachment!.Id], CancellationToken.None);

        // Already bound by the first send.
        await Assert.ThrowsAsync<AttachmentBindingException>(() => _sut.SendStaffNoteAsync(
            orgId, userId, salon.Id, "Second.", null, [attachment.Id], CancellationToken.None));

        // Unknown id.
        await Assert.ThrowsAsync<AttachmentBindingException>(() => _sut.SendStaffNoteAsync(
            orgId, userId, salon.Id, "Third.", null, [Guid.NewGuid()], CancellationToken.None));
    }

    [Fact]
    public async Task SendStaffNoteAsync_RefusesMoreAttachmentsThanTheCap()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var ids = new List<Guid>();
        for (var i = 0; i < 6; i++)
        {
            var attachment = await _sut.CreateAttachmentAsync(
                orgId, userId, salon.Id, [1], "image/png", $"photo{i}.png", null, null, CancellationToken.None);
            ids.Add(attachment!.Id);
        }

        await Assert.ThrowsAsync<AttachmentBindingException>(() => _sut.SendStaffNoteAsync(
            orgId, userId, salon.Id, "Too many.", null, ids, CancellationToken.None));
    }

    [Fact]
    public async Task SendStaffNoteAsync_ReplaysWithItsAttachments_WithoutASecondRow()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Id, [1], "image/png", "photo.png", null, null, CancellationToken.None);
        var key = Guid.NewGuid();

        var first = await _sut.SendStaffNoteAsync(
            orgId, userId, salon.Id, "Here it is.", key, [attachment!.Id], CancellationToken.None);
        // The retry names the same key and the same words: it replays rather than conflicting,
        // even though the attachment is now bound.
        var second = await _sut.SendStaffNoteAsync(
            orgId, userId, salon.Id, "Here it is.", key, null, CancellationToken.None);

        Assert.Equal(first.Id, second.Id);
        Assert.Equal("attachment", second.ContentBlocks[1].GetProperty("type").GetString());
        Assert.Equal(1, _agent.PostCount);
    }

    [Fact]
    public async Task GetAttachmentAsync_RequiresAVisibleConversation()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var attachment = await _sut.CreateAttachmentAsync(
            orgId, userId, salon.Id, [1], "image/png", "photo.png", null, null, CancellationToken.None);

        var found = await _sut.GetAttachmentAsync(orgId, userId, salon.Id, attachment!.Id, CancellationToken.None);
        var invisible = await _sut.GetAttachmentAsync(Guid.NewGuid(), userId, salon.Id, attachment.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Null(invisible);
    }

    [Fact]
    public async Task MarkReadAsync_RecordsTheCallersMarker()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var note = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "hi", null, null, CancellationToken.None);

        var outcome = await _sut.MarkReadAsync(orgId, userId, salon.Id, note.Id, CancellationToken.None);

        Assert.Equal(MarkReadOutcome.Recorded, outcome);
        var state = Assert.Single(_readStates.All);
        Assert.Equal(orgId, state.OrganizationId);
        Assert.Equal(userId, state.UserId);
        Assert.Equal(salon.Id, state.ConversationId);
        Assert.Equal(note.Id, state.LastReadMessageId);
    }

    [Fact]
    public async Task MarkReadAsync_AdvancesButNeverRegresses()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var older = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "first", null, null, CancellationToken.None);
        var newer = await _sut.SendStaffNoteAsync(orgId, userId, salon.Id, "second", null, null, CancellationToken.None);

        var advanced = await _sut.MarkReadAsync(orgId, userId, salon.Id, newer.Id, CancellationToken.None);
        // A second device re-opening an older position must not un-read what the first read.
        var regressed = await _sut.MarkReadAsync(orgId, userId, salon.Id, older.Id, CancellationToken.None);

        Assert.Equal(MarkReadOutcome.Recorded, advanced);
        Assert.Equal(MarkReadOutcome.Ignored, regressed);
        Assert.Equal(newer.Id, Assert.Single(_readStates.All).LastReadMessageId);
        Assert.Equal(1, _readStates.SaveCount);
    }

    [Fact]
    public async Task MarkReadAsync_IsPerUserOnASharedThread()
    {
        var orgId = Guid.NewGuid();
        var colleague = Guid.NewGuid();
        var me = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, colleague, Guid.NewGuid(), CancellationToken.None);
        var note = await _sut.SendStaffNoteAsync(orgId, colleague, salon.Id, "hi", null, null, CancellationToken.None);

        await _sut.MarkReadAsync(orgId, colleague, salon.Id, note.Id, CancellationToken.None);
        await _sut.MarkReadAsync(orgId, me, salon.Id, note.Id, CancellationToken.None);

        Assert.Equal(2, _readStates.All.Count);
        Assert.Equal(2, _readStates.All.Select(s => s.UserId).Distinct().Count());
    }

    [Fact]
    public async Task MarkReadAsync_RejectsAMessageFromAnotherConversation()
    {
        var orgId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(orgId, userId, null, CancellationToken.None);
        var other = await _sut.GetOrCreateSalonAsync(orgId, Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None);
        var foreign = await _sut.SendStaffNoteAsync(orgId, userId, other.Id, "elsewhere", null, null, CancellationToken.None);

        var outcome = await _sut.MarkReadAsync(orgId, userId, salon.Id, foreign.Id, CancellationToken.None);

        Assert.Equal(MarkReadOutcome.MessageNotFound, outcome);
        Assert.Empty(_readStates.All);
    }

    [Fact]
    public async Task MarkReadAsync_ReportsAnInvisibleConversation()
    {
        var orgId = Guid.NewGuid();
        var salon = await _sut.GetOrCreateSalonAsync(Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None);

        var outcome = await _sut.MarkReadAsync(orgId, Guid.NewGuid(), salon.Id, Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(MarkReadOutcome.ConversationNotFound, outcome);
        Assert.Empty(_readStates.All);
    }
}
