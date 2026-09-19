using System.Globalization;
using System.Text.Json;
using Aveline.Api.Modules.Conversations.DTOs;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Conversations.Repositories;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Derives the inbox tile from a conversation row: the block-aware preview, the row's category
/// (the block the preview came from) and the actionable marker set.
/// </summary>
/// <remarks>
/// <para>
/// Agent output is published with <c>kind: "Note"</c> for every persona, so the row's category
/// lives in the message's <em>content blocks</em>, not in its kind. A preview rule that looked
/// for a block <em>of type</em> <c>text</c> would skip an inbound <c>client_message</c> block and
/// return nothing for a block-only message, so the mapping is explicit per block type.
/// </para>
/// <para>
/// One mapper serves the list endpoint and the realtime broadcast, so a tile that arrives over
/// the hub is the same tile a re-read returns.
/// </para>
/// </remarks>
public static class ConversationTileMapper
{
    /// <summary>How long a preview may be before it is cut off.</summary>
    public const int PreviewMaxLength = 120;

    /// <summary>The closed marker vocabulary, in the order the row renders it.</summary>
    public static readonly IReadOnlyList<string> MarkerPriority = ["approval", "choice", "draft"];

    /// <summary>Builds the wire row for one conversation.</summary>
    public static ConversationDto ToDto(ConversationListRow row)
    {
        var conversation = row.Conversation;
        var (preview, block) = BuildPreview(row.LastMessage);

        return new ConversationDto(
            conversation.Id,
            conversation.Kind.ToString(),
            conversation.CustomerId,
            row.CustomerName,
            conversation.ExternalRef,
            conversation.ThreadId,
            conversation.Status.ToString(),
            conversation.LastMessageAt,
            preview,
            row.LastMessage?.Kind.ToString(),
            block,
            AuthorFor(row.LastMessage),
            row.LastMessage?.AuthorAgentKey,
            BuildMarkers(row.LastMessage, row.HasPendingSignOff));
    }

    /// <summary>
    /// The last author in the client's vocabulary. Inbound customer content is stored as
    /// <see cref="AuthorKind.System"/>, so it is reported as the customer rather than the platform.
    /// </summary>
    public static string? AuthorFor(Message? message) => message?.AuthorKind switch
    {
        AuthorKind.User => "Staff",
        AuthorKind.Agent => "Agent",
        AuthorKind.System => "Customer",
        _ => null,
    };

    /// <summary>
    /// The preview and the block type it was derived from. Both are <c>null</c> when the message
    /// carries no block the inbox knows how to summarise.
    /// </summary>
    public static (string? Preview, string? Block) BuildPreview(Message? message)
    {
        if (message is null)
        {
            return (null, null);
        }

        foreach (var block in ReadBlocks(message.ContentBlocksJson))
        {
            var type = StringField(block, "type");
            if (string.IsNullOrWhiteSpace(type))
            {
                continue;
            }

            var preview = PreviewFor(type, block);
            if (!string.IsNullOrWhiteSpace(preview))
            {
                return (Truncate(preview, PreviewMaxLength), type);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// The row's actionable states, sorted by fixed priority <c>approval</c> -&gt; <c>choice</c>
    /// -&gt; <c>draft</c>. A set rather than one value, because a thread can hold more than one.
    /// </summary>
    public static IReadOnlyList<string> BuildMarkers(Message? lastMessage, bool hasPendingSignOff)
    {
        var markers = new List<string>(MarkerPriority.Count);

        // The conversation holds a message with Kind == SignOff and Status == AwaitingSignOff.
        if (hasPendingSignOff)
        {
            markers.Add("approval");
        }

        var types = new HashSet<string>(StringComparer.Ordinal);
        if (lastMessage is not null)
        {
            foreach (var block in ReadBlocks(lastMessage.ContentBlocksJson))
            {
                var type = StringField(block, "type");
                if (!string.IsNullOrWhiteSpace(type))
                {
                    types.Add(type);
                }
            }
        }

        // Aveline's clarification: the client has to be picked before the thread is bound.
        if (types.Contains("choice"))
        {
            markers.Add("choice");
        }

        // Ava's (or Elle's) draft: "here is a message you can send them, copy it?".
        if (types.Contains("suggestion"))
        {
            markers.Add("draft");
        }

        return markers;
    }

    /// <summary>The preview text for one block, or <c>null</c> when it has nothing to say.</summary>
    private static string? PreviewFor(string type, JsonElement block) => type switch
    {
        "text" => StringField(block, "text"),
        // The customer's own relayed words. The block's type is client_message, not text.
        "client_message" => StringField(block, "text"),
        "suggestion" => StringField(block, "text"),
        "piece" => StringField(block, "name") ?? "Piece",
        "look" => StringField(block, "name") ?? "A new look",
        "at_a_glance" => RowCount(block) is var count and > 0
            ? $"{count} details"
            : null,
        "payment" => PaymentPreview(block),
        "courier" => CourierPreview(block),
        "sign_off" => "Approval needed",
        "choice" => StringField(block, "prompt"),
        // An attachment's preview is its file name, so a photo-only note still says something
        // in the list rather than drawing an empty row.
        "attachment" => StringField(block, "fileName") ?? "Attachment",
        _ => null,
    };

    private static string PaymentPreview(JsonElement block)
    {
        var status = StringField(block, "status");
        if (!string.IsNullOrWhiteSpace(status))
        {
            return $"Payment \u00b7 {status}";
        }

        return Amount(block) is var amount and not null
            ? $"Payment \u00b7 LKR {amount}"
            : "Payment";
    }

    private static string CourierPreview(JsonElement block)
    {
        var parts = new[] { StringField(block, "carrier"), StringField(block, "status") }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToList();
        return parts.Count > 0 ? string.Join(" \u00b7 ", parts) : "Delivery update";
    }

    private static string? Amount(JsonElement block)
    {
        if (!block.TryGetProperty("amount", out var amount))
        {
            return null;
        }

        return amount.ValueKind switch
        {
            JsonValueKind.Number when amount.TryGetDecimal(out var value) =>
                value.ToString("N0", CultureInfo.InvariantCulture),
            JsonValueKind.String => amount.GetString(),
            _ => null,
        };
    }

    private static int RowCount(JsonElement block) =>
        block.TryGetProperty("rows", out var rows) && rows.ValueKind == JsonValueKind.Array
            ? rows.GetArrayLength()
            : 0;

    private static string? StringField(JsonElement block, string field)
    {
        if (!block.TryGetProperty(field, out var value) || value.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var text = value.GetString();
        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private static IEnumerable<JsonElement> ReadBlocks(string? contentBlocksJson)
    {
        if (string.IsNullOrWhiteSpace(contentBlocksJson))
        {
            return [];
        }

        try
        {
            using var document = JsonDocument.Parse(contentBlocksJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return [];
            }

            // Clone so the elements outlive the document.
            return document.RootElement.EnumerateArray().Select(element => element.Clone()).ToList();
        }
        catch (JsonException)
        {
            // A message whose blocks cannot be read is a message with no preview, not a failure.
            return [];
        }
    }

    private static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..maxLength];
}
