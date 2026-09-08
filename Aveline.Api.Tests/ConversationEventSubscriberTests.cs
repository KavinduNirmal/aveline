using System.Text.Json;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
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

        public Task<MessageDto> ApplyAgentMessageAsync(Guid orgId, AgentMessageEvent evt, CancellationToken cancellationToken = default)
        {
            Applied.Add(evt);
            return Task.FromResult(new MessageDto(
                Guid.NewGuid(), evt.ConversationId, "Agent", evt.AgentKey, null, evt.Kind,
                evt.ContentBlocks, evt.ReplyToMessageId, MessageStatus.Published, DateTime.UtcNow));
        }
    }

    private sealed class FakeBroadcaster : IMessageBroadcaster
    {
        public List<MessageDto> Broadcast { get; } = [];

        public Task BroadcastMessageAsync(MessageDto message, CancellationToken cancellationToken = default)
        {
            Broadcast.Add(message);
            return Task.CompletedTask;
        }
    }

    private static ConversationEventSubscriber CreateSubscriber(IEventBus bus, FakeConversationService service, FakeBroadcaster broadcaster)
    {
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        services.AddSingleton<IConversationService>(service);
        services.AddSingleton<IMessageBroadcaster>(broadcaster);
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

        Assert.Equal(3, subscriber.RegisteredEventTypes.Count);
        Assert.Contains(ConversationEvents.MessageCreated, subscriber.RegisteredEventTypes);
        Assert.Contains(ConversationEvents.MessageUpdated, subscriber.RegisteredEventTypes);
        Assert.Contains(ConversationEvents.ConversationCreated, subscriber.RegisteredEventTypes);
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
        var conversationId = Guid.NewGuid();
        var payload = new
        {
            conversation_id = conversationId,
            thread_id = "thread-1",
            author = new { agent_key = AgentKeys.Aveline },
            kind = "Note",
            blocks = new[] { new { type = "text", text = "Hello" } },
            reply_to_message_id = (Guid?)null,
            workflow_run_id = Guid.NewGuid(),
        };

        await bus.PublishAsync(ConversationEvents.MessageCreated, orgId, payload);

        var applied = Assert.Single(service.Applied);
        Assert.Equal(conversationId, applied.ConversationId);
        Assert.Equal(AgentKeys.Aveline, applied.AgentKey);
        Assert.Equal(MessageKind.Note, applied.Kind);
        Assert.Single(broadcaster.Broadcast);
    }
}
