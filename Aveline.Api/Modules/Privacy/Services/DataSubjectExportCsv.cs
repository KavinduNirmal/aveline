using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// The CSV half of the export (plan §7.4). A JSON document maps cleanly onto the nested structure
/// (memories, preferences, interactions); CSV does not, so the format is <b>one file per collection
/// inside a ZIP, with a <c>MANIFEST.json</c></b> describing what is in it — never one flattened file
/// that silently drops the collections it cannot express.
/// </summary>
public static class DataSubjectExportCsv
{
    /// <summary>The manifest entry name, which is also the ZIP's index of contents.</summary>
    public const string ManifestName = "MANIFEST.json";

    /// <summary>One file per collection, in the order a reader wants them.</summary>
    internal static readonly (string File, string Property)[] Collections =
    [
        ("customer.csv", "customer"),
        ("consent.csv", "consent"),
        ("consent-history.csv", "consentHistory"),
        ("memories.csv", "memories"),
        ("preferences.csv", "preferences"),
        ("events.csv", "events"),
        ("interactions.csv", "interactions"),
        ("tags.csv", "tags"),
        ("matches.csv", "matches"),
        ("sourcing-requests.csv", "sourcingRequests"),
        ("conversations.csv", "conversations"),
        ("messages.csv", "messages"),
        ("attachments.csv", "attachments"),
    ];

    /// <summary>Builds the ZIP of CSVs plus the manifest.</summary>
    public static byte[] BuildZip(DataSubjectExportDocument export)
    {
        ArgumentNullException.ThrowIfNull(export);

        using var buffer = new MemoryStream();
        var manifest = new List<object>();

        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (file, property) in Collections)
            {
                var rows = Rows(export.Document, property);
                WriteEntry(archive, file, Csv(rows));
                manifest.Add(new { name = file, rows = rows.Count });
            }

            var document = new
            {
                generatedAtUtc = export.Document.TryGetProperty("generatedAtUtc", out var generated)
                    ? generated.GetString()
                    : null,
                subject = export.Document.TryGetProperty("subject", out var subject) ? subject : default,
                counts = export.Counts,
                files = manifest,
            };

            WriteEntry(
                archive,
                ManifestName,
                JsonSerializer.Serialize(document, new JsonSerializerOptions { WriteIndented = true }));
        }

        return buffer.ToArray();
    }

    /// <summary>
    /// Flattens one collection into rows. A record is a header set of its scalar properties, plus a
    /// <c>json</c> column for any nested value, so nothing is silently dropped.
    /// </summary>
    private static List<Dictionary<string, string>> Rows(JsonElement document, string property)
    {
        var rows = new List<Dictionary<string, string>>();
        if (!document.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null)
        {
            return rows;
        }

        if (value.ValueKind == JsonValueKind.Object)
        {
            rows.Add(Flatten(value));
            return rows;
        }

        if (value.ValueKind != JsonValueKind.Array)
        {
            return rows;
        }

        foreach (var item in value.EnumerateArray())
        {
            rows.Add(Flatten(item));
        }

        return rows;
    }

    private static Dictionary<string, string> Flatten(JsonElement item)
    {
        var row = new Dictionary<string, string>(StringComparer.Ordinal);
        if (item.ValueKind != JsonValueKind.Object)
        {
            row["value"] = Render(item);
            return row;
        }

        foreach (var property in item.EnumerateObject())
        {
            row[property.Name] = Render(property.Value);
        }

        return row;
    }

    private static string Render(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.String => value.GetString() ?? string.Empty,
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        JsonValueKind.Object or JsonValueKind.Array => value.GetRawText(),
        _ => value.GetRawText(),
    };

    /// <summary>
    /// A CSV with a stable union-of-columns header. The column order is the order keys first appear,
    /// so two rows of the same shape produce the same file every time.
    /// </summary>
    private static string Csv(List<Dictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return string.Empty;
        }

        var columns = new List<string>();
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (!columns.Contains(key, StringComparer.Ordinal))
                {
                    columns.Add(key);
                }
            }
        }

        var builder = new StringBuilder();
        builder.AppendLine(string.Join(',', columns.Select(Escape)));
        foreach (var row in rows)
        {
            builder.AppendLine(string.Join(',', columns.Select(c => Escape(row.TryGetValue(c, out var v) ? v : string.Empty))));
        }

        return builder.ToString();
    }

    private static string Escape(string value)
    {
        if (!value.Contains(',', StringComparison.Ordinal)
            && !value.Contains('"', StringComparison.Ordinal)
            && !value.Contains('\n', StringComparison.Ordinal)
            && !value.Contains('\r', StringComparison.Ordinal))
        {
            return value;
        }

        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    private static void WriteEntry(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name, CompressionLevel.Fastest);
        using var stream = entry.Open();
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    /// <summary>The download filename for one format (plan §7.2, §7.4).</summary>
    public static string FileName(string organizationSlug, DateTime generatedAtUtc, string format)
    {
        var extension = format.Equals("csv", StringComparison.OrdinalIgnoreCase) ? "zip" : "json";
        return $"aveline-data-{organizationSlug}-{generatedAtUtc.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}.{extension}";
    }
}
