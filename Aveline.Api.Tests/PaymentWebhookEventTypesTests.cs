using Aveline.Api.Modules.Payments.Domain;

namespace Aveline.Api.Tests;

/// <summary>
/// The provider wire vocabulary, in one place (plan §9.6, P10). Before this phase every adapter
/// carried its own private string-to-type switch, which is how a provider dispute ended up as
/// <see cref="PaymentWebhookEventType.Unknown"/> with no way for a caller to tell it apart from a
/// genuinely unrecognised event.
/// </summary>
public class PaymentWebhookEventTypesTests
{
    [Theory]
    [InlineData("intent.succeeded", PaymentWebhookEventType.IntentSucceeded)]
    [InlineData("intent.failed", PaymentWebhookEventType.IntentFailed)]
    [InlineData("intent.cancelled", PaymentWebhookEventType.IntentCancelled)]
    [InlineData("intent.expired", PaymentWebhookEventType.IntentExpired)]
    [InlineData("refund.succeeded", PaymentWebhookEventType.RefundSucceeded)]
    [InlineData("refund.failed", PaymentWebhookEventType.RefundFailed)]
    public void Parse_MapsEveryPublishedType(string wire, PaymentWebhookEventType expected)
    {
        Assert.Equal(expected, PaymentWebhookEventTypes.Parse(wire));
    }

    /// <summary>
    /// The deliverable: a dispute is its own type now, under every spelling a provider may use.
    /// </summary>
    [Theory]
    [InlineData("dispute.opened")]
    [InlineData("charge.disputed")]
    [InlineData("charge.dispute.created")]
    public void Parse_RecognisesADispute_AsItsOwnType(string wire)
    {
        Assert.Equal(PaymentWebhookEventType.DisputeOpened, PaymentWebhookEventTypes.Parse(wire));
    }

    /// <summary>
    /// The fail-safe survives: a type nobody handles is still <c>Unknown</c>, which the settlement
    /// path stores unprocessed and logs.
    /// </summary>
    [Theory]
    [InlineData("charge.frobnicated")]
    [InlineData("")]
    [InlineData(null)]
    public void Parse_AnUnrecognisedType_IsUnknown(string? wire)
    {
        Assert.Equal(PaymentWebhookEventType.Unknown, PaymentWebhookEventTypes.Parse(wire));
    }
}
