using System.Text.Json;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Aveline.Api.Modules.Conversations.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Tests;

public class ConversationEventSubscriberTests
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
        public Dictionary<string, Conversation> ByThread { get; } = [];

        public Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult<Conversation?>(null);

        public Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken cancellationToken = default)
            => Task.FromResult(ByThread.GetValueOrDefault(threadId));

        public Task<(IReadOnlyList<Conversation> Items, int Total)> ListAsync(Guid orgId, int page, int pageSize, CancellationToken cancellationToken = default)
            => Task.FromResult<(IReadOnlyList<Conversation>, int)>(([], 0));

        public Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(Guid orgId, Guid? customerId, string threadId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<Conversation> GetOrCreateSalonByExternalRefAsync(Guid orgId, string externalRef, string threadId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }

    private sealed class FakeConversationService : IConversationService
    {
        public List<AgentMessageEvent> Applied { get; } = [];

        public Task<ConversationDto> GetOrCreateSalonAsync(Guid orgId, Guid userId, Guid? customerId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<ConversationDto?> GetAsync(Guid orgId, Guid conversationId, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<(IReadOnlyList<ConversationDto> Items, int Total)> ListAsync(Guid orgId, int page, int pageSize, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<(IReadOnlyList<MessageDto> Items, int Total)> ListMessagesAsync(Guid orgId, Guid conversationId, int page, int pageSize, Guid? around = null, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<MessageDto> SendStaffNoteAsync(Guid orgId, Guid userId, Guid conversationId, string text, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<MessageDto> ApplyAgentMessageAsync(AgentMessageEvent evt, CancellationToken cancellationToken = default)
        {
            Applied.Add(evt);
            return Task.FromResult(new MessageDto(
                Guid.NewGuid(), evt.ConversationId, "Agent", evt.AgentKey, null, evt.Kind,
                evt.ContentBlocks, null, evt.ReplyToMessageId, MessageStatus.Published, DateTime.UtcNow));
        }

        public Task<MessageDto> DecideSignOffAsync(Guid orgId, Guid userId, Guid conversationId, Guid messageId, bool approved, string contentHash, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();

        public Task<MessageDto> RecordInboundClientMessageAsync(Guid orgId, string externalRef, string from, string text, CancellationToken cancellationToken = default)
            => throw new NotImplementedException();
    }

    private sealed class FakeBroadcaster : IMessageBroadcaster
    {
        public List<MessageDto> Broadcast { get; } = [];
        public List<AgentStateDto> StateBroadcast { get; } = [];

        public Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
        {
            Broadcast.Add(message);
            return Task.CompletedTask;
        }

        public Task BroadcastAgentStateAsync(AgentStateDto state, CancellationToken cancellationToken = default)
        {
            StateBroadcast.Add(state);
            return Task.CompletedTask;
        }
    }

    private static ConversationEventSubscriber CreateSubscriber(
        IEventBus bus,
        FakeConversationService service,
        FakeBroadcaster broadcaster,
        FakeConversationRepository? repository = null)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IConversationService>(service);
        services.AddSingleton<IMessageBroadcaster>(broadcaster);
        services.AddSingleton<IConversationRepository>(repository ?? new FakeConversationRepository());
        var provider = services.BuildServiceProvider();
        return new ConversationEventSubscriber(bus, provider.GetRequiredService<IServiceScopeFactory>(), new CapturingLogger<ConversationEventSubscriber>());
    }

    [Fact]
    public async Task StartAsync_RegistersHandlers_ForConversationEventTypes()
    {
        var bus = new InMemoryEventBus();
        var service = new FakeConversationService();
        var subscriber = CreateSubscriber(bus, service, new FakeBroadcaster());

        await subscriber.StartAsync(CancellationToken.None);

        Assert.Equal(4, subscriber.RegisteredEventTypes.Count);
        Assert.Contains(ConversationEvents.MessageCreated, subscriber.RegisteredEventTypes);
        Assert.Contains(ConversationEvents.MessageUpdated, subscriber.RegisteredEventTypes);
        Assert.Contains(ConversationEvents.ConversationCreated, subscriber.RegisteredEventTypes);
        Assert.Contains(ConversationEvents.AgentStatus, subscriber.RegisteredEventTypes);
    }

    [Fact]
    public async Task MessageCreatedEvent_DispatchesToApplyAgentMessage()
    {
        var bus = new InMemoryEventBus();
        var service = new FakeConversationService();
        var broadcaster = new FakeBroadcaster();
        var subscriber = CreateSubscriber(bus, service, broadcaster);
        await subscriber.StartAsync(CancellationToken.None);

        var orgId = Guid.NewGuid();
        var payload = new
        {
            thread_id = "thread-1",
            author = new { agent_key = AgentKeys.Aveline },
            kind = "Note",
            blocks = new[] { new { type = "text", text = "Hello" } },
            reply_to_message_id = (Guid?)null,
            workflow_run_id = Guid.NewGuid(),
        };

        await bus.PublishAsync(ConversationEvents.MessageCreated, orgId, payload);

        var applied = Assert.Single(service.Applied);
        Assert.Equal("thread-1", applied.ThreadId);
        Assert.Equal(AgentKeys.Aveline, applied.AgentKey);
        Assert.Equal(MessageKind.Note, applied.Kind);
        Assert.Single(broadcaster.Broadcast);
    }

    [Fact]
    public async Task AgentStatusEvent_BroadcastsState_ToResolvedConversation()
    {
        var bus = new InMemoryEventBus();
        var service = new FakeConversationService();
        var broadcaster = new FakeBroadcaster();
        var repository = new FakeConversationRepository();
        var conversationId = Guid.NewGuid();
        repository.ByThread["thread-1"] = new Conversation { Id = conversationId, ThreadId = "thread-1" };
        var subscriber = CreateSubscriber(bus, service, broadcaster, repository);
        await subscriber.StartAsync(CancellationToken.None);

        var orgId = Guid.NewGuid();
        var payload = new
        {
            thread_id = "thread-1",
            state = "searching",
            agent_key = AgentKeys.Aveline,
            trace_id = Guid.NewGuid(),
        };

        await bus.PublishAsync(ConversationEvents.AgentStatus, orgId, payload);

        var state = Assert.Single(broadcaster.StateBroadcast);
        Assert.Equal(conversationId, state.ConversationId);
        Assert.Equal("searching", state.State);
        Assert.Equal(AgentKeys.Aveline, state.AgentKey);
    }

    [Fact]
    public async Task AgentStatusEvent_UnknownThread_IsSkipped()
    {
        var bus = new InMemoryEventBus();
        var service = new FakeConversationService();
        var broadcaster = new FakeBroadcaster();
        var subscriber = CreateSubscriber(bus, service, broadcaster);
        await subscriber.StartAsync(CancellationToken.None);

        var orgId = Guid.NewGuid();
        var payload = new { thread_id = "unknown-thread", state = "thinking" };

        await bus.PublishAsync(ConversationEvents.AgentStatus, orgId, payload);

        Assert.Empty(broadcaster.StateBroadcast);
    }
}
