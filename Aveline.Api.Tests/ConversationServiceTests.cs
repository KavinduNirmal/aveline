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

        public Task<(IReadOnlyList<Conversation> Items, int Total)> ListAsync(Guid orgId, int page, int pageSize, CancellationToken ct)
        {
            var scoped = _conversations.Where(c => c.OrganizationId == orgId).OrderByDescending(c => c.LastMessageAt).ToList();
            var items = scoped.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<Conversation>, int)>((items, scoped.Count));
        }

        public Task<Conversation> GetOrCreateSalonAsync(Guid orgId, Guid? customerId, string threadId, CancellationToken ct)
        {
            var existing = _conversations.FirstOrDefault(c => c.OrganizationId == orgId && c.CustomerId == customerId && c.Kind == ConversationKind.Salon);
            if (existing is not null)
            {
                return Task.FromResult(existing);
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
            return Task.FromResult(created);
        }

        public Task SaveAsync(Conversation conversation, CancellationToken ct)
        {
            SaveCount++;
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
            _messages.Add(message);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAgentClient : IAgentServiceClient
    {
        public int PostCount { get; private set; }
        public string? LastPath { get; private set; }

        public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            PostCount++;
            LastPath = path;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
            => Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
    }

    private readonly FakeConversationRepository _conversations = new();
    private readonly FakeMessageRepository _messages = new();
    private readonly FakeAgentClient _agent = new();
    private readonly ConversationService _sut;

    public ConversationServiceTests()
    {
        _sut = new ConversationService(_conversations, _messages, _agent, new CapturingLogger<ConversationService>());
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
        Assert.Equal(1, _messages.SaveCount);
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

        var message = await _sut.ApplyAgentMessageAsync(orgId, evt, CancellationToken.None);

        Assert.Equal("Agent", message.AuthorKind);
        Assert.Equal(AgentKeys.Aveline, message.AgentKey);
        Assert.Equal(MessageKind.Note, message.Kind);
        Assert.Equal(MessageStatus.Published, message.Status);
        Assert.Equal(1, _messages.SaveCount);
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
            _sut.ApplyAgentMessageAsync(Guid.NewGuid(), evt, CancellationToken.None));
    }
}
