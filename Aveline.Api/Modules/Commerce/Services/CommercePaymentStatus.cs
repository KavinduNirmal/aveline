namespace Aveline.Api.Modules.Commerce.Services;

/// <summary>
/// The existing <c>Payments.Status</c> string vocabulary, stated once. Plan §9.7: "Replace the
/// free-text <c>"pending"</c>/<c>"confirmed"</c>/<c>"refunded"</c> vocabulary with the provider
/// status mapped onto the existing strings, keeping the column for compatibility while the intents
/// table becomes the source of truth."
/// </summary>
/// <remarks>
/// <para>
/// The column keeps its shipped string values because three readers already filter on them:
/// <c>PaymentQueryParametersDto.Status</c>, <c>TenantDashboardService</c> (which sums
/// <c>Status == "refunded"</c>) and the Flutter client's DTO. Only the *writer* changed: every
/// value below is derived from the provider's verdict on the linked intent, never from a caller's
/// request body.
/// </para>
/// <para>
/// <c>expired</c> is part of the column's declared vocabulary but is not produced here: an expired
/// intent reports <c>Expired</c> through the intent, and mapping it onto the payment's <c>failed</c>
/// bucket would report a timeout as a declined charge. The intent's own status is the place a client
/// reads that distinction, which is the point of making the intent the source of truth.
/// </para>
/// </remarks>
public static class CommercePaymentStatus
{
    /// <summary>The customer has not settled, or the provider is still processing the settlement.</summary>
    public const string Pending = "pending";

    /// <summary>The provider reports the charge settled.</summary>
    public const string Confirmed = "confirmed";

    /// <summary>The provider reports the charge failed, cancelled, or expired.</summary>
    public const string Failed = "failed";

    /// <summary>The charge was returned; written by the refund path, not by a provider event.</summary>
    public const string Refunded = "refunded";

    /// <summary>
    /// Maps the intent's provider status onto the column's vocabulary. The status string is the
    /// intent read's own value (<c>PaymentIntentView.DeriveStatus</c>, which already applies the
    /// derivable <c>Refunded</c> and <c>Expired</c> states), so an intent refunded through the
    /// provider reports <c>refunded</c> here without a second derivation.
    /// </summary>
    public static string FromProviderStatus(string intentStatus) => intentStatus switch
    {
        "Succeeded" => Confirmed,
        "Refunded" => Refunded,
        "Failed" or "Cancelled" or "Expired" => Failed,
        // `RequiresAction` and `Processing`, plus any future status a provider introduces. An
        // unknown provider state is not permission to say money arrived.
        _ => Pending,
    };

    /// <summary>
    /// Maps the creation call's provider status. Identical to <see cref="FromProviderStatus"/> except
    /// for one case: a provider that reports <c>Succeeded</c> at creation leaves the row
    /// <c>pending</c>.
    /// </summary>
    /// <remarks>
    /// <b>Why the exception.</b> On this row, <c>confirmed</c> means "Aveline has recorded the
    /// boutique takings entry", and that entry is written by the confirmation poll — the only writer.
    /// Writing <c>confirmed</c> at creation would make the row claim a settlement the journal does
    /// not yet hold, and the confirmation route's idempotency guard would then skip the journal write
    /// entirely. The provider's verdict is not lost: it is on the intent, which is the source of
    /// truth, and the first poll moves the row and writes the entry together.
    /// </remarks>
    public static string FromProviderStatusAtCreation(string intentStatus) =>
        intentStatus == "Succeeded" ? Pending : FromProviderStatus(intentStatus);
}
