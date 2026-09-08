using System.Text.Json;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Conversations.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Registers handlers on the <see cref="IEventBus"/> for the conversation event types
/// (<c>message.created</c>, <c>message.updated</c>, <c>conversation.created</c>). When an
/// agent publishes a content event, the handler persists it via
/// <see cref="IConversationService.ApplyAgentMessageAsync"/> and broadcasts it. This is where
/// the API becomes the system of record for messages.
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
    ];

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _bus.SubscribeAsync(ConversationEvents.MessageCreated, OnMessageCreatedAsync, cancellationToken);
        await _bus.SubscribeAsync(ConversationEvents.MessageUpdated, OnMessageUpdatedAsync, cancellationToken);
        await _bus.SubscribeAsync(ConversationEvents.ConversationCreated, OnConversationCreatedAsync, cancellationToken);
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

    private Task OnMessageUpdatedAsync(EventEnvelope envelope, CancellationToken cancellationToken)
    {
        // Status/block updates are handled in a later slice (SignOff). Log for now.
        _logger.LogDebug("Received message.updated event (eventId={EventId}).", envelope.EventId);
        return Task.CompletedTask;
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
}
