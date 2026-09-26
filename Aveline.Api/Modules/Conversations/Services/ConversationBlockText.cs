using System.Text.Json;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Flattens a stored message block array into readable prose for an agent transcript
/// (ADR-023, W1.1).
/// </summary>
/// <remarks>
/// Extracted as a pure function because it is the one genuinely intricate part of building a
/// transcript, and it is exercised by every history read. The stored blocks are heterogeneous:
/// <c>text</c> carries <c>text</c>, <c>client_message</c> carries <c>from</c> plus <c>text</c>,
/// <c>choice</c> carries a <c>prompt</c>, and <c>attachment</c>/<c>piece</c>/<c>look</c> carry no
/// prose at all.
///
/// <para>
/// Only prose is extracted. A non-text block collapses to <c>null</c> rather than a generated
/// description, so the agent never reads a fabricated summary of content it cannot actually see.
/// </para>
/// </remarks>
public static class ConversationBlockText
{
    /// <summary>
    /// Returns the concatenated prose of <paramref name="contentBlocksJson"/>, or <c>null</c> when
    /// the payload is empty, malformed, not an array, or carries no readable text.
    /// </summary>
    public static string? Flatten(string? contentBlocksJson)
    {
        if (string.IsNullOrWhiteSpace(contentBlocksJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(contentBlocksJson);
            if (document.RootElement.ValueKind != JsonValueKind.Array)
            {
                return null;
            }

            var parts = new List<string>();
            foreach (var block in document.RootElement.EnumerateArray())
            {
                if (block.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                var text = (block.TryGetProperty("type", out var typeElement)
                        ? typeElement.GetString()
                        : null) switch
                {
                    "text" => ReadString(block, "text"),
                    "client_message" => ReadString(block, "text"),
                    "choice" => ReadString(block, "prompt"),
                    _ => null,
                };

                if (!string.IsNullOrWhiteSpace(text))
                {
                    parts.Add(text);
                }
            }

            return parts.Count == 0 ? null : string.Join("\n", parts);
        }
        catch (JsonException)
        {
            // A malformed row must not break a transcript read.
            return null;
        }
    }

    private static string? ReadString(JsonElement block, string propertyName) =>
        block.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;
}
