namespace Aveline.Api.Modules.Payments.Services;

/// <summary>
/// The stable reason codes the reconciliation read reports for a divergent intent. Constants rather
/// than free text so a dashboard, an alert rule or a test can key off them.
/// </summary>
public static class ReconciliationReasons
{
    /// <summary>The row has no provider intent id, so there is nothing to reconcile against.</summary>
    public const string ProviderIntentMissing = "provider-intent-missing";

    /// <summary>The provider does not know the charge the row names.</summary>
    public const string ProviderUnknownIntent = "provider-unknown-intent";

    /// <summary>The adapter could not be resolved, or refused the read. Not an all-clear.</summary>
    public const string ProviderUnavailable = "provider-unavailable";

    /// <summary>Unsettled and past <c>ExpiresAt</c>: the sweep has not made the state durable yet.</summary>
    public const string ExpiredUnsettled = "expired-unsettled";

    /// <summary>The provider settled the charge and the webhook never moved the row.</summary>
    public const string ProviderSettledUnconfirmed = "provider-settled-unconfirmed";

    /// <summary>The provider's charge state is not the one Aveline stored.</summary>
    public const string StatusDiverged = "status-diverged";

    /// <summary>The provider's charge amount is not the one Aveline stored (risk R5).</summary>
    public const string AmountDiverged = "amount-diverged";

    /// <summary>The provider's charge currency is not the one Aveline stored (risk R9).</summary>
    public const string CurrencyDiverged = "currency-diverged";

    /// <summary>The provider returned the money and Aveline never recorded the refund.</summary>
    public const string RefundNotRecorded = "refund-not-recorded";

    /// <summary>Aveline recorded a refund the provider never made.</summary>
    public const string RefundRecordedWithoutProvider = "refund-recorded-without-provider";
}

/// <summary>
/// The window and scope of one reconciliation pass. Every member is optional: absent means the
/// default window (<see cref="PaymentReconciliationService.DefaultWindowHours"/> ending now) and
/// every organisation and provider.
/// </summary>
/// <param name="OrganizationId">Scope the intent half to one tenant.</param>
/// <param name="Provider">Scope to one adapter key.</param>
/// <param name="From">Inclusive lower bound on <c>CreatedAt</c>.</param>
/// <param name="To">Inclusive upper bound on <c>CreatedAt</c>.</param>
public sealed record PaymentReconciliationQuery(
    Guid? OrganizationId = null,
    string? Provider = null,
    DateTime? From = null,
    DateTime? To = null);

/// <summary>One intent whose provider state is not the state Aveline holds. Clean rows are absent.</summary>
/// <remarks>
/// Carries no card, PAN, CVC, expiry or token field (constraint C11): a reconciliation row names an
/// intent, an amount and a reason, never an instrument.
/// </remarks>
public sealed record UnreconciledIntentRow(
    Guid PaymentIntentId,
    Guid OrganizationId,
    string Provider,
    string Purpose,
    string StoredStatus,
    string? ProviderStatus,
    string Reason,
    decimal AmountLkr,
    string Currency,
    DateTime CreatedAt,
    DateTime? ExpiresAt,
    DateTime? SettledAt,
    DateTime? RefundedAt);

/// <summary>The inbound webhooks still holding a null <c>ProcessedAt</c>, by provider.</summary>
/// <param name="Reasons">Distinct <c>ProcessingError</c> values, capped, for the operator's first look.</param>
public sealed record UnprocessedWebhookBacklogRow(
    string Provider,
    long UnprocessedEvents,
    DateTime? OldestReceivedAt,
    IReadOnlyList<string> Reasons);

/// <summary>
/// One reconciliation pass. This is the single output the read and the alert both consume.
/// </summary>
public sealed record PaymentReconciliationReport(
    Guid? OrganizationId,
    string? Provider,
    DateTime From,
    DateTime To,
    int IntentsChecked,
    bool Truncated,
    IReadOnlyList<UnreconciledIntentRow> UnreconciledIntents,
    IReadOnlyDictionary<string, long> UnreconciledByProvider,
    IReadOnlyList<UnprocessedWebhookBacklogRow> UnprocessedWebhookBacklog,
    long UnprocessedBacklogTotal,
    DateTime CheckedAt,
    IReadOnlyList<string> Notes)
{
    /// <summary>How many intents diverged. The number <c>unreconciled_intents</c> is published from.</summary>
    public int UnreconciledCount => UnreconciledIntents.Count;
}

/// <summary>
/// The one derivation behind both the reconciliation read and the unreconciled-intent alert
/// (plan §10 Phase 7, §12.1).
/// </summary>
public interface IPaymentReconciliationService
{
    /// <summary>Reconciles provider state against <c>PaymentIntents</c> for a window.</summary>
    Task<PaymentReconciliationReport> ReconcileAsync(
        PaymentReconciliationQuery? query = null, CancellationToken cancellationToken = default);
}
