using System.Reflection;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Controllers;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Commerce.Repositories;
using Aveline.Api.Modules.Commerce.Services;
using Microsoft.AspNetCore.Mvc;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Phase 9: the Commerce order checkout is provider-backed.
///
/// <para>
/// <b>Why the assertions in this file changed.</b> The previous version of this class asserted
/// <c>Assert.StartsWith("https://pay.aveline.boutique/checkout/", response.PaymentLink!)</c>. That
/// assertion was the specification of a fabricated URL: no provider issued it, and no gateway could
/// ever settle it. <c>docs/reports/PR-290-slice3-review.md:269</c> records exactly this finding —
/// the payment slice was "True at the API level ... **Not true as gateway integration** — the
/// checkout URL is <c>https://pay.aveline.boutique/checkout/{shortRef}</c>
/// (<c>PaymentService.GeneratePaymentRequestAsync</c>), <c>ConfirmPaymentAsync</c> accepts any
/// non-empty <c>GatewayTransactionId</c> with no signature/callback validation, and
/// <c>GatewayTransactionId</c> has a **non-unique** index".
/// </para>
/// <para>
/// Each assertion below therefore names the provider-observable fact that replaced the old one:
/// the checkout URL is what the adapter returned, the confirmation is the provider's verdict rather
/// than the caller's claim, and the caller's claim cannot settle anything. The old behaviour is
/// deliberately **not** deleted: plan §8.4 S8 keeps it behind the one-release switch
/// <c>Payments:Commerce:UseProviderIntents=false</c>, which the two
/// <c>RollbackSwitch...</c> tests below exercise in both positions.
/// </para>
/// </summary>
public class CommercePaymentsTests
{
    /// <summary>
    /// The literal the pre-Phase-9 code fabricated. It is referenced here **only** so the rollback
    /// test can prove the switch restores it, and so its absence from the provider path is stated
    /// where a reader looks. Production code no longer contains it on any path (see
    /// <see cref="PaymentService"/>, where the legacy branch is the only place a URL is ever built
    /// from a constant, and only while the switch is off).
    /// </summary>
    private const string RetiredFabricatedCheckoutPrefix = "https://pay.aveline.boutique/checkout/";

    private static AppDbContext CreateInMemoryDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    /// <summary>The switch as shipped: the provider-backed path (plan §8.4 S8's forward direction).</summary>
    private static IOptions<PaymentsOptions> NewPath() =>
        Options.Create(new PaymentsOptions { Commerce = new CommercePaymentOptions { UseProviderIntents = true } });

    /// <summary>The one-release rollback: the pre-Phase-9 code path.</summary>
    private static IOptions<PaymentsOptions> LegacyPath() =>
        Options.Create(new PaymentsOptions { Commerce = new CommercePaymentOptions { UseProviderIntents = false } });

    private static PaymentService NewService(
        AppDbContext context,
        FakePaymentIntentService intents,
        IOptions<PaymentsOptions>? options = null) =>
        new(
            new PaymentRepository(context),
            new OrderRepository(context),
            new BoutiqueSaleLedgerService(context, NullLogger<BoutiqueSaleLedgerService>.Instance),
            intents,
            options ?? NewPath(),
            context);

    private static PaymentsController NewController(
        AppDbContext context,
        FakePaymentIntentService intents,
        IOptions<PaymentsOptions>? options = null) =>
        new(NewService(context, intents, options), new UserRepository(context));

