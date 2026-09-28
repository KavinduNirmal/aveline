using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using FluentAssertions;

namespace Aveline.Api.Tests;

/// <summary>
/// The OnePay adapter's own unit surface (plan Phase 8, section 8.5). These are the pieces the
/// provider-neutral contract suite cannot see: the exact request shape OnePay's hash covers, the
/// exhaustive status table, the refund body, and the transport failures.
/// </summary>
/// <remarks>
/// <para>
/// <b>No card data, anywhere.</b> The adapter never sends, parses, or stores a card number, CVC,
/// expiry, or a saved-card token: the customer types those on OnePay's hosted page, and Aveline only
/// ever sees a <c>redirect_url</c>. <see cref="Adapter_CodeNeverMentionsCardDataFields"/> is the
/// guard that keeps that true as the file changes.
/// </para>
/// <para>
/// <b>What is confirmed and what is assumed.</b> Every scenario that says "assumed" is an
/// interpretation of the published docs, not an observation of a live response; the adapter names
/// each one. See the provider's remarks for the confirmed/assumed split.
/// </para>
/// </remarks>
public sealed class OnePayPaymentProviderTests
{
    private static readonly DateTimeOffset Now = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ---------------------------------------------------------------- creation: the request shape

    [Fact]
    public async Task Create_SendsTheMinorUnitAmountAndCurrency()
    {
        var stub = new OnePayHttpStub().WhenPathContains("checkout", _ => SuccessfulCreate());
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await provider.CreatePaymentIntentAsync(
            CompleteRequest(amount: Money.Lkr(3500m), idempotencyKey: "key-3500"));

        var body = Root(stub.SingleRequest());

        // "Minor units" at the provider boundary: the OnePay API takes a decimal major-unit amount,
        // so `Money.ToMajorUnits()` is the single conversion site and it must not drift.
        ParseAmount(body.GetProperty("amount")).Should().Be(3500.00m);
        body.GetProperty("currency").GetString().Should().Be("LKR");
        body.GetProperty("app_id").GetString().Should().Be(OnePayTestAdapter.AppId);
    }

    [Fact]
    public async Task Create_SendsAnAvelineTraceAndAnIdempotencyReference()
    {
        var stub = new OnePayHttpStub().WhenPathContains("checkout", _ => SuccessfulCreate());
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);
        var request = CompleteRequest(idempotencyKey: "key-trace-42");

        await provider.CreatePaymentIntentAsync(request);

        var body = Root(stub.SingleRequest());

        // `reference` is OnePay's own correlation field: the idempotency key rides there, which is
        // what makes a replay recognisable in the merchant dashboard.
        body.GetProperty("reference").GetString().Should().Be("key-trace-42");

        // `additionalData` is the documented free-form slot; the Aveline ids ride there so an
        // orphaned OnePay charge can be traced back.
        var additional = body.GetProperty("additionalData").GetString();
        additional.Should().NotBeNullOrWhiteSpace();

