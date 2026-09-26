using Aveline.Api.Modules.Commerce.Services;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Services;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// ADR-024, Decision 1. The line items a conversation sends the agent are derived from what the
/// customer explicitly asked to buy and resolved against real inventory.
/// </summary>
/// <remarks>
/// Two refusals carry the weight here, and both are about the number the approval rules gate on: a
/// message that neither buys nor prices produces nothing, and a message that cannot be resolved to a
/// specific piece produces nothing (a guess about which piece is a guess about the total). What a
/// message *does* produce carries its purpose: a price question resolves the same pieces an order
/// does and declares itself a quote, which is what stops a question becoming a sale (ADR-028).
/// </remarks>
public class OrderContextBuilderTests
{
    private static readonly InventoryItemDto EmeraldSaree = new()
    {
        Id = Guid.Parse("b7f1c1a4-0000-4000-8000-000000000001"),
        ItemName = "Emerald Green Georgette Saree",
        Category = "Sarees",
        Color = "Emerald Green",
        Price = 75000m,
        Cost = 65000m,
        Quantity = 4,
        Status = "available",
        IsAvailable = true
    };

    private static readonly InventoryItemDto FuchsiaDress = new()
    {
        Id = Guid.Parse("b7f1c1a4-0000-4000-8000-000000000002"),
        ItemName = "Fuchsia Pink Bodycon Mini Dress",
        Category = "Gowns",
        Color = "Fuchsia Pink",
        Price = 12550m,
        Cost = 10000m,
        Quantity = 10,
        Status = "available",
        IsAvailable = true
    };

    private static readonly InventoryItemDto ChampagneDress = new()
    {
        Id = Guid.Parse("b7f1c1a4-0000-4000-8000-000000000003"),
        ItemName = "Champagne Rose Garden Satin Midi Dress",
        Category = "Gowns",
        Color = "Champagne Cream",
        Price = 10000m,
        Cost = 9500m,
        Quantity = 4,
        Status = "available",
        IsAvailable = true
    };

    private static OrderContextBuilder Builder(params InventoryItemDto[] catalog)
    {
        var inventory = new Mock<IInventoryService>();
        inventory
            .Setup(service => service.QueryCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<CatalogQueryRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new CatalogPagedResponse { Items = catalog.ToList(), Total = catalog.Length });
        return new OrderContextBuilder(inventory.Object);
    }

    [Fact]
    public async Task AMessageNamingAPiece_ResolvesItWithTheCatalogsOwnPriceAndCost()
    {
        var context = await Builder(EmeraldSaree, FuchsiaDress, ChampagneDress)
            .BuildAsync(Guid.NewGuid(), "I want to buy the emerald green saree, please send the order");

        var item = Assert.Single(context.Items);
        Assert.Equal(EmeraldSaree.Id, item.ItemId);
        Assert.Equal("Emerald Green Georgette Saree", item.ItemName);
        Assert.Equal(75000m, item.UnitPrice);
        Assert.Equal(65000m, item.WholesaleCost);
        Assert.Equal(1, item.Quantity);
        Assert.Equal(75000m, item.TotalPrice);
    }

    [Fact]
    public async Task APieceNamedByTwoOfItsWords_StillResolves()
    {
        // "pink dress" is the customer's phrasing; the catalog says "Fuchsia Pink Bodycon Mini Dress".
        var context = await Builder(EmeraldSaree, FuchsiaDress, ChampagneDress)
            .BuildAsync(Guid.NewGuid(), "Can I buy the pink dress please?");

        Assert.Equal(FuchsiaDress.Id, Assert.Single(context.Items).ItemId);
    }

    [Fact]
    public async Task TwoPiecesInOneMessage_ResolveToTwoLineItems()
    {
        var context = await Builder(EmeraldSaree, FuchsiaDress, ChampagneDress)
            .BuildAsync(Guid.NewGuid(), "I'll take the emerald saree and the pink dress");

        Assert.Equal(2, context.Items.Count);
        Assert.Contains(context.Items, item => item.ItemId == EmeraldSaree.Id);
        Assert.Contains(context.Items, item => item.ItemId == FuchsiaDress.Id);
    }

    [Theory]
    // Neither buying nor pricing: a question about availability, or a preference. There is nothing
    // for the rules to evaluate, so nothing is sent.
    [InlineData("Do you have the emerald green saree?")]
    [InlineData("Is the pink dress available in a size 8?")]
    [InlineData("I love the emerald saree")]
    public async Task AMessageThatNeitherBuysNorPrices_ProducesNoItems(string message)
    {
        var context = await Builder(EmeraldSaree, FuchsiaDress)
            .BuildAsync(Guid.NewGuid(), message);

        Assert.Empty(context.Items);
        Assert.Equal(OrderContextPurpose.Order, context.Purpose);
    }

