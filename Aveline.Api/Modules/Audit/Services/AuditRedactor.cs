using System.Text.Json;
using System.Text.Json.Nodes;

namespace Aveline.Api.Modules.Audit.Services;

/// <summary>
/// Key-name based redaction. A key whose name contains any of the documented sensitive
/// fragments has its entire value replaced, at any depth. Values are never inspected,
/// so a secret cannot survive by being nested or re-labelled (domain-model.md §9).
/// </summary>
public sealed class AuditRedactor : IAuditRedactor
{
    public const string RedactedMarker = "[REDACTED]";

    private static readonly string[] SensitiveFragments =
    [
        "password",
        "secret",
        "token",
        "credential",
        "ciphertext",
        "apikey",
        "api_key",
        "authorization",
        "cookie",
        "hash",
        "prompt",
        "message",
        "body",
        "content",
    ];

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public string? Redact(object? payload)
    {
        if (payload is null)
        {
            return null;
        }

        var node = JsonSerializer.SerializeToNode(payload, SerializerOptions);
        if (node is null)
        {
            return null;
        }

        RedactNode(node);
        return node.ToJsonString(SerializerOptions);
    }

    private static void RedactNode(JsonNode node)
    {
        switch (node)
        {
            case JsonObject jsonObject:
                foreach (var property in jsonObject.ToList())
                {
                    if (IsSensitive(property.Key))
                    {
                        jsonObject[property.Key] = RedactedMarker;
                    }
                    else if (property.Value is not null)
                    {
                        RedactNode(property.Value);
                    }
                }

                break;

            case JsonArray jsonArray:
                foreach (var item in jsonArray)
                {
                    if (item is not null)
                    {
                        RedactNode(item);
                    }
                }

                break;
        }
    }

    private static bool IsSensitive(string key)
    {
        var normalized = key.ToLowerInvariant();
        return SensitiveFragments.Any(fragment => normalized.Contains(fragment, StringComparison.Ordinal));
    }
}
