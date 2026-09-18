using System.Text.Json;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Registers handlers on the <see cref="IEventBus"/> for the conversation event types
/// (<c>message.created</c>, <c>message.updated</c>, <c>conversation.created</c>,
/// <c>agent.status</c>). When an agent publishes a content event, the handler persists it via
/// <see cref="IConversationService.ApplyAgentMessageAsync"/> and broadcasts it. When an agent
/// publishes a lifecycle state, the handler resolves the conversation from its thread id and
/// broadcasts the state. This is where the API becomes the system of record for messages.
///
/// <para>
/// Works with both the Redis bus (handlers are invoked by <c>RedisSubscriptionService</c>)
/// and the in-memory fallback used by the test suite. Scoped services are resolved per event
/// via <see cref="IServiceScopeFactory"/> because this hosted service is a singleton.
/// </para>
/// </summary>
public sealed class ConversationEventSubscriber : IHostedService
{
    private readonly IEventBus _bus;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ConversationEventSubscriber> _logger;

    public ConversationEventSubscriber(
        IEventBus bus,
        IServiceScopeFactory scopeFactory,
        ILogger<ConversationEventSubscriber> logger)
    {
        _bus = bus;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    /// <summary>The event types this subscriber registers handlers for.</summary>
    public IReadOnlyCollection<string> RegisteredEventTypes { get; } =
    [
        ConversationEvents.MessageCreated,
        ConversationEvents.MessageUpdated,
        ConversationEvents.ConversationCreated,
        ConversationEvents.AgentStatus,
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _bus.SubscribeAsync(ConversationEvents.MessageCreated, OnMessageCreatedAsync, cancellationToken);
        await _bus.SubscribeAsync(ConversationEvents.MessageUpdated, OnMessageUpdatedAsync, cancellationToken);
        await _bus.SubscribeAsync(ConversationEvents.ConversationCreated, OnConversationCreatedAsync, cancellationToken);
        await _bus.SubscribeAsync(ConversationEvents.AgentStatus, OnAgentStatusAsync, cancellationToken);
        _logger.LogInformation("Conversation event subscriber registered handlers for {Count} event types.", RegisteredEventTypes.Count);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    private async Task OnMessageCreatedAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var evt = ParseMessageEvent(envelope);
        if (evt is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<IMessageBroadcaster>();

        var message = await conversations.ApplyAgentMessageAsync(evt, cancellationToken);
        await broadcaster.BroadcastMessageAsync(message, cancellationToken);
    }

    private async Task OnAgentStatusAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var (threadId, state, agentKey, traceId) = ParseAgentStatusEvent(envelope);
        if (threadId is null || state is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationRepository>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<IMessageBroadcaster>();

        var conversation = await conversations.GetByThreadIdAsync(threadId, cancellationToken);
        if (conversation is null)
        {
            // The state event may arrive before the conversation is known to this instance;
            // drop it rather than fail the listener.
            _logger.LogDebug("Agent status for unknown thread {ThreadId} skipped.", threadId);
            return;
        }

        await broadcaster.BroadcastAgentStateAsync(
            new AgentStateDto(conversation.Id, state, agentKey, traceId),
            cancellationToken);
    }

    private async Task OnMessageUpdatedAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        var evt = ParseMessageUpdateEvent(envelope);
        if (evt is null)
        {
            return;
        }

        using var scope = _scopeFactory.CreateScope();
        var conversations = scope.ServiceProvider.GetRequiredService<IConversationService>();
        var broadcaster = scope.ServiceProvider.GetRequiredService<IMessageBroadcaster>();

        var updated = await conversations.ApplyAgentMessageUpdateAsync(evt, cancellationToken);
        if (updated is null)
        {
            // The message does not exist (or was for a conversation this instance does not
            // know yet); skip rather than fail the listener.
            _logger.LogDebug(
                "message.updated for unknown message {MessageId} skipped.",
                evt.MessageId);
            return;
        }

        await broadcaster.BroadcastMessageAsync(updated, cancellationToken);
    }

    private Task OnConversationCreatedAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        _logger.LogDebug("Received conversation.created event (eventId={EventId}).", envelope.EventId);
        return Task.CompletedTask;
    }

