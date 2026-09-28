using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Services;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsOptions = Microsoft.Extensions.Options.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Payments P5 (plan §9.3 F3, §8.5): the mid-cycle proration a plan change is charged, and the rule
/// that decides whether the provider or Aveline does the arithmetic.
/// </summary>
/// <remarks>
/// The local fallback is <c>PlanChangeProration.Local</c>:
/// <c>round((next - previous) * remainingDays / daysInMonth(at), 2, AwayFromZero)</c>, where the day
/// count is the calendar month of the proration date and <c>remainingDays</c> is clamped to it. The
/// table below is the specification of that formula, including a leap February and a zero-day
/// remainder; <c>Proration_FromThePeriodEnd_…</c> in <c>MockPaymentProviderTests</c> is the mock
/// adapter's own statement of the same arithmetic.
/// </remarks>
public class PlanChangeProrationTests
{
    // ============================================================ the local formula

    [Theory]
    // The service's own shape: 19 of February's 28 days on a LKR 3,500 upgrade.
    [InlineData(0, 3500, "2026-03-01", "2026-02-10", 2375.00)]
    // A leap February bills over 29 days.
    [InlineData(0, 3500, "2028-02-29", "2028-02-01", 3379.31)]
    [InlineData(0, 3500, "2028-02-29", "2028-02-28", 120.69)]
    // A non-leap February bills over 28.
    [InlineData(0, 3500, "2027-02-28", "2027-02-01", 3375.00)]
    // Only the difference is prorated, not the whole new price.
    [InlineData(3500, 9000, "2026-03-01", "2026-02-10", 3732.14)]
    // A 31-day month.
    [InlineData(0, 3500, "2026-01-31", "2026-01-01", 3387.10)]
    // A half-cent rounds away from zero, matching Money.Lkr (0.70 * 1 / 28 = 0.025).
    [InlineData(3499.30, 3500, "2027-02-02", "2027-02-01", 0.03)]
    // The zero-day remainder: struck on the period's last day, nothing is owed.
    [InlineData(0, 3500, "2028-02-29", "2028-02-29", 0.00)]
    [InlineData(0, 3500, "2027-02-28", "2027-02-28", 0.00)]
    // A date past the period end is clamped to zero rather than becoming a credit.
    [InlineData(0, 3500, "2026-02-01", "2026-03-05", 0.00)]
    // A downgrade, and a change that does not move the price, owe nothing.
    [InlineData(9000, 3500, "2026-03-01", "2026-02-10", 0.00)]
    [InlineData(3500, 3500, "2026-03-01", "2026-02-10", 0.00)]
    public void Local_MatchesTheDocumentedFormula(
        decimal previousPriceLkr, decimal newPriceLkr, string periodEnd, string at, decimal expected)
    {
        var result = PlanChangeProration.Local(
            previousPriceLkr, newPriceLkr, DateOnly.Parse(periodEnd), DateOnly.Parse(at));

        Assert.Equal(expected, result);
    }

    /// <summary>
    /// The fallback rounds to the same minor unit the provider boundary takes, so a half-cent cannot
    /// round down here and up there (D3, <c>Money.Lkr</c>).
    /// </summary>
    [Fact]
    public void Local_RoundsToTheSameMinorUnitAsMoneyLkr()
    {
        // 0.70 * 1 / 28 on 1 February 2027 is exactly 0.025: away from zero is 0.03, banker's
        // rounding would give 0.02.
        var local = PlanChangeProration.Local(0m, 0.70m, new DateOnly(2027, 2, 2), new DateOnly(2027, 2, 1));

        Assert.Equal(0.03m, local);
        Assert.Equal(local, Money.Lkr(local).ToMajorUnits());
        Assert.Equal(3L, Money.Lkr(local).AmountMinor);
    }

    // ============================================================ provider versus local

    private sealed class StubProviderFactory(IPaymentProvider provider) : IPaymentProviderFactory
    {
        public IPaymentProvider Active => provider;

        public IPaymentProvider Resolve(string providerKey) => provider;
    }

    private static readonly ProrationRequest Request = new(
        PreviousMonthlyPriceLkr: 0m,
        NewMonthlyPriceLkr: 3500m,
        PeriodEnd: new DateOnly(2026, 3, 1),
        At: new DateOnly(2026, 2, 10));

    private static MockPaymentProvider CreateMock()
    {
        var options = new PaymentsOptions { Currency = "LKR" };
        options.Mock.Enabled = true;

        return new MockPaymentProvider(
            OptionsOptions.Create(options),
            TimeProvider.System,
            NullLogger<MockPaymentProvider>.Instance,
            new PaymentMetrics());
    }

