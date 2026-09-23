using System.Text;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Services;

namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// A line item derived from what a customer explicitly asked for and resolved against real
/// inventory reference data (ADR-024, Decision 1).
/// </summary>
/// <remarks>
/// The price and cost come from the inventory row and from nowhere else. That is the whole point:
/// a guessed price would corrupt the very margin check the approval rules exist to enforce, so an
/// item that cannot be resolved is omitted rather than approximated (invariant A1).
/// </remarks>
public sealed record OrderContextItem(
    Guid ItemId,
    string ItemName,
    int Quantity,
    decimal UnitPrice,
    decimal WholesaleCost)
{
    /// <summary>The line total, computed the same way <c>OrderService</c> computes it.</summary>
    public decimal TotalPrice => Math.Round(UnitPrice * Quantity, 2);
}

/// <summary>
/// The order context a conversation contributes to an agent run: the resolvable line items, and
/// nothing else. Deliberately not a draft order — the API owns persistence (ADR-024, Decision 2).
/// </summary>
public sealed record OrderContext(IReadOnlyList<OrderContextItem> Items)
{
    /// <summary>The context of a message that named no resolvable item.</summary>
    public static readonly OrderContext Empty = new(Array.Empty<OrderContextItem>());

    public bool HasItems => Items.Count > 0;
}

/// <summary>Derives candidate line items from a customer message (ADR-024, Decision 1).</summary>
public interface IOrderContextBuilder
{
    /// <summary>
    /// Return the inventory-resolved line items a message asks to buy, or
    /// <see cref="OrderContext.Empty"/> when it asks for nothing purchasable.
    /// </summary>
    Task<OrderContext> BuildAsync(
        Guid organizationId,
        string? message,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves the pieces a customer named against the organization's catalog.
/// </summary>
/// <remarks>
/// <para>
/// Two deliberate refusals shape this class. First, a message with no purchase signal produces no
/// items at all: "how much is the pink dress?" routes to commerce and must not be turned into an
/// order. Second, a match must be specific. A message that matches several distinct pieces equally
/// well produces <em>nothing</em>, because choosing one would be a guess, and a guess about which
/// piece is a guess about the total — the number the approval rules gate on.
/// </para>
/// <para>
/// Quantity parsing is intentionally shallow: the first standalone number in the message, unless it
/// directly follows "size". Anything cleverer belongs in a real NLU step, and this value is visible
/// on the draft order the owner approves.
/// </para>
/// </remarks>
public sealed class OrderContextBuilder : IOrderContextBuilder
{
    private readonly IInventoryService _inventory;

    /// <summary>How many catalog rows a single match is allowed to consider.</summary>
    private const int CatalogScanLimit = 200;

    /// <summary>The most line items one message may resolve to, as a safety rail.</summary>
    private const int MaxItems = 5;

    public OrderContextBuilder(IInventoryService inventory)
    {
        _inventory = inventory ?? throw new ArgumentNullException(nameof(inventory));
    }

    /// <summary>
    /// Words that say nothing about <em>which</em> piece is meant. Kept deliberately short: a word
    /// that names a garment ("dress", "saree", "gown") must never appear here, or the item it
    /// identifies becomes unmatchable.
    /// </summary>
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        "a", "about", "all", "am", "an", "and", "any", "are", "as", "at", "be", "been", "but",
        "by", "can", "checkout", "could", "did", "do", "does", "for", "from", "get", "give",
        "had", "has", "have", "he", "her", "here", "him", "his", "how", "i", "if", "in", "is",
        "it", "its", "just", "know", "let", "like", "me", "much", "my", "need", "no", "not",
        "of", "on", "one", "or", "our", "out", "please", "put", "she", "should", "show", "so",
        "some", "tell", "than", "that", "the", "their", "them", "then", "there", "these", "they",
        "this", "those", "to", "too", "up", "us", "very", "was", "we", "were", "what", "when",
        "where", "which", "who", "why", "will", "with", "would", "you", "your"
    };

    /// <summary>
    /// Verbs and phrases that mean "I am buying this". A message must carry one before any item is
    /// resolved, so a question about a piece never becomes an order for it.
    /// </summary>
    private static readonly string[] PurchaseSignals =
    {
        "buy", "buying", "purchase", "purchasing", "order", "ordering", "checkout", "reserve",
        "reserving", "i'll take", "ill take", "i will take", "take the", "send me", "send it",
        "send the", "get me", "place an order", "place order", "want to get", "would like to buy",
        "like to buy", "want to buy", "want to order", "going to buy", "proceed with"
    };

    /// <inheritdoc />
    public async Task<OrderContext> BuildAsync(
        Guid organizationId,
        string? message,
        CancellationToken cancellationToken = default)
    {
        if (organizationId == Guid.Empty || string.IsNullOrWhiteSpace(message))
        {
            return OrderContext.Empty;
        }

        var normalized = Normalize(message);
        if (!HasPurchaseSignal(normalized))
        {
            return OrderContext.Empty;
        }

        var messageTokens = Tokenize(normalized);
        if (messageTokens.Count == 0)
        {
            return OrderContext.Empty;
        }

        var catalog = await LoadCatalogAsync(organizationId, cancellationToken);
        if (catalog.Count == 0)
        {
            return OrderContext.Empty;
        }

        var matches = new List<Match>();
        foreach (var item in catalog)
        {
            var match = MatchItem(item, messageTokens);
            if (match is not null)
            {
                matches.Add(match);
            }
        }

        if (matches.Count == 0)
        {
            return OrderContext.Empty;
        }

        var selected = SelectDistinct(matches);
        var quantity = QuantityFrom(normalized);

        var items = selected
            .Take(MaxItems)
            .Select(match => new OrderContextItem(
                match.Item.Id,
                match.Item.ItemName,
                quantity,
                match.Item.Price,
                match.Item.Cost))
            .ToList();

        return items.Count == 0 ? OrderContext.Empty : new OrderContext(items);
    }

    private async Task<IReadOnlyList<InventoryItemDto>> LoadCatalogAsync(
        Guid organizationId,
        CancellationToken cancellationToken)
    {
        var response = await _inventory.QueryCatalogAsync(
            organizationId,
            new CatalogQueryRequest
            {
                OrganizationId = organizationId,
                InStockOnly = true,
                Statuses = new List<string> { "available" },
                Page = 1,
                PageSize = CatalogScanLimit
            },
            cancellationToken);

        // `IsAvailable` is the domain's own answer (stock > 0, status available, not deleted). It is
        // re-checked here so a catalog filter change cannot quietly admit a piece we cannot sell.
        return (response?.Items ?? new List<InventoryItemDto>())
            .Where(item => item.IsAvailable)
            .ToList();
    }

    /// <summary>A catalog item the message names, with the evidence that it was named.</summary>
    private sealed record Match(InventoryItemDto Item, IReadOnlyList<int> MatchedIndices)
    {
        public int FirstTokenIndex => MatchedIndices[0];

        public int MatchedTokenCount => MatchedIndices.Count;
    }

    /// <summary>
    /// Decide whether <paramref name="messageTokens"/> names <paramref name="item"/>.
    /// </summary>
    /// <remarks>
    /// The item's own significant tokens must appear in the message <em>in name order</em>, and at
    /// least two of them must be present (or the single one, for a one-word name). Requiring order
    /// keeps "green saree" from matching a "Saree ... Green" name that reads as a different piece,
    /// and requiring two tokens is what stops a bare "dress" from matching every dress in the shop.
    /// </remarks>
    private static Match? MatchItem(InventoryItemDto item, IReadOnlyList<string> messageTokens)
    {
        var nameTokens = Tokenize(Normalize(item.ItemName));
        if (nameTokens.Count == 0)
        {
            return null;
        }

        var required = Math.Min(2, nameTokens.Count);
        var matched = new List<int>();
        var cursor = 0;

        foreach (var nameToken in nameTokens)
        {
            var foundAt = -1;
            for (var i = cursor; i < messageTokens.Count; i++)
            {
                if (TokensEquivalent(nameToken, messageTokens[i]))
                {
                    foundAt = i;
                    break;
                }
            }

            if (foundAt < 0)
            {
                continue;
            }

            matched.Add(foundAt);
            cursor = foundAt + 1;
        }

        if (matched.Count < required)
        {
            return null;
        }

        return new Match(item, matched);
    }

    /// <summary>
    /// Drop partial matches a stronger match already covers, then refuse genuine ties.
    /// </summary>
    /// <remarks>
    /// Without the first filter a catalog holding both "Saree" and "Emerald Green Georgette Saree"
    /// would put both on the draft order, counting one purchase twice. The second filter is the
    /// invariant-A1 refusal: when two distinct pieces are named by exactly the same words, nothing
    /// is returned, because picking one would be guessing.
    /// </remarks>
    private static IReadOnlyList<Match> SelectDistinct(IReadOnlyList<Match> matches)
    {
        var winners = matches
            .Where(candidate => !matches.Any(other =>
                !ReferenceEquals(other, candidate)
                && other.MatchedIndices.Count > candidate.MatchedIndices.Count
                && candidate.MatchedIndices.All(other.MatchedIndices.Contains)))
            .ToList();

        var tied = winners
            .GroupBy(match => string.Join(',', match.MatchedIndices))
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToList();

        var ordered = winners
            .Except(tied)
            .OrderBy(match => match.FirstTokenIndex)
            .ToList();

        return ordered;
    }

    /// <summary>
    /// The quantity the customer stated, defaulting to one. A number immediately after "size" is a
    /// measurement, not a count.
    /// </summary>
    internal static int QuantityFrom(string normalizedMessage)
    {
        var raw = normalizedMessage.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < raw.Length; i++)
        {
            if (!int.TryParse(raw[i], out var value) || value is < 1 or > 99)
            {
                continue;
            }

            if (i > 0 && raw[i - 1] is "size" or "sizes")
            {
                continue;
            }

            return value;
        }

        return 1;
    }