    private static AgentMessageEvent? ParseMessageEvent(EventEnvelope envelope)
    {
        JsonElement payload;
        if (envelope.Payload is JsonElement element)
        {
            payload = element;
        }
        else if (envelope.Payload is not null)
        {
            // In-memory bus passes the raw object; normalize to a JsonElement so both the
            // Redis and in-memory paths parse identically.
            try
            {
                payload = JsonSerializer.SerializeToElement(envelope.Payload);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        try
        {
            var threadId = payload.GetProperty("thread_id").GetString() ?? string.Empty;
            var agentKey = payload.TryGetProperty("author", out var author)
                && author.TryGetProperty("agent_key", out var key)
                ? key.GetString()
                : null;
            var kind = payload.TryGetProperty("kind", out var kindProp)
                && Enum.TryParse<MessageKind>(kindProp.GetString(), ignoreCase: true, out var parsedKind)
                ? parsedKind
                : MessageKind.Note;
            var blocks = payload.TryGetProperty("blocks", out var blocksProp)
                ? blocksProp
                : default;
            var replyTo = payload.TryGetProperty("reply_to_message_id", out var replyProp)
                && replyProp.ValueKind == JsonValueKind.String
                ? replyProp.GetGuid()
                : (Guid?)null;
            var workflowRunId = payload.TryGetProperty("workflow_run_id", out var runProp)
                && runProp.ValueKind == JsonValueKind.String
                ? runProp.GetGuid()
                : (Guid?)null;

            return new AgentMessageEvent(
                Guid.Empty,
                threadId,
                agentKey ?? AgentKeys.Aveline,
                kind,
                blocks,
                replyTo,
                workflowRunId);
        }
        catch (JsonException)
        {
            // Malformed payload; log and skip rather than crash the listener.
            return null;
        }
    }

    /// <summary>
    /// Parses a <c>message.updated</c> payload into an <see cref="AgentMessageUpdateEvent"/>.
    /// Requires <c>conversation_id</c> and <c>message_id</c>; <c>status</c> and <c>blocks</c> are
    /// optional. Returns <c>null</c> when the payload is malformed or missing required fields.
    /// </summary>
    private static AgentMessageUpdateEvent? ParseMessageUpdateEvent(EventEnvelope envelope)
    {
        JsonElement payload;
        if (envelope.Payload is JsonElement element)
        {
            payload = element;
        }
        else if (envelope.Payload is not null)
        {
            try
            {
                payload = JsonSerializer.SerializeToElement(envelope.Payload);
            }
            catch (JsonException)
            {
                return null;
            }
        }
        else
        {
            return null;
        }

        try
        {
            if (!payload.TryGetProperty("conversation_id", out var conversationProp)
                || conversationProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }
            if (!payload.TryGetProperty("message_id", out var messageProp)
                || messageProp.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            var conversationId = conversationProp.GetGuid();
            var messageId = messageProp.GetGuid();

            MessageStatus? status = null;
            if (payload.TryGetProperty("status", out var statusProp)
                && statusProp.ValueKind == JsonValueKind.String
                && Enum.TryParse<MessageStatus>(statusProp.GetString(), ignoreCase: true, out var parsedStatus))
            {
                status = parsedStatus;
            }

            var blocks = payload.TryGetProperty("blocks", out var blocksProp)
                ? blocksProp
                : default;

            return new AgentMessageUpdateEvent(conversationId, messageId, status, blocks);
        }
        catch (JsonException)
        {
            // Malformed payload; log and skip rather than crash the listener.
            return null;
        }
    }

    /// <summary>
    /// Parses an <c>agent.status</c> payload into its parts. Returns <c>null</c> thread/state
    /// when the payload is malformed or missing required fields.
    /// </summary>
    private static (string? ThreadId, string? State, string? AgentKey, Guid? TraceId) ParseAgentStatusEvent(EventEnvelope envelope)
    {
        JsonElement payload;
        if (envelope.Payload is JsonElement element)
        {
            payload = element;
        }
        else if (envelope.Payload is not null)
        {
            try
            {
                payload = JsonSerializer.SerializeToElement(envelope.Payload);
            }
            catch (JsonException)
            {
                return (null, null, null, null);
            }
        }
        else
        {
            return (null, null, null, null);
        }

        try
        {
            var threadId = payload.TryGetProperty("thread_id", out var threadProp)
                ? threadProp.GetString()
                : null;
            var state = payload.TryGetProperty("state", out var stateProp)
                ? stateProp.GetString()
                : null;
            var agentKey = payload.TryGetProperty("agent_key", out var keyProp)
                ? keyProp.GetString()
                : null;
            var traceId = payload.TryGetProperty("trace_id", out var traceProp)
                && traceProp.ValueKind == JsonValueKind.String
                ? traceProp.GetGuid()
                : (Guid?)null;
            return (threadId, state, agentKey, traceId);
        }
        catch (JsonException)
        {
            return (null, null, null, null);
        }
    }
}