    [Fact]
    public async Task APricingQuestion_ResolvesItsPiecesAsAQuote()
    {
        // ADR-028. "How much is the pink dress?" is a question, so it must not become an order - but
        // a discount ceiling needs a price, and the price is only in the catalog. The piece is
        // resolved and the *purpose* is what keeps it from being bought.
        var context = await Builder(EmeraldSaree, FuchsiaDress)
            .BuildAsync(Guid.NewGuid(), "How much is the pink dress?");

        Assert.True(context.IsQuote);
        Assert.Equal(FuchsiaDress.Id, Assert.Single(context.Items).ItemId);
        Assert.Equal(FuchsiaDress.Price, context.Items[0].UnitPrice);
    }

    [Fact]
    public async Task TheDiscountQuestionFromTheThread_ResolvesTheDressItNames()
    {
        // The message that produced the defect, verbatim: a discount question about one named piece,
        // which resolved a quote and then had nothing to price it with.
        var context = await Builder(EmeraldSaree, FuchsiaDress, ChampagneDress)
            .BuildAsync(
                Guid.NewGuid(),
                "How much of a discount can we give this customer for +Champagne Rose satin midi dress?");

        Assert.True(context.IsQuote);
        var item = Assert.Single(context.Items);
        Assert.Equal(ChampagneDress.Id, item.ItemId);
        Assert.Equal(ChampagneDress.Cost, item.WholesaleCost);
    }

    [Fact]
    public async Task ABuyIsStillAnOrder_EvenWhenItNamesAPiece()
    {
        // The purpose is decided by the purchase signal, not by the presence of a price word: "how
        // much" appears here too, and it must not demote a real order to a quote.
        var context = await Builder(FuchsiaDress)
            .BuildAsync(Guid.NewGuid(), "I'll take the pink dress - how much is it?");

        Assert.False(context.IsQuote);
        Assert.Single(context.Items);
    }

    [Fact]
    public async Task APricingQuestionThatNamesNothingSpecific_ProducesNoItems()
    {
        // One token is not a piece: matching "dress" alone would price whichever dress came back
        // first, and the total it produced would be a guess.
        var context = await Builder(EmeraldSaree, FuchsiaDress)
            .BuildAsync(Guid.NewGuid(), "how much is a dress?");

        Assert.Empty(context.Items);
    }

    [Theory]
    // Nothing specific was named. Picking a dress here would be choosing the total by guesswork.
    [InlineData("I want to buy a dress")]
    [InlineData("buy me something nice")]
    // Named a piece that is not in the catalog: omitted, never approximated.
    [InlineData("I want to buy the blue velvet lehenga")]
    public async Task AMessageThatNamesNoResolvablePiece_ProducesNoItems(string message)
    {
        var context = await Builder(EmeraldSaree, FuchsiaDress, ChampagneDress)
            .BuildAsync(Guid.NewGuid(), message);

        Assert.Empty(context.Items);
    }

    [Fact]
    public async Task APieceThatIsNotSellable_IsNotResolved()
    {
        var soldOut = new InventoryItemDto
        {
            Id = Guid.NewGuid(),
            ItemName = "Emerald Green Georgette Saree",
            Price = 75000m,
            Cost = 65000m,
            Quantity = 0,
            Status = "sold_out",
            IsAvailable = false
        };

        var context = await Builder(soldOut).BuildAsync(Guid.NewGuid(), "I want to buy the emerald saree");

        Assert.Empty(context.Items);
    }

    [Theory]
    [InlineData("I want to buy 3 emerald sarees", 3)]
    [InlineData("buy the emerald saree", 1)]
    // A number after "size" is a measurement, not a count.
    [InlineData("I want to buy the emerald saree in size 4", 1)]
    public async Task TheStatedQuantity_IsUsed(string message, int expected)
    {
        var context = await Builder(EmeraldSaree).BuildAsync(Guid.NewGuid(), message);

        Assert.Equal(expected, Assert.Single(context.Items).Quantity);
    }

    [Fact]
    public async Task TheCatalogIsAskedOnlyForSellableItems()
    {
        CatalogQueryRequest? captured = null;
        var inventory = new Mock<IInventoryService>();
        inventory
            .Setup(service => service.QueryCatalogAsync(
                It.IsAny<Guid>(), It.IsAny<CatalogQueryRequest>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, CatalogQueryRequest, CancellationToken>((_, request, _) => captured = request)
            .ReturnsAsync(new CatalogPagedResponse { Items = new List<InventoryItemDto> { EmeraldSaree } });

        await new OrderContextBuilder(inventory.Object)
            .BuildAsync(Guid.NewGuid(), "I want to buy the emerald saree");

        Assert.NotNull(captured);
        Assert.True(captured!.InStockOnly);
        Assert.Contains("available", captured.Statuses ?? new List<string>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task AnEmptyMessage_ProducesNoItems(string? message)
    {
        var context = await Builder(EmeraldSaree).BuildAsync(Guid.NewGuid(), message);

        Assert.Empty(context.Items);
    }

    [Fact]
    public async Task NoOrganization_ProducesNoItems()
    {
        var context = await Builder(EmeraldSaree).BuildAsync(Guid.Empty, "I want to buy the emerald saree");

        Assert.Empty(context.Items);
    }
}
