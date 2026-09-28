using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #404 (Payments P1), the top-up half of G7. The catalogue and the purchase must select the
/// same row for a SKU: the newest <c>Active</c> row whose effective window contains the instant.
/// Before this, the catalogue listed every active row (so a re-priced SKU appeared twice) and the
/// purchase took the newest by <c>EffectiveFrom</c> with no window check, so the two could disagree.
/// </summary>
public class PriceBookSelectionTests
{
    private static readonly DateTime At = new(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc);

    private static BlossomPriceEntry Row(
        string skuCode,
        decimal priceLkr,
        DateTime effectiveFrom,
        DateTime? effectiveTo = null,
        BlossomRuleStatus status = BlossomRuleStatus.Active,
        decimal blossomQuantity = 500m) =>
        new()
        {
            SkuKind = BlossomSkuKind.TopUpPack,
            SkuCode = skuCode,
            BlossomQuantity = blossomQuantity,
            PriceLkr = priceLkr,
            EffectiveFrom = effectiveFrom,
            EffectiveTo = effectiveTo,
            Status = status,
            ChangeReason = "A top-up price row seeded by the price-book selection test.",
            CreatedByUserId = Guid.CreateVersion7(),
        };

    [Fact]
    public void SelectsTheNewestEffectiveRowForTheSku()
    {
        var entries = new[]
        {
            Row("blossom_pack_500", 2000m, At.AddMonths(-6), effectiveTo: At.AddMonths(-3)),
            Row("blossom_pack_500", 2200m, At.AddMonths(-3)),
        };

        var selected = PriceBookSelection.SelectActiveSku(entries, "blossom_pack_500", At);

        Assert.NotNull(selected);
        Assert.Equal(2200m, selected!.PriceLkr);
    }

    [Fact]
    public void SelectsTheOlderRowWhileItsWindowIsStillOpen()
    {
        var entries = new[]
        {
            Row("blossom_pack_500", 2000m, At.AddMonths(-6), effectiveTo: At.AddMonths(-3)),
            Row("blossom_pack_500", 2200m, At.AddMonths(-3)),
        };

        var selected = PriceBookSelection.SelectActiveSku(
            entries, "blossom_pack_500", At.AddMonths(-4));

        Assert.NotNull(selected);
        Assert.Equal(2000m, selected!.PriceLkr);
    }

    [Fact]
    public void AnExpiredSkuIsNotSelectable()
    {
        var entries = new[]
        {
            Row("blossom_pack_500", 2000m, At.AddMonths(-6), effectiveTo: At.AddMonths(-1)),
        };

        Assert.Null(PriceBookSelection.SelectActiveSku(entries, "blossom_pack_500", At));
    }

    [Fact]
    public void ANonActiveSkuIsNotSelectable()
    {
        var entries = new[]
        {
            Row("blossom_pack_500", 2000m, At.AddMonths(-6), status: BlossomRuleStatus.Draft),
        };

        Assert.Null(PriceBookSelection.SelectActiveSku(entries, "blossom_pack_500", At));
    }

    [Fact]
    public void AnotherSkusRowIsNotSelected()
    {
        var entries = new[] { Row("blossom_pack_100", 500m, At.AddMonths(-6)) };

        Assert.Null(PriceBookSelection.SelectActiveSku(entries, "blossom_pack_500", At));
    }
}
