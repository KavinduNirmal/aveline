using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Payments.DTOs;

namespace Aveline.Api.Tests;

/// <summary>
/// M19 — the wire shapes cannot carry card data. Constraint C11 is an invariant of the request
/// surface, not a promise: if a DTO grew a card number, CVC, expiry, PAN or token field, a client
/// could send it to Aveline, and the only safe design is that there is no field to send.
/// </summary>
/// <remarks>
/// The scan is a serialiser test as well as a reflection test: a property could be serialised under
/// a wire name that differs from its CLR name, so the serialised JSON is what is checked against the
/// forbidden vocabulary.
/// </remarks>
public class PaymentDtosTests
{
    /// <summary>Every token a card-carrying field would have to be named with, case-insensitively.</summary>
    private static readonly string[] ForbiddenTokens =
    [
        "card", "pan", "cvc", "cvv", "expiry", "expiresmonth", "expiresyear", "expmonth", "expyear",
        "token", "creditcard", "cardnumber", "securitycode", "trackdata", "iban", "accountnumber",
    ];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public static TheoryData<Type, object> PaymentDtos => new()
    {
        { typeof(CreateTopUpCheckoutRequest), new CreateTopUpCheckoutRequest("pack_500") },
        {
            typeof(CreateSubscriptionCheckoutRequest),
            new CreateSubscriptionCheckoutRequest(PlanTier.Bloom, "month")
        },
        {
            typeof(TopUpCheckoutResponse),
            new TopUpCheckoutResponse(
                Guid.CreateVersion7(), "mock", "RequiresAction", "pack_500", 500m, 9000m, "LKR",
                "https://example.test/checkout", DateTime.UtcNow.AddMinutes(30))
        },
        {
            typeof(PaymentIntentResponse),
            new PaymentIntentResponse(
                Guid.CreateVersion7(), "mock", "mock_1", "BlossomTopUp", "RequiresAction", 9000m,
                "LKR", "https://example.test/checkout", null, null, DateTime.UtcNow, null,
                DateTime.UtcNow.AddMinutes(30))
        },
        { typeof(TopUpPackView), new TopUpPackView("pack_500", 500m, 9000m, "LKR") },
    };

    [Theory]
    [MemberData(nameof(PaymentDtos))]
    public void NoPaymentDto_ExposesACardField(Type type, object instance)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        foreach (var property in properties)
        {
            var name = Normalize(property.Name);
            Assert.DoesNotContain(ForbiddenTokens, forbidden => name.Contains(forbidden, StringComparison.Ordinal));
        }

        var json = JsonSerializer.Serialize(instance, type, Json).ToLowerInvariant();
        foreach (var forbidden in ForbiddenTokens)
        {
            Assert.DoesNotContain($"\"{forbidden}", json, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// Guards the guard: if the forbidden vocabulary were reduced to something that matches nothing,
    /// the assertions above would pass vacuously. A field named <c>CardNumber</c> must be caught.
    /// </summary>
    [Fact]
    public void TheForbiddenVocabulary_CatchesACardField()
    {
        var name = Normalize("CardNumber");

        Assert.Contains(ForbiddenTokens, forbidden => name.Contains(forbidden, StringComparison.Ordinal));
    }

    private static string Normalize(string value) =>
        Regex.Replace(value, "[^A-Za-z0-9]", string.Empty).ToLowerInvariant();
}
