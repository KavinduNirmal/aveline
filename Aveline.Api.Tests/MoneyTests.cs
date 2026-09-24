using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Tests;

/// <summary>
/// D3 (payment abstraction §6.7): money crosses the provider boundary as integer minor units while
/// the domain keeps <c>decimal(18,2)</c> LKR. <see cref="Money.Lkr"/> is the single conversion site,
/// so the property that matters is that a price survives the round trip unchanged for every value
/// in the seeded price book, and that a non-positive amount can never become a charge.
/// </summary>
public class MoneyTests
{
    /// <summary>The six seeded prices (scripts/seed-price-book.sh): Bloom 3500, Orchid 9000,
    /// Rose 20000, and the top-up packs at 500, 2000 and 3500.</summary>
    [Theory]
    [InlineData(3500)]
    [InlineData(9000)]
    [InlineData(20000)]
    [InlineData(500)]
    [InlineData(2000)]
    public void Lkr_ToMajorUnits_IsTheIdentityForEverySeededPriceBookValue(decimal price)
    {
        var money = Money.Lkr(price);

        money.ToMajorUnits().Should().Be(price);
        money.AmountMinor.Should().Be((long)(price * 100m));
        money.Currency.Should().Be("LKR");
    }

    /// <summary>
    /// A third decimal place cannot survive a round trip through the ledger, so it is rounded at
    /// the boundary. The rounding mode is deliberate: <c>AwayFromZero</c>, matching
    /// <c>BlossomCalculator</c>, rather than the CLR's default banker's rounding.
    /// </summary>
    [Theory]
    [InlineData(10.005, 1001)]  // 1000.5 -> 1001 (ToEven would give 1000)
    [InlineData(0.015, 2)]      // 1.5 -> 2
    [InlineData(0.005, 1)]      // 0.5 -> 1 (ToEven would give 0)
    [InlineData(0.125, 13)]     // 12.5 -> 13 (ToEven would give 12)
    [InlineData(10.004, 1000)]  // below the half-cent
    public void Lkr_RoundsAwayFromZeroAtTheHalfCent(decimal amount, long expectedMinorUnits)
    {
        Money.Lkr(amount).AmountMinor.Should().Be(expectedMinorUnits);
    }

    [Fact]
    public void ToMajorUnits_DividesByOneHundred()
    {
        new Money(1234, "LKR").ToMajorUnits().Should().Be(12.34m);
        new Money(1, "LKR").ToMajorUnits().Should().Be(0.01m);
    }

    /// <summary>Currency is part of the value, so two amounts in different currencies are never
    /// equal even when their minor units match.</summary>
    [Fact]
    public void Equality_IncludesTheCurrency()
    {
        new Money(100, "LKR").Should().NotBe(new Money(100, "USD"));
        Money.Lkr(1).Should().Be(new Money(100, "LKR"));
    }

    /// <summary>
    /// A charge of zero or a negative amount is a programming error, not a customer outcome, so it
    /// is refused where the money is created rather than at the provider call.
    /// </summary>
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-0.01)]
    public void Lkr_RejectsANonPositiveAmount(decimal amount)
    {
        var create = () => Money.Lkr(amount);

        create.Should().Throw<ArgumentOutOfRangeException>();
    }
}