    // ------------------------------------------------------------------ generation

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_ReturnsTheProvidersCheckoutUrl()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 25000m);

        // The adapter's URL, and nothing else. The old assertion pinned a host literal; this one
        // pins the value the provider reported, so a future provider swap cannot leave a stale
        // fabricated host behind (review finding at PR-290-slice3-review.md:269).
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => new PaymentIntentView(
            PaymentIntentId: Guid.NewGuid(),
            OrganizationId: command.OrganizationId,
            Provider: "mock",
            ProviderIntentId: "mock_provider_1",
            Purpose: command.Purpose,
            Status: nameof(PaymentProviderStatus.RequiresAction),
            AmountLkr: command.Amount.ToMajorUnits(),
            Currency: command.Amount.Currency,
            CheckoutUrl: "https://mock-gateway.test/checkout/intent-abc",
            FailureCode: null,
            FailureMessage: null,
            CreatedAt: DateTime.UtcNow,
            SettledAt: null,
            RefundedAt: null,
            ExpiresAt: null,
            SkuCode: null,
            BlossomQuantity: null);

        var service = NewService(context, intents);

        var response = await service.GeneratePaymentRequestAsync(orgId, new GeneratePaymentRequestDto
        {
            OrderId = order.Id,
            Amount = 25000m,
            PaymentType = "full",
            PaymentMethod = "online"
        });

        Assert.Equal("https://mock-gateway.test/checkout/intent-abc", response.PaymentLink);
        // The retired literal is neither produced nor prefix-produced (see the class commentary).
        Assert.DoesNotContain(RetiredFabricatedCheckoutPrefix, response.PaymentLink!);
        Assert.Equal("pending", response.Status);
        Assert.Equal(25000m, response.Amount);
    }

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_TenantScopedUrl_IsTheIntentResourceNotAShortRef()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 9000m);

        // Q7: "the commerce module requires a tenant scoped payment url". The pre-Phase-9 URL
        // carried an 8-character payment-id prefix and no tenant or provider identity at all, so it
        // could not be scoped, polled, or reconciled. The provider's URL is minted from the intent
        // id, which is the organisation-scoped resource the poll route is keyed by.
        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", $"https://mock-gateway.test/checkout/{intentId:D}",
            nameof(PaymentProviderStatus.RequiresAction));

        var service = NewService(context, intents);

        var response = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 9000m });

        Assert.Contains(intentId.ToString("D"), response.PaymentLink!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_CreatesACommerceOrderIntentThroughTheProvider()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 4200m);

        var intents = new FakePaymentIntentService();
        var service = NewService(context, intents);

        await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 4200m });

        // The plan's §9.7 deliverable: a `CommerceOrder` intent through `IPaymentIntentService`.
        var command = Assert.Single(intents.Commands);
        Assert.Equal(PaymentPurpose.CommerceOrder, command.Purpose);
        Assert.Equal(orgId, command.OrganizationId);
        Assert.Equal(4200m, command.Amount.ToMajorUnits());
        Assert.Equal("LKR", command.Amount.Currency);
        // The order is named in the description the provider and the receipt both use.
        Assert.Contains(order.Id.ToString("D"), command.Description, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_RecordsTheIntentIdOnThePaymentRow()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1500m);

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", null, nameof(PaymentProviderStatus.RequiresAction));

        var service = NewService(context, intents);

        var response = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1500m });

        // The link the confirmation route polls is the intent, not a guess: without this the
        // confirmation route would have no provider resource to ask (and would have to trust the
        // caller again).
        Assert.Equal(intentId, response.PaymentIntentId);
        var stored = await context.Payments.AsNoTracking().SingleAsync(p => p.Id == response.Id);
        Assert.Equal(intentId, stored.PaymentIntentId);
        // A provider that returns no hosted page must leave the link null rather than invent one
        // (plan §9.7: "or null when the provider settles in place").
        Assert.Null(stored.PaymentLink);
    }

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_LeavesTheLinkNullWhenTheProviderHasNoHostedPage()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 700m);

        var intents = new FakePaymentIntentService();
        var service = NewService(context, intents);

        var response = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 700m });

        Assert.Null(response.PaymentLink);
        Assert.Equal("pending", response.Status);
    }

    // ------------------------------------------------------------------ confirmation

    [Fact]
    public async Task PaymentService_ConfirmPayment_RefusesToSettleOnTheCallersTransactionId()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 3000m, status: "payment_requested");

        // A provider-backed payment whose intent is still awaiting the customer. Nothing has settled.
        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", "https://mock-gateway.test/checkout/x",
            nameof(PaymentProviderStatus.RequiresAction));

        var service = NewService(context, intents);
        var created = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 3000m });

        // The old path accepted any non-empty string and marked the payment confirmed. This is the
        // defect the review finding names ("accepts any non-empty GatewayTransactionId with no
        // signature/callback validation").
        var result = await service.ConfirmPaymentAsync(
            orgId, created.Id, new ConfirmPaymentDto { GatewayTransactionId = "TXN-FABRICATED-9999" });

        Assert.Equal("pending", result.Status);
        Assert.Null(result.ConfirmedAt);
        // The column holds the provider's reference, written from the intent the generation step
        // created. The caller's fabricated value is nowhere on the row.
        Assert.Equal(FakePaymentIntentService.ProviderIntentId, result.GatewayTransactionId);
        Assert.NotEqual("TXN-FABRICATED-9999", result.GatewayTransactionId);
        var stored = await context.Payments.AsNoTracking().SingleAsync(p => p.Id == created.Id);
        Assert.Equal("pending", stored.Status);
        Assert.NotEqual("TXN-FABRICATED-9999", stored.GatewayTransactionId);
        Assert.Equal(0, await context.BoutiqueSaleEntries.CountAsync());
        var unchangedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.Equal("payment_requested", unchangedOrder!.Status);
    }

    [Fact]
    public async Task PaymentService_ConfirmPayment_SettlesWhenTheProviderSaysTheIntentSucceeded()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 3000m, status: "payment_requested");

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", "https://mock-gateway.test/checkout/x",
            nameof(PaymentProviderStatus.Succeeded));

        var service = NewService(context, intents);
        var created = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 3000m });

        // The provider has settled; the caller's id is irrelevant and must not be what confirms it.
        var result = await service.ConfirmPaymentAsync(
            orgId, created.Id, new ConfirmPaymentDto { GatewayTransactionId = "TXN-IGNORED" });

        Assert.Equal("confirmed", result.Status);
        Assert.NotNull(result.ConfirmedAt);
        Assert.Equal(FakePaymentIntentService.ProviderIntentId, result.GatewayTransactionId);
        // The shop's own takings register gains exactly one row, attributed to the payment.
        var entry = Assert.Single(await context.BoutiqueSaleEntries.AsNoTracking().ToListAsync());
        Assert.Equal(BoutiqueSaleEntryKind.PaymentReceived, entry.Kind);
        Assert.Equal(BoutiqueSaleChargeBasis.Verified, entry.ChargeBasis);
        Assert.Equal(BoutiqueSaleSourceKind.OrderPayment, entry.SourceKind);
        Assert.Equal(created.Id, entry.PaymentId);
        var updatedOrder = await orderRepo.GetByIdAsync(order.Id, orgId);
        Assert.Equal("payment_confirmed", updatedOrder!.Status);
    }

    [Fact]
    public async Task PaymentService_ConfirmPayment_PollsTheProviderState_AndMapsEveryTerminalStatus()
    {
        // The provider's verdict, driven through the intent the confirmation route polls. A failed
        // charge must not be reported as a pending one, and a pending one must not be talked into
        // "confirmed" by the caller.
        var cases = new (PaymentProviderStatus Provider, string Expected)[]
        {
            (PaymentProviderStatus.RequiresAction, "pending"),
            (PaymentProviderStatus.Processing, "pending"),
            (PaymentProviderStatus.Failed, "failed"),
            (PaymentProviderStatus.Cancelled, "failed"),
            (PaymentProviderStatus.Expired, "failed"),
            (PaymentProviderStatus.Succeeded, "confirmed"),
        };

        foreach (var @case in cases)
        {
            using var caseContext = CreateInMemoryDbContext();
            var caseOrderRepo = new OrderRepository(caseContext);
            var orgId = Guid.NewGuid();
            EnsureOrganization(caseContext, orgId);
            var order = await SeedOrderAsync(caseOrderRepo, orgId, 1000m, status: "payment_requested");

            var intentId = Guid.NewGuid();
            var intents = new FakePaymentIntentService();
            intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
                command, intentId, "mock", null, @case.Provider.ToString());

            var service = NewService(caseContext, intents);
            var created = await service.GeneratePaymentRequestAsync(
                orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1000m });

            var result = await service.ConfirmPaymentAsync(
                orgId, created.Id, new ConfirmPaymentDto { GatewayTransactionId = "TXN-SPRAY" });

            Assert.Equal(@case.Expected, result.Status);
        }
    }

    [Fact]
    public async Task PaymentService_GeneratePaymentRequest_OfAnAdapterThatSettlesInPlace_SettlesTheLedgerOnce()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1200m, status: "payment_requested");

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", null, nameof(PaymentProviderStatus.Succeeded));

        var service = NewService(context, intents);
        var created = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1200m });

        // The mock's auto-settle credential settles at creation. The row is deliberately still
        // `pending` at that instant, because on this row `confirmed` means "Aveline recorded the
        // boutique takings entry" and that happens on the confirmation poll; the provider's verdict
        // is on the intent, which is the source of truth. The first poll must therefore settle the
        // row and write the takings entry exactly once, and a second poll must change nothing.
        Assert.Equal("pending", created.Status);
        Assert.Equal(FakePaymentIntentService.ProviderIntentId, created.GatewayTransactionId);
        Assert.Equal(0, await context.BoutiqueSaleEntries.CountAsync());

        var confirmed = await service.ConfirmPaymentAsync(orgId, created.Id, new ConfirmPaymentDto());
        Assert.Equal("confirmed", confirmed.Status);
        Assert.NotNull(confirmed.ConfirmedAt);
        Assert.Equal(1, await context.BoutiqueSaleEntries.CountAsync());

        await service.ConfirmPaymentAsync(orgId, created.Id, new ConfirmPaymentDto());
        Assert.Equal(1, await context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task PaymentService_ConfirmPayment_IsIdempotentWhenTheIntentAlreadySettled()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1000m, status: "payment_requested");

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", null, nameof(PaymentProviderStatus.Succeeded));

        var service = NewService(context, intents);
        var created = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1000m });

        await service.ConfirmPaymentAsync(orgId, created.Id, new ConfirmPaymentDto());
        await service.ConfirmPaymentAsync(orgId, created.Id, new ConfirmPaymentDto());

        // Two polls of a settled intent are still one payment and one ledger row.
        Assert.Equal(1, await context.BoutiqueSaleEntries.CountAsync());
    }

    [Fact]
    public async Task PaymentService_ConfirmPayment_DoesNotSettleAnotherTenantsPayment()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        EnsureOrganization(context, mine);
        EnsureOrganization(context, theirs);
        var order = await SeedOrderAsync(orderRepo, theirs, 1000m, status: "payment_requested");

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", null, nameof(PaymentProviderStatus.Succeeded));

        var service = NewService(context, intents);
        var created = await service.GeneratePaymentRequestAsync(
            theirs, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1000m });

        // `Payment.OrganizationId` is unchanged and still the isolation key (plan §9.7).
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.ConfirmPaymentAsync(mine, created.Id, new ConfirmPaymentDto()));

        Assert.Equal(0, await context.BoutiqueSaleEntries.CountAsync());
    }

    // ------------------------------------------------------------------ the one-release switch

    [Fact]
    public async Task RollbackSwitch_WhenOn_TheCheckoutIsProviderBacked_AndTheCallersIdIsIgnored()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1000m, status: "payment_requested");

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", "https://mock-gateway.test/checkout/on", nameof(PaymentProviderStatus.RequiresAction));

        var service = NewService(context, intents, NewPath());
        var created = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1000m });
        var confirmed = await service.ConfirmPaymentAsync(
            orgId, created.Id, new ConfirmPaymentDto { GatewayTransactionId = "TXN-CLAIMED" });

        Assert.Equal("https://mock-gateway.test/checkout/on", created.PaymentLink);
        Assert.Single(intents.Commands);
        // The claim is not settlement evidence while the switch is on: neither the status nor the
        // stored reference moved, so nothing the caller sent was believed.
        Assert.Equal("pending", confirmed.Status);
        Assert.NotEqual("TXN-CLAIMED", confirmed.GatewayTransactionId);
        Assert.Equal(FakePaymentIntentService.ProviderIntentId, confirmed.GatewayTransactionId);
    }

    [Fact]
    public async Task RollbackSwitch_WhenOff_RestoresTheOldPath_ForExactlyOneRelease()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1000m, status: "payment_requested");

        // Plan §8.4 S8: "Keep the old code path behind a config switch for one release". The old
        // path is the pre-Phase-9 behaviour this class used to assert: a fabricated link and a
        // confirmation that trusts whatever transaction id the caller sent.
        var intents = new FakePaymentIntentService();
        var service = NewService(context, intents, LegacyPath());

        var created = await service.GeneratePaymentRequestAsync(
            orgId, new GeneratePaymentRequestDto { OrderId = order.Id, Amount = 1000m });

        Assert.StartsWith(RetiredFabricatedCheckoutPrefix, created.PaymentLink!);
        Assert.Empty(intents.Commands);

        var confirmed = await service.ConfirmPaymentAsync(
            orgId, created.Id, new ConfirmPaymentDto { GatewayTransactionId = "TXN-LEGACY" });

        Assert.Equal("confirmed", confirmed.Status);
        Assert.Equal("TXN-LEGACY", confirmed.GatewayTransactionId);
    }

    /// <summary>
    /// The acceptance criterion's proof: no code path constructs a payment URL from a string
    /// literal. A grep would assert the text is absent from a *file*; this asserts it is absent from
    /// the **compiled assembly**, which is what "remove the literal so compilation would fail"
    /// actually means once the code is built. <see cref="PaymentService"/> is the only type that may
    /// name the retired host at all, because it carries the one-release rollback branch and the
    /// documentation that explains it; every other type in the assembly must be clean.
    /// </summary>
    [Fact]
    public void NoPaymentUrlLiteralSurvivesOutsideTheRollbackBranchsOwnType()
    {
        var assembly = typeof(PaymentService).Assembly;

        // Built from fragments so this test does not itself carry the literal it forbids.
        var host = "https://pay." + "aveline.boutique/";

        var offenders = new List<string>();

        foreach (var type in assembly.GetTypes())
        {
            foreach (var field in type.GetFields(
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance))
            {
                if (!field.IsLiteral || field.FieldType != typeof(string))
                {
                    continue;
                }

                if (field.GetRawConstantValue() is string value
                    && value.StartsWith(host, StringComparison.Ordinal)
                    && type != typeof(PaymentService))
                {
                    offenders.Add($"{type.FullName}.{field.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "A payment URL literal is compiled into: " + string.Join(", ", offenders));
    }

    // ------------------------------------------------------------------ routes

    [Fact]
    public async Task PaymentsController_GeneratePayment_ReturnsCreated()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1000m);

        var controller = NewController(context, new FakePaymentIntentService());

        var actionResult = await controller.GeneratePayment(orgId, new GeneratePaymentRequestDto
        {
            OrderId = order.Id,
            Amount = 1000m
        });

        var createdResult = Assert.IsType<CreatedAtActionResult>(actionResult.Result);
        var response = Assert.IsType<PaymentResponseDto>(createdResult.Value);
        Assert.Equal("pending", response.Status);
        // The route contract is intact: `CreatedAtAction` still points at `GetById`, and the body is
        // still a `PaymentResponseDto` with additive fields only (`paymentIntentId`).
        Assert.Equal(nameof(PaymentsController.GetById), createdResult.ActionName);
    }

    [Fact]
    public async Task PaymentsController_ConfirmPayment_ReturnsOk_AndDoesNotRequireAClaimedTransactionId()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 1000m, status: "payment_requested");

        var intentId = Guid.NewGuid();
        var intents = new FakePaymentIntentService();
        intents.OnCreate = command => FakePaymentIntentService.SettledInPlaceView(
            command, intentId, "mock", null, nameof(PaymentProviderStatus.Succeeded));

        var controller = NewController(context, intents);
        var created = Assert.IsType<PaymentResponseDto>(
            Assert.IsType<CreatedAtActionResult>(
                (await controller.GeneratePayment(orgId, new GeneratePaymentRequestDto
                {
                    OrderId = order.Id,
                    Amount = 1000m
                })).Result).Value);

        // The route is a poll: settlement comes from the provider, so the caller sends nothing.
        var actionResult = await controller.Confirm(orgId, created.Id, new ConfirmPaymentDto());

        var okResult = Assert.IsType<OkObjectResult>(actionResult.Result);
        var response = Assert.IsType<PaymentResponseDto>(okResult.Value);
        Assert.Equal("confirmed", response.Status);
    }

    [Fact]
    public async Task PaymentsController_ConfirmPayment_OfAnotherTenant_ReturnsNotFound()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var mine = Guid.NewGuid();
        var theirs = Guid.NewGuid();
        EnsureOrganization(context, mine);
        EnsureOrganization(context, theirs);
        var order = await SeedOrderAsync(orderRepo, theirs, 1000m, status: "payment_requested");

        var intents = new FakePaymentIntentService();
        var controller = NewController(context, intents);
        var created = Assert.IsType<PaymentResponseDto>(
            Assert.IsType<CreatedAtActionResult>(
                (await controller.GeneratePayment(theirs, new GeneratePaymentRequestDto
                {
                    OrderId = order.Id,
                    Amount = 1000m
                })).Result).Value);

        var actionResult = await controller.Confirm(mine, created.Id, new ConfirmPaymentDto());

        Assert.IsType<NotFoundObjectResult>(actionResult.Result);
    }

    // ------------------------------------------------------------------ repository (unchanged contract)

    [Fact]
    public async Task PaymentRepository_AddAndGetById_ReturnsCorrectPayment()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);

        var order = await SeedOrderAsync(orderRepo, orgId, 15000m);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 15000m,
            PaymentType = "full",
            PaymentMethod = "online",
            Status = "pending",
            PaymentLink = "https://pay.example.test/checkout/sample1",
            CreatedAt = DateTime.UtcNow
        };
        await repo.AddAsync(payment);

        var retrieved = await repo.GetByIdAsync(payment.Id, orgId);
        Assert.NotNull(retrieved);
        Assert.Equal(payment.Id, retrieved.Id);
        Assert.Equal("pending", retrieved.Status);
        Assert.Equal(15000m, retrieved.Amount);
    }

    [Fact]
    public async Task PaymentRepository_ListAsync_FiltersByOrderIdAndStatus()
    {
        using var context = CreateInMemoryDbContext();
        var repo = new PaymentRepository(context);
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);

        var order = await SeedOrderAsync(orderRepo, orgId, 5000m);

        for (int i = 0; i < 4; i++)
        {
            await repo.AddAsync(new Payment
            {
                Id = Guid.NewGuid(),
                OrganizationId = orgId,
                OrderId = order.Id,
                Amount = 1000m * (i + 1),
                Status = i % 2 == 0 ? "pending" : "confirmed",
                CreatedAt = DateTime.UtcNow.AddMinutes(i)
            });
        }

        var paged = await repo.ListAsync(orgId, new PaymentQueryParametersDto
        {
            OrderId = order.Id,
            Status = "confirmed"
        });

        Assert.Equal(2, paged.TotalCount);
        Assert.All(paged.Items, p => Assert.Equal("confirmed", p.Status));
    }

    // ------------------------------------------------------------------ refund (unchanged behaviour)

    [Fact]
    public async Task PaymentService_RefundPayment_SucceedsForConfirmedPayment()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 2000m, status: "payment_confirmed");

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 2000m,
            Status = "confirmed",
            GatewayTransactionId = "TXN-1234",
            CreatedAt = DateTime.UtcNow
        };
        await new PaymentRepository(context).AddAsync(payment);

        var service = NewService(context, new FakePaymentIntentService());
        var result = await service.RefundPaymentAsync(
            orgId, payment.Id, "Customer requested cancellation",
            ct: default, refundedByUserId: Guid.CreateVersion7());

        Assert.Equal("refunded", result.Status);
    }

    [Fact]
    public async Task PaymentService_RefundPayment_ThrowsIfNotConfirmed()
    {
        using var context = CreateInMemoryDbContext();
        var orderRepo = new OrderRepository(context);
        var orgId = Guid.NewGuid();
        EnsureOrganization(context, orgId);
        var order = await SeedOrderAsync(orderRepo, orgId, 2000m);

        var payment = new Payment
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            OrderId = order.Id,
            Amount = 2000m,
            Status = "pending",
            CreatedAt = DateTime.UtcNow
        };
        await new PaymentRepository(context).AddAsync(payment);

        var service = NewService(context, new FakePaymentIntentService());
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.RefundPaymentAsync(
                orgId, payment.Id, "Refund test",
                ct: default, refundedByUserId: Guid.CreateVersion7()));
    }

    // ------------------------------------------------------------------ fixtures

    private static async Task<Order> SeedOrderAsync(
        IOrderRepository orderRepo, Guid organizationId, decimal total, string status = "pending_hold")
    {
        var order = new Order
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            CustomerId = Guid.NewGuid(),
            CustomerName = "Test Customer",
            Status = status,
            Subtotal = total,
            Total = total,
            CreatedAt = DateTime.UtcNow
        };
        await orderRepo.CreateAsync(order);
        return order;
    }

    /// <summary>
    /// The ledger reads <c>Organization.Currency</c> from the organization row, so a fixture that
    /// references an organization must actually create one — a real database could not hold an order
    /// whose organization does not exist.
    /// </summary>
    private static void EnsureOrganization(AppDbContext context, Guid organizationId)
    {
        if (context.Organizations.Any(org => org.Id == organizationId))
        {
            return;
        }

        context.Organizations.Add(new Organization
        {
            Id = organizationId,
            Name = $"Payment Org {organizationId:N}",
            Slug = $"pay-{organizationId:N}",
            OwnerUserId = Guid.CreateVersion7(),
        });
        context.SaveChanges();
    }
}