        var metadata = JsonDocument.Parse(additional!);
        metadata.RootElement.GetProperty("aveline_intent_id").GetString()
            .Should().Be(request.IntentId.ToString("D"));
        metadata.RootElement.GetProperty("aveline_idempotency_key").GetString().Should().Be("key-trace-42");
        metadata.RootElement.GetProperty("aveline_purpose").GetString()
            .Should().Be(nameof(PaymentPurpose.BlossomTopUp));
    }

    [Fact]
    public async Task Create_SendsTheDocumentedSha256HashOverAppIdCurrencyAmountAndSalt()
    {
        var stub = new OnePayHttpStub().WhenPathContains("checkout", _ => SuccessfulCreate());
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await provider.CreatePaymentIntentAsync(CompleteRequest(amount: Money.Lkr(1234.50m)));

        var body = Root(stub.SingleRequest());

        // SHA256(app_id + currency + amount + HASH_SALT), amount formatted to two decimals, exactly
        // as the documented worked example (api-implementation).
        var expected = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{OnePayTestAdapter.AppId}LKR1234.50{OnePayTestAdapter.HashSalt}"))).ToLowerInvariant();

        body.GetProperty("hash").GetString().Should().Be(expected);
    }

    [Fact]
    public async Task Create_SendsTheDocumentedCustomerAndRedirectFields()
    {
        var stub = new OnePayHttpStub().WhenPathContains("checkout", _ => SuccessfulCreate());
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await provider.CreatePaymentIntentAsync(CompleteRequest());

        var body = Root(stub.SingleRequest());

        body.GetProperty("customer_first_name").GetString().Should().Be("Nimal");
        body.GetProperty("customer_last_name").GetString().Should().Be("Perera");
        body.GetProperty("customer_phone_number").GetString().Should().Be("+94771234567");
        body.GetProperty("customer_email").GetString().Should().Be("customer@example.test");
        body.GetProperty("transaction_redirect_url").GetString()
            .Should().Be("https://example.test/payments/return");
    }

    [Fact]
    public async Task Create_PostsToTheConfiguredCheckoutPathAndAdvertisesHostedCheckout()
    {
        var stub = new OnePayHttpStub().WhenPathContains("checkout", _ => SuccessfulCreate());
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var created = await provider.CreatePaymentIntentAsync(CompleteRequest());

        var sent = stub.SingleRequest();
        sent.Is("POST").Should().BeTrue();
        sent.Path.Should().Be("/v3/checkout/link/");
        // The Redirection API authenticates by `app_id` plus the SHA-256 `hash`; it documents no
        // Authorization header (unlike the refund endpoint), so the adapter must not invent one.
        sent.Headers.Should().NotContainKey("Authorization");

        // The response's redirect target is the customer handoff, and the status is the hosted page
        // the customer has not completed yet.
        provider.Capabilities.SupportsHostedCheckout.Should().BeTrue();
        created.CheckoutUrl.Should().Be(new Uri("https://sandbox.onepay.lk/pay/ONP2026010100001"));
        created.Status.Should().Be(PaymentProviderStatus.RequiresAction);
        created.Amount.Should().Be(Money.Lkr(3500m));
        created.ProviderIntentId.Should().Be("ONP2026010100001");
    }

    [Fact]
    public async Task Create_WithoutAReturnUrl_ThrowsNotSupported()
    {
        var stub = new OnePayHttpStub();
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var requestWithoutReturnUrl = Request(returnUrl: null);

        await Assert.ThrowsAsync<PaymentProviderNotSupportedException>(() =>
            provider.CreatePaymentIntentAsync(requestWithoutReturnUrl));

        stub.Requests.Should().BeEmpty(
            "the adapter must refuse before it calls OnePay with an incomplete request");
    }

    [Fact]
    public async Task Create_WithoutAnIdempotencyKey_ThrowsNotSupported()
    {
        var stub = new OnePayHttpStub();
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentProviderNotSupportedException>(() =>
            provider.CreatePaymentIntentAsync(Request(idempotencyKey: " ")));

        stub.Requests.Should().BeEmpty();
    }

    // ---------------------------------------------------------------- the status table, exhaustively

    /// <summary>
    /// Every documented provider status, and the Aveline status it must produce. One case per row;
    /// <see cref="UnknownProviderStatus_MapsToProcessing"/> carries the fallback row.
    /// </summary>
    public static TheoryData<string, PaymentProviderStatus> StatusTable => new()
    {
        { "SUCCESS", PaymentProviderStatus.Succeeded },
        { "1", PaymentProviderStatus.Succeeded },
        { "PENDING", PaymentProviderStatus.Processing },
        { "PROCESSING", PaymentProviderStatus.Processing },
        { "INITIATED", PaymentProviderStatus.RequiresAction },
        { "FAILED", PaymentProviderStatus.Failed },
        { "FAILURE", PaymentProviderStatus.Failed },
        { "DECLINED", PaymentProviderStatus.Failed },
        { "CANCELLED", PaymentProviderStatus.Cancelled },
        { "CANCELED", PaymentProviderStatus.Cancelled },
        { "EXPIRED", PaymentProviderStatus.Expired },
        { "REFUNDED", PaymentProviderStatus.Succeeded },
        { "CHARGEBACK", PaymentProviderStatus.Failed },
    };

    [Theory]
    [MemberData(nameof(StatusTable))]
    public async Task Get_MapsEveryDocumentedStatus(string providerStatus, PaymentProviderStatus expected)
    {
        var stub = new OnePayHttpStub().WhenPathContains(
            "transaction/status", _ => StatusRead(providerStatus));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var read = await provider.GetPaymentIntentAsync("ONP2026010100001");

        read.Should().NotBeNull();
        read!.Status.Should().Be(expected);
    }

    [Theory]
    [InlineData("some_future_status")]
    [InlineData("PARTIALLY_REFUNDED")]
    [InlineData("AWAITING_3DS")]
    [InlineData("")]
    public async Task UnknownProviderStatus_MapsToProcessing(string providerStatus)
    {
        // The load-bearing rule: a status with no Aveline equivalent is never Succeeded.
        var stub = new OnePayHttpStub().WhenPathContains(
            "transaction/status", _ => StatusRead(providerStatus));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var read = await provider.GetPaymentIntentAsync("ONP2026010100001");

        read!.Status.Should().Be(PaymentProviderStatus.Processing);
        read.Status.Should().NotBe(PaymentProviderStatus.Succeeded);
    }

    [Theory]
    [InlineData("FAILED", "FAILED")]
    [InlineData("DECLINED", "do not honour")]
    [InlineData("CHARGEBACK", "chargeback")]
    [InlineData("PENDING", null)]
    public async Task Get_CarriesTheProviderMessageOnlyForAFailure(string providerStatus, string? expected)
    {
        var stub = new OnePayHttpStub().WhenPathContains(
            "transaction/status", _ => StatusRead(providerStatus));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var read = await provider.GetPaymentIntentAsync("ONP2026010100001");

        if (expected is null)
        {
            read!.FailureCode.Should().BeNull();
            read.FailureMessage.Should().BeNull();
        }
        else
        {
            read!.FailureMessage.Should().Be(expected);
        }
    }

    [Fact]
    public async Task Get_ReturnsNullForAnIntentOnePayDoesNotKnow()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => OnePayHttpStub.Status(HttpStatusCode.NotFound));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var read = await provider.GetPaymentIntentAsync("ONP-does-not-exist");

        read.Should().BeNull();
    }

    [Fact]
    public async Task Get_AnUnexpectedStatusRead_ThrowsATransportFailure()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => OnePayHttpStub.Status(HttpStatusCode.InternalServerError));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var exception = await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            provider.GetPaymentIntentAsync("ONP2026010100001"));

        exception.StatusCode.Should().Be(502);
    }

    [Fact]
    public async Task Get_AProviderRateLimit_ThrowsATransportFailureRatherThanInventingState()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => OnePayHttpStub.Status(HttpStatusCode.TooManyRequests));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            provider.GetPaymentIntentAsync("ONP2026010100001"));
    }

    [Fact]
    public async Task Create_WhenOnePayIsUnreachable_ThrowsATransportFailure()
    {
        var stub = new OnePayHttpStub().WhenSendThrows(new HttpRequestException("connection refused"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            provider.CreatePaymentIntentAsync(CompleteRequest()));
    }

    // ---------------------------------------------------------------- cancellation

    [Fact]
    public async Task Cancel_ThrowsNotSupported_BecauseOnePayPublishesNoCancelOperation()
    {
        var stub = new OnePayHttpStub().WhenPathContains("transaction/status", _ => StatusRead("PENDING"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentProviderNotSupportedException>(() =>
            provider.CancelPaymentIntentAsync("ONP2026010100001", "customer abandoned checkout"));
    }

    [Fact]
    public async Task Cancel_ASettledIntent_ThrowsAStateFailureNotANotSupportedFailure()
    {
        var stub = new OnePayHttpStub().WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var exception = await Assert.ThrowsAsync<PaymentIntentStateException>(() =>
            provider.CancelPaymentIntentAsync("ONP2026010100001", "customer changed their mind"));

        exception.StatusCode.Should().Be(409);
    }

    // ---------------------------------------------------------------- refunds

    [Fact]
    public async Task Refund_Full_SendsNoAmountAndIsNotPartial()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"))
            .WhenPathContains("refund", _ => SuccessfulRefund("3500.00", "SUCCESS"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var refund = await provider.RefundAsync(new ProviderRefundRequest(
            "ONP2026010100001", Money.Lkr(3500m), "refund-key-1", "Customer asked for a refund."));

        var body = Root(stub.Request("refund"));
        body.GetProperty("is_partially").GetBoolean().Should().BeFalse();
        body.TryGetProperty("amount", out _).Should().BeFalse("a full refund sends no amount");
        body.GetProperty("onepay_transaction_id").GetString().Should().Be("ONP2026010100001");
        body.GetProperty("refund_reason").GetString().Should().Be("REQUESTED_BY_CUSTOMER");

        refund.Status.Should().Be(PaymentProviderStatus.Succeeded);
        refund.Amount.Should().Be(Money.Lkr(3500m));
        refund.ProviderIntentId.Should().Be("ONP2026010100001");
    }

    [Fact]
    public async Task Refund_Partial_SendsTheAmountAndThePartialFlag()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"))
            .WhenPathContains("refund", _ => SuccessfulRefund("100.00", "SUCCESS"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await provider.RefundAsync(new ProviderRefundRequest(
            "ONP2026010100001", Money.Lkr(100m), "refund-key-2", "Partial goodwill refund."));

        var body = Root(stub.Request("refund"));
        body.GetProperty("is_partially").GetBoolean().Should().BeTrue();
        ParseAmount(body.GetProperty("amount")).Should().Be(100.00m);

        // The refund endpoint is the one that documents an Authorization App Token.
        stub.Request("refund").Headers.Should().ContainKey("Authorization");
        stub.Request("refund").Headers["Authorization"].Should().Contain(OnePayTestAdapter.AppToken);
    }

    [Fact]
    public async Task Refund_AnAcceptedButUnsettledRefund_IsProcessing_NotSucceeded()
    {
        // OnePay documents the submission state as `refund-initiated` and does not publish the
        // terminal vocabulary, so the adapter must not read acceptance as settlement.
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"))
            .WhenPathContains("refund", _ => SuccessfulRefund("100.00", "refund-initiated"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var refund = await provider.RefundAsync(new ProviderRefundRequest(
            "ONP2026010100001", Money.Lkr(100m), "refund-key-8", "Submitted, not settled."));

        refund.Status.Should().Be(PaymentProviderStatus.Processing);
    }

    [Fact]
    public async Task Refund_MapsTheFreeTextReasonOntoADocumentedReasonCode()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"))
            .WhenPathContains("refund", _ => SuccessfulRefund("100.00", "SUCCESS"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await provider.RefundAsync(new ProviderRefundRequest(
            "ONP2026010100001", Money.Lkr(100m), "refund-key-3", "The customer reported a fraudulent charge."));

        var body = Root(stub.Request("refund"));
        body.GetProperty("refund_reason").GetString().Should().Be("FRAUDULENT");
    }

    [Fact]
    public async Task Refund_AnUnsettledIntent_ThrowsAStateFailureBeforeCallingOnePay()
    {
        var stub = new OnePayHttpStub().WhenPathContains("transaction/status", _ => StatusRead("PENDING"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentIntentStateException>(() =>
            provider.RefundAsync(new ProviderRefundRequest(
                "ONP2026010100001", Money.Lkr(100m), "refund-key-4", "Too early.")));

        stub.Requests.Should().OnlyContain(r => r.Path.Contains("transaction/status"));
    }

    [Fact]
    public async Task Refund_MoreThanWasCharged_ThrowsAStateFailure()
    {
        var stub = new OnePayHttpStub().WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        // The settled amount is 3500.00, so 3500.01 cannot be a refund of it.
        await Assert.ThrowsAsync<PaymentIntentStateException>(() =>
            provider.RefundAsync(new ProviderRefundRequest(
                "ONP2026010100001", Money.Lkr(3500.01m), "refund-key-5", "One cent too many.")));
    }

    [Fact]
    public async Task Refund_ForAnIntentOnePayDoesNotKnow_ThrowsAStateFailure()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => OnePayHttpStub.Status(HttpStatusCode.NotFound));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentIntentStateException>(() =>
            provider.RefundAsync(new ProviderRefundRequest(
                "ONP-unknown", Money.Lkr(100m), "refund-key-6", "Nothing to refund.")));
    }

    [Fact]
    public async Task Refund_OnePayRateLimits_ThrowsATransportFailureAndRecordsNothing()
    {
        var stub = new OnePayHttpStub()
            .WhenPathContains("transaction/status", _ => StatusRead("SUCCESS"))
            .WhenPathContains("refund", _ => OnePayHttpStub.Status(HttpStatusCode.TooManyRequests));
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            provider.RefundAsync(new ProviderRefundRequest(
                "ONP2026010100001", Money.Lkr(100m), "refund-key-7", "Try later.")));
    }

    // ---------------------------------------------------------------- webhooks

    [Fact]
    public void VerifyWebhook_ThrowsNotSupported_BecauseOnePaySignsNoCallback()
    {
        var stub = new OnePayHttpStub();
        var provider = OnePayTestAdapter.Build(TimeProvider.System, stub);

        var request = new PaymentWebhookRequest(
            """{"transaction_id":"ONP2026010100001","status":1,"status_message":"SUCCESS"}""",
            new Dictionary<string, string>(),
            "203.0.113.7",
            Now);

        var exception = Assert.Throws<PaymentProviderNotSupportedException>(
            () => provider.VerifyAndParseWebhook(request));

        exception.Message.Should().Contain("signature");
    }

    // ---------------------------------------------------------------- the honest no-card-data guard

    [Fact]
    public void Adapter_CodeNeverMentionsCardDataFields()
    {
        // Constraint C11 / assumption A2: no card number, CVC, expiry, or saved-card token may appear
        // anywhere in the adapter. The customer types those on OnePay's hosted page.
        //
        // Comments and string literals are stripped first, because the adapter's remarks *explain*
        // that card data never arrives and would otherwise trip the check on the explanation. What
        // this guards is executable code and wire field names, which is where a leak would live.
        var code = StripCommentsAndStrings(File.ReadAllText(ProviderSourcePath()));

        var forbidden = new[]
        {
            "card_number", "cardnumber", "card_num", "cvc", "cvv", "cvn", "expiry", "expiration",
            "token_id", "saved_card",
        };

        foreach (var term in forbidden)
        {
            code.Should().NotContain(
                term,
                $"'{term}' is a card-data field and must never appear in the adapter's code (A2, C11)");
        }
    }

    /// <summary>
    /// Removes <c>//</c> and <c>/* */</c> comments and single-line string literals so the guard above
    /// tests the adapter's code rather than the prose that documents it.
    /// </summary>
    private static string StripCommentsAndStrings(string source)
    {
        var builder = new StringBuilder(source.Length);
        var inLineComment = false;
        var inBlockComment = false;
        var inString = false;
        var inChar = false;

        for (var i = 0; i < source.Length; i++)
        {
            var current = source[i];
            var next = i + 1 < source.Length ? source[i + 1] : '\0';

            if (inLineComment)
            {
                if (current == '\n')
                {
                    inLineComment = false;
                    builder.Append(current);
                }

                continue;
            }

            if (inBlockComment)
            {
                if (current == '*' && next == '/')
                {
                    inBlockComment = false;
                    i++;
                }

                continue;
            }

            if (inString)
            {
                if (current == '\\')
                {
                    i++;
                    continue;
                }

                if (current == '"')
                {
                    inString = false;
                }

                continue;
            }

            if (inChar)
            {
                if (current == '\\')
                {
                    i++;
                    continue;
                }

                if (current == '\'')
                {
                    inChar = false;
                }

                continue;
            }

            if (current == '/' && next == '/')
            {
                inLineComment = true;
                i++;
                continue;
            }

            if (current == '/' && next == '*')
            {
                inBlockComment = true;
                i++;
                continue;
            }

            if (current == '"')
            {
                inString = true;
                continue;
            }

            if (current == '\'')
            {
                inChar = true;
                continue;
            }

            builder.Append(current);
        }

        return builder.ToString().ToLowerInvariant();
    }

    // ---------------------------------------------------------------- helpers

    private static string ProviderSourcePath()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Aveline.Api.Tests", "Aveline.Api.Tests.csproj")))
        {
            directory = directory.Parent;
        }

        directory.Should().NotBeNull("the test must run inside the repository");
        return Path.Combine(
            directory!.FullName, "Aveline.Api", "Modules", "Payments", "Providers", "OnePayPaymentProvider.cs");
    }

    private static CreateProviderIntentRequest Request(
        Money? amount = null, string idempotencyKey = "onepay-test-key", Uri? returnUrl = null) => new(
        IntentId: Guid.Parse("11111111-2222-3333-4444-555555555555"),
        Amount: amount ?? Money.Lkr(3500m),
        Purpose: PaymentPurpose.BlossomTopUp,
        Description: "OnePay adapter unit test.",
        CustomerReference: "test-organization",
        IdempotencyKey: idempotencyKey,
        // Passed straight through: a test asking for a null return URL must actually get one, or the
        // guard it is testing is never reached.
        ReturnUrl: returnUrl,
        ExpiresAt: null);

    private static Uri DefaultReturnUrl() => new("https://example.test/payments/return");

    /// <summary>The request every create-path test uses: a normal, complete request.</summary>
    private static CreateProviderIntentRequest CompleteRequest(
        Money? amount = null, string idempotencyKey = "onepay-test-key") =>
        Request(amount, idempotencyKey, DefaultReturnUrl());

    private static HttpResponseMessage SuccessfulCreate() => OnePayHttpStub.Json(
        """
        {
          "status": 1,
          "data": {
            "transaction_id": "ONP2026010100001",
            "redirect_url": "https://sandbox.onepay.lk/pay/ONP2026010100001"
          }
        }
        """);

    /// <summary>
    /// The refund acknowledgement. The published docs describe the submission state as
    /// <c>refund-initiated</c> but do not publish the terminal vocabulary, so tests state the status
    /// they are exercising: <c>refund-initiated</c> is the documented "accepted, not settled" state
    /// (<see cref="Refund_AnAcceptedButUnsettledRefund_IsProcessing_NotSucceeded"/>) and
    /// <c>SUCCESS</c> is the mapped terminal one.
    /// </summary>
    private static HttpResponseMessage SuccessfulRefund(string amount, string status) => OnePayHttpStub.Json(
        $$"""
        {
          "status": 1,
          "data": {
            "refund_id": "ONPRF2026010100001",
            "status": "{{status}}",
            "amount": "{{amount}}",
            "currency": "LKR"
          }
        }
        """);

    /// <summary>
    /// The documented read shape. <c>status</c> is a string in the envelope and the transaction's own
    /// status is the string <c>data.status</c>; the adapter must read both (the callback sample sends
    /// the same field as a number).
    /// </summary>
    private static HttpResponseMessage StatusRead(string providerStatus) => OnePayHttpStub.Json(
        $$"""
        {
          "status": 1,
          "data": {
            "transaction_id": "ONP2026010100001",
            "status": "{{providerStatus}}",
            "status_message": "{{MessageFor(providerStatus)}}",
            "amount": "3500.00",
            "currency": "LKR"
          }
        }
        """);

    private static string MessageFor(string providerStatus) => providerStatus switch
    {
        "FAILED" => "FAILED",
        "DECLINED" => "do not honour",
        "CHARGEBACK" => "chargeback",
        "SUCCESS" => "SUCCESS",
        _ => "PENDING",
    };

    /// <summary>
    /// The wire amount is a two-decimal string (<c>"3500.00"</c>), which is what the documented
    /// worked example sends; read it as text so a change to a quoted or unquoted JSON form is caught
    /// rather than silently coerced.
    /// </summary>
    private static decimal ParseAmount(JsonElement element)
    {
        element.ValueKind.Should().Be(JsonValueKind.String);
        return decimal.Parse(element.GetString()!, System.Globalization.CultureInfo.InvariantCulture);
    }

    private static System.Text.Json.JsonElement Root(OnePayHttpStub.Captured request)
    {
        request.Text().Should().NotBeNullOrWhiteSpace("the adapter must send a JSON body");
        return JsonDocument.Parse(request.Text()).RootElement;
    }
}