    /// <summary>True when the message asks to buy something rather than asking about it.</summary>
    internal static bool HasPurchaseSignal(string normalizedMessage)
        => PurchaseSignals.Any(signal => normalizedMessage.Contains(signal, StringComparison.Ordinal));

    /// <summary>
    /// Lower-case, punctuation-free, whitespace-collapsed text. Digits survive because a quantity
    /// may be read from them.
    /// </summary>
    internal static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? char.ToLowerInvariant(ch) : ' ');
        }

        return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>The message's or a name's meaningful words, in order.</summary>
    internal static List<string> Tokenize(string normalized)
        => normalized
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(token => token.Length > 1 && !StopWords.Contains(token))
            .ToList();

    /// <summary>
    /// Whether two words are the same word for matching purposes, allowing for a plural on either
    /// side ("saree"/"sarees", "dress"/"dresses").
    /// </summary>
    internal static bool TokensEquivalent(string a, string b)
    {
        if (string.Equals(a, b, StringComparison.Ordinal))
        {
            return true;
        }

        foreach (var variant in Variants(a))
        {
            if (Variants(b).Contains(variant, StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<string> Variants(string token)
    {
        yield return token;
        if (token.Length <= 3 || !token.EndsWith('s'))
        {
            yield break;
        }

        yield return token[..^1];
        if (token.EndsWith("es", StringComparison.Ordinal))
        {
            yield return token[..^2];
        }
    }
}
