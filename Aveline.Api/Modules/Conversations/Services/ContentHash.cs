using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aveline.Api.Modules.Conversations.Services;

/// <summary>
/// Computes a canonical hash of a message's content blocks so a human approval can be bound to
/// the exact payload that was displayed. The hash is stable across JSON formatting/whitespace
/// differences (object keys are sorted, no insignificant whitespace) but changes whenever any
/// value in the payload changes.
/// </summary>
public static class ContentHash
{
    /// <summary>Computes the canonical SHA-256 (hex) of the given content blocks JSON.</summary>
    public static string Compute(string contentBlocksJson)
    {
        if (string.IsNullOrWhiteSpace(contentBlocksJson))
        {
            return ComputeCanonical(JsonNode.Parse("[]"));
        }

        JsonNode? node;
        try
        {
            node = JsonNode.Parse(contentBlocksJson);
        }
        catch (JsonException)
        {
            // Fall back to hashing the raw bytes so we never silently approve an unparseable payload.
            return Sha256Hex(Encoding.UTF8.GetBytes(contentBlocksJson));
        }

        return ComputeCanonical(node);
    }

    private static string ComputeCanonical(JsonNode? node)
    {
        var canonical = (Canonicalize(node) ?? JsonNode.Parse("null")).ToJsonString(new JsonSerializerOptions { WriteIndented = false });
        return Sha256Hex(Encoding.UTF8.GetBytes(canonical));
    }

    /// <summary>Recursively sorts object properties so key order never affects the hash.</summary>
    private static JsonNode? Canonicalize(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
            {
                var sorted = new JsonObject();
                foreach (var key in obj.Select(p => p.Key).OrderBy(k => k, StringComparer.Ordinal))
                {
                    sorted[key] = Canonicalize(obj[key]);
                }
                return sorted;
            }
            case JsonArray arr:
            {
                var outArr = new JsonArray();
                foreach (var item in arr)
                {
                    outArr.Add(Canonicalize(item));
                }
                return outArr;
            }
            default:
                return node?.DeepClone();
        }
    }

    private static string Sha256Hex(byte[] bytes)
    {
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
