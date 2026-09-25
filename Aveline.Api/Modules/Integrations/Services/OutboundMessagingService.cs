using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// The default <see cref="IOutboundMessagingService"/>: a thin dispatcher over the registered
/// <see cref="IOutboundChannel"/>s. It holds no channel-specific logic on purpose — everything that
/// differs between channels lives in the channel, which is what makes a second channel a new class
/// rather than a new flow.
/// </summary>
public sealed class OutboundMessagingService : IOutboundMessagingService, IOutboundChannelRegistry
{
    /// <summary>The WhatsApp channel key; the only channel with a provider today.</summary>
    public const string WhatsAppChannelKey = WhatsAppOutboundChannel.Key;

    private readonly IReadOnlyDictionary<string, IOutboundChannel> _channels;
    private readonly ILogger<OutboundMessagingService> _logger;

    public OutboundMessagingService(
        IEnumerable<IOutboundChannel> channels,
        ILogger<OutboundMessagingService> logger)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(logger);

        _channels = channels.ToDictionary(channel => channel.ChannelKey, StringComparer.Ordinal);
        _logger = logger;
    }

    /// <inheritdoc/>
    public IReadOnlyDictionary<string, IOutboundChannel> Channels => _channels;

    /// <inheritdoc/>
    public IOutboundChannel? Resolve(string channelKey) =>
        _channels.GetValueOrDefault(channelKey);

    /// <inheritdoc/>
    public Task<OutboundMessageResult> SendWhatsAppTextAsync(
        Guid organizationId,
        string toE164,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => DispatchTextAsync(
            WhatsAppChannelKey, organizationId, toE164, text, idempotencyKey, cancellationToken);

    /// <inheritdoc/>
    public Task<OutboundMessageResult> SendWhatsAppTemplateAsync(
        Guid organizationId,
        string toE164,
        string templateName,
        string languageCode,
        IReadOnlyList<object>? components,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
        => DispatchTemplateAsync(
            WhatsAppChannelKey, organizationId, toE164, templateName, languageCode, components,
            idempotencyKey, cancellationToken);

    /// <inheritdoc/>
    public async Task<bool> IsChannelConfiguredAsync(
        Guid organizationId, string channelKey, CancellationToken cancellationToken = default)
    {
        var channel = Resolve(channelKey);
        if (channel is null)
        {
            return false;
        }

        try
        {
            return await channel.IsConfiguredAsync(organizationId, cancellationToken);
        }
        catch (Exception ex)
        {
            // "Can we send?" must answer false rather than throw: a caller deciding whether to
            // attempt a disclosure cannot distinguish a crash from a missing credential anyway,
            // and both mean "do not send".
            _logger.LogError(
                ex,
                "Outbound channel configuration check failed. organizationId={OrganizationId} channel={Channel}",
                organizationId, channelKey);
            return false;
        }
    }

    private Task<OutboundMessageResult> DispatchTextAsync(
        string channelKey,
        Guid organizationId,
        string toE164,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var channel = Resolve(channelKey);
        return channel is null
            ? Task.FromResult(UnknownChannel(channelKey))
            : channel.SendTextAsync(organizationId, toE164, text, idempotencyKey, cancellationToken);
    }

    private Task<OutboundMessageResult> DispatchTemplateAsync(
        string channelKey,
        Guid organizationId,
        string toE164,
        string templateName,
        string languageCode,
        IReadOnlyList<object>? components,
        string idempotencyKey,
        CancellationToken cancellationToken)
    {
        var channel = Resolve(channelKey);
        return channel is null
            ? Task.FromResult(UnknownChannel(channelKey))
            : channel.SendTemplateAsync(
                organizationId, toE164, templateName, languageCode, components, idempotencyKey,
                cancellationToken);
    }

    private OutboundMessageResult UnknownChannel(string channelKey)
    {
        _logger.LogWarning(
            "Outbound message skipped: no channel is registered for the key. channel={Channel}",
            channelKey);
        return OutboundMessageResult.NotConfigured($"No outbound channel is registered for '{channelKey}'.");
    }
}
