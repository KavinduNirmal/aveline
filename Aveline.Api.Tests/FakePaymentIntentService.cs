using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// A hand-written test double for <see cref="IPaymentIntentService"/>. The Commerce order checkout
/// must delegate to the provider abstraction, and this double is what lets the Commerce tests assert
/// what was asked of that abstraction without standing up the whole intent pipeline (an EF context,
/// an event bus, an audit service, a provider factory and a metrics registry).
/// </summary>
/// <remarks>
/// <para>
/// <b>It models the poll, not a shortcut.</b> <see cref="Stored"/> is the mutable "provider-side"
/// state the confirmation route reads, and <see cref="OnPoll"/> runs before every
/// <see cref="GetAsync"/> so a test can flip the intent from <c>RequiresAction</c> to
/// <c>Succeeded</c> between two confirms — exactly what an arriving webhook does to the real row.
/// </para>
/// <para>
/// It deliberately implements no business rules: the real rules are the intent service's, and are
/// tested against it.
/// </para>
/// </remarks>
public sealed class FakePaymentIntentService : IPaymentIntentService
{
    /// <summary>The provider intent id every created view carries; the confirmation stores it.</summary>
    public const string ProviderIntentId = "mock_provider_dummy";

    /// <summary>Every command passed to <see cref="CreateAsync"/>, in order.</summary>
    public List<CreatePaymentIntentCommand> Commands { get; } = [];

    /// <summary>What <see cref="CreateAsync"/> should return for a command. Defaults to a view with no URL.</summary>
    public Func<CreatePaymentIntentCommand, PaymentIntentView>? OnCreate { get; set; }

    /// <summary>The state <see cref="GetAsync"/> serves. Set by the <see cref="OnCreate"/> projection.</summary>
    public PaymentIntentView? Stored { get; set; }

    /// <summary>Runs before each <see cref="GetAsync"/>, so a test can "deliver a webhook" first.</summary>
    public Action? OnPoll { get; set; }

    public Task<PaymentIntentView> CreateAsync(
        CreatePaymentIntentCommand command, CancellationToken cancellationToken = default)
    {
        Commands.Add(command);
        Stored = OnCreate?.Invoke(command) ?? View(command);
        return Task.FromResult(Stored);
    }

    public Task<PaymentIntentView> GetAsync(
        Guid organizationId, Guid intentId, CancellationToken cancellationToken = default)
    {
        OnPoll?.Invoke();

        if (Stored is null)
        {
            throw new PaymentIntentNotFoundException(intentId);
        }

        // Scoped the way the real service scopes it: another tenant's intent does not exist here.
        if (Stored.OrganizationId != organizationId)
        {
            throw new PaymentIntentNotFoundException(intentId);
        }

        return Task.FromResult(Stored);
    }

    public Task<PaymentIntentView> CancelAsync(
        Guid organizationId, Guid intentId, string reason, CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The Commerce checkout does not cancel intents.");

    public Task<PaymentRefundResult> RequestRefundAsync(
        Guid organizationId, Guid intentId, decimal? amountLkr, string reason,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The Commerce checkout does not refund intents.");

    /// <summary>A neutral view for a command, used when a test does not care about the shape.</summary>
    public static PaymentIntentView View(CreatePaymentIntentCommand command) =>
        SettledInPlaceView(command, Guid.NewGuid(), "manual", null, "RequiresAction");

    /// <summary>
    /// A view with a chosen intent id, provider and status. Named for its one real use: pinning the
    /// provider's answer so the Commerce assertions are about the Commerce code.
    /// </summary>
    public static PaymentIntentView SettledInPlaceView(
        CreatePaymentIntentCommand command,
        Guid intentId,
        string provider,
        string? checkoutUrl,
        string status) =>
        new(
            PaymentIntentId: intentId,
            OrganizationId: command.OrganizationId,
            Provider: provider,
            ProviderIntentId: ProviderIntentId,
            Purpose: command.Purpose,
            Status: status,
            AmountLkr: command.Amount.ToMajorUnits(),
            Currency: command.Amount.Currency,
            CheckoutUrl: checkoutUrl,
            FailureCode: null,
            FailureMessage: null,
            CreatedAt: DateTime.UtcNow,
            SettledAt: null,
            RefundedAt: null,
            ExpiresAt: null,
            SkuCode: command.SkuCode,
            BlossomQuantity: command.BlossomQuantity);
}