    /// <summary>
    /// The capability is consulted, not assumed: an adapter that advertises proration is asked for
    /// its own figure.
    /// </summary>
    [Fact]
    public void Compute_WhenTheProviderSupportsProration_UsesTheProvidersFigure()
    {
        var calculator = new ProrationCalculator(new StubProviderFactory(CreateMock()));

        var quote = calculator.Compute(Request);

        Assert.Equal(ProrationSource.Provider, quote.Source);
        Assert.Equal(
            MockPaymentProvider.ProrateMonthly(3500m, Request.PeriodEnd, Request.At), quote.AmountLkr);
        Assert.Equal(2375.00m, quote.AmountLkr);
    }

    /// <summary>
    /// The capability is consulted, not assumed: an adapter that cannot prorate is never asked, and
    /// Aveline's documented formula prices the charge instead.
    /// </summary>
    [Fact]
    public void Compute_WhenTheProviderCannotProrate_FallsBackToTheLocalFormula()
    {
        var calculator = new ProrationCalculator(new StubProviderFactory(new ManualPaymentProvider()));

        var quote = calculator.Compute(Request);

        Assert.Equal(ProrationSource.Local, quote.Source);
        Assert.Equal(PlanChangeProration.Local(0m, 3500m, Request.PeriodEnd, Request.At), quote.AmountLkr);
        Assert.Equal(2375.00m, quote.AmountLkr);
    }

    /// <summary>
    /// The loud path the acceptance criterion names: asking a provider that cannot prorate throws the
    /// documented not-supported error rather than silently returning the local figure.
    /// </summary>
    [Fact]
    public void ComputeWithProvider_WhenTheProviderCannotProrate_ThrowsNotSupported()
    {
        var calculator = new ProrationCalculator(new StubProviderFactory(new ManualPaymentProvider()));

        var exception = Assert.Throws<PaymentProviderNotSupportedException>(
            () => calculator.ComputeWithProvider(Request));

        Assert.Equal(501, exception.StatusCode);
        Assert.Equal("payment-provider-capability-missing", exception.ErrorCode);
        Assert.Contains("manual", exception.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Advertising a capability without implementing it is an adapter contract hole, and it is
    /// reported rather than papered over with the local formula.
    /// </summary>
    [Fact]
    public void ComputeWithProvider_WhenTheCapabilityHasNoImplementation_ThrowsNotSupported()
    {
        var calculator = new ProrationCalculator(
            new StubProviderFactory(new AdvertisesProrationWithoutImplementingIt()));

        var exception = Assert.Throws<PaymentProviderNotSupportedException>(
            () => calculator.ComputeWithProvider(Request));

        Assert.Equal(501, exception.StatusCode);
        Assert.Contains(nameof(IProrationProvider), exception.Message, StringComparison.Ordinal);
    }

    /// <summary>The local path never consults the provider at all.</summary>
    [Fact]
    public void ComputeLocally_IgnoresTheProvider()
    {
        var calculator = new ProrationCalculator(new StubProviderFactory(new ManualPaymentProvider()));

        Assert.Equal(2375.00m, calculator.ComputeLocally(Request));
    }

    /// <summary>
    /// A minimal adapter that claims <c>SupportsProration</c> but does not implement
    /// <see cref="IProrationProvider"/>. Every other call is refused loudly rather than faked.
    /// </summary>
    private sealed class AdvertisesProrationWithoutImplementingIt : IPaymentProvider
    {
        public string Key => "advertises-proration";

        public PaymentProviderCapabilities Capabilities { get; } = new(
            SupportsRecurringSubscriptions: true,
            SupportsProration: true,
            SupportsPartialRefunds: false,
            SupportsCancelAtPeriodEnd: false,
            SupportsHostedCheckout: true,
            SettlesAsynchronously: true);

        public Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
            CreateProviderIntentRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public Task<ProviderPaymentIntent?> GetPaymentIntentAsync(
            string providerIntentId, CancellationToken cancellationToken = default) =>
            Task.FromResult<ProviderPaymentIntent?>(null);

        public Task<ProviderPaymentIntent> CancelPaymentIntentAsync(
            string providerIntentId, string reason, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public Task<ProviderRefund> RefundAsync(
            ProviderRefundRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public PaymentWebhookEvent VerifyAndParseWebhook(PaymentWebhookRequest request) =>
            throw new PaymentWebhookVerificationException("signature");

        public Task<ProviderSubscription> CreateOrUpdateSubscriptionAsync(
            CreateProviderSubscriptionRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");

        public Task<ProviderSubscription> CancelSubscriptionAsync(
            string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderNotSupportedException("Not supported by the test double.");
    }
}
