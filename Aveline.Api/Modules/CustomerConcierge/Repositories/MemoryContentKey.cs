using System.Text;

namespace Aveline.Api.Modules.CustomerConcierge.Repositories;

/// <summary>
/// The statement-equality rule for customer memories (gap A3).
/// </summary>
/// <remarks>
/// <para>
/// Two memories are the same note when their content matches under this normalisation: case,
/// surrounding and repeated whitespace, and trailing punctuation are not distinctions a reader
/// makes, and they are exactly how the same fact ends up stored twice. This is the server-side
/// twin of the agent's <c>normalise_memory_content</c>.
/// </para>
/// <para>
/// It lives in the repository layer because the store is what has to enforce it - the column it
/// feeds carries a unique index, so the rule holds under two concurrent writers and not merely
/// under the one thread that happened to read first. Two copies of this rule would eventually
/// disagree about a duplicate, which is why the agent references this one rather than re-deriving
/// it.
/// </para>
/// </remarks>
public static class MemoryContentKey
{
    /// <summary>The normalised key for a memory statement.</summary>
    public static string From(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var trimmed = content.Trim().TrimEnd('.', '!', '?');
        var builder = new StringBuilder(trimmed.Length);
        var lastWasSpace = false;

        foreach (var character in trimmed)
        {
            if (char.IsWhiteSpace(character))
            {
                // Collapse runs of whitespace to one separator, and drop the leading run entirely.
                if (builder.Length > 0 && !lastWasSpace)
                {
                    builder.Append(' ');
                    lastWasSpace = true;
                }

                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
            lastWasSpace = false;
        }

        // A trailing space can only survive if the statement ended in whitespace after the
        // punctuation was stripped ("party. ").
        return builder.ToString().TrimEnd();
    }
}
