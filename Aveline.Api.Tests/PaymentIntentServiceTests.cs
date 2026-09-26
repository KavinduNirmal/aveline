using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Models;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Domain;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Revenue.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsOptions = Microsoft.Extensions.Options.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Plan §6.4's create-and-refund path at the service layer (P2-B2). The §7.5 rows that need a row,
/// a ledger or a provider call but not an HTTP host live here: M4's "no intent row" half, M12's
/// service half, M13's service half, M14, M15 and M16.
/// </summary>
public class PaymentIntentServiceTests
{
    private const string Secret = "whsec_intent-service-test";

    private static readonly DateTimeOffset Now = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly string _databaseName = $"PaymentIntent_{Guid.NewGuid()}";
    private readonly AppDbContext _context;

    public PaymentIntentServiceTests()
    {
        _context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: _databaseName)
            .Options);
    }

    private AppDbContext NewContext() => new(new DbContextOptionsBuilder<AppDbContext>()
        .UseInMemoryDatabase(databaseName: _databaseName)
        .Options);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class StubProviderFactory(IPaymentProvider provider) : IPaymentProviderFactory
    {
        public IPaymentProvider Active => provider;

        public IPaymentProvider Resolve(string providerKey) => provider;
    }

    private sealed class ThrowingProvider : IPaymentProvider
    {
        public string Key => "mock";

        public PaymentProviderCapabilities Capabilities { get; } = new(
            true, true, true, true, true, true);

        public Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
            CreateProviderIntentRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderTransportException("The provider could not be reached.");

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

    private PaymentMetrics CreateMetrics() => new();

    private AuditService CreateAudit() => new(
        new AuditRepository(_context), new AuditRedactor(), new HttpContextAccessor(),
        NullLogger<AuditService>.Instance);

    private PaymentIntentService CreateService(
        IPaymentProvider provider, PaymentMetrics? metrics = null, IEventBus? bus = null,
        TimeProvider? clock = null, PaymentsOptions? payments = null) =>
        new(
            _context,
            new PaymentIntentRepository(_context),
            new StubProviderFactory(provider),
            new IncomeLedgerService(_context, NullLogger<IncomeLedgerService>.Instance),
            bus ?? new InMemoryEventBus(),
            CreateAudit(),
            metrics ?? CreateMetrics(),
            clock ?? new FixedClock(Now),
            NullLogger<PaymentIntentService>.Instance,
            payments is null ? null : OptionsOptions.Create(payments));

    private MockPaymentProvider CreateMock(string? defaultCredential = null, bool autoSettle = true)
    {
        var options = new PaymentsOptions { Currency = "LKR" };
        options.Mock.Enabled = true;
        options.Mock.AutoSettle = autoSettle;
        options.Mock.WebhookSigningSecret = Secret;

        return new MockPaymentProvider(
            OptionsOptions.Create(options),
            new FixedClock(Now),
            NullLogger<MockPaymentProvider>.Instance,
            CreateMetrics(),
            defaultCredential);
    }

    private async Task<Guid> SeedOrganizationAsync()
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId,
            ClerkId = $"pi_{ownerId:N}",
            Email = "pi@aveline.lk",
            FirstName = "Pay",
            LastName = "Intent",
            Username = $"pi_{ownerId:N}",
            UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var org = new Organization
        {
            Name = "Payment Intent Boutique",
            Slug = $"pi-{ownerId:N}",
            OwnerUserId = ownerId,
        };
        _context.Organizations.Add(org);
        await _context.SaveChangesAsync();
        return org.Id;
    }

    private static CreatePaymentIntentCommand TopUpCommand(
        Guid organizationId,
        string skuCode = "pack_500",
        decimal blossomQuantity = 500m,
        decimal priceLkr = 9000m,
        string? idempotencyKey = null,
        DateTime? billingPeriodStart = null,
        DateTime? billingPeriodEnd = null) =>
        new(
            organizationId,
            PaymentPurpose.BlossomTopUp,
            Money.Lkr(priceLkr),
            $"Top-up purchase {skuCode} ({blossomQuantity} Blossoms).",
            skuCode,
            blossomQuantity,
            idempotencyKey,
            Guid.CreateVersion7(),
            billingPeriodStart,
            billingPeriodEnd);

    // ── Create ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Create_PersistsTheHandoff_AndTheResolvedPeriodCap()
    {
        var orgId = await SeedOrganizationAsync();
        var periodStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc);
        var service = CreateService(CreateMock());

        var view = await service.CreateAsync(TopUpCommand(
            orgId,
            idempotencyKey: "checkout-key-1",
            billingPeriodStart: periodStart,
            billingPeriodEnd: periodEnd));

        var row = await _context.PaymentIntents.SingleAsync();

        Assert.Equal(row.Id, view.PaymentIntentId);
        Assert.Equal("mock", view.Provider);
        Assert.Equal("RequiresAction", view.Status);
        Assert.Equal("BlossomTopUp", view.Purpose.ToString());
        Assert.Equal(9000m, view.AmountLkr);
        Assert.Equal("LKR", view.Currency);
        Assert.Null(view.SettledAt);
        Assert.NotNull(row.ProviderIntentId);
        Assert.Equal(row.ProviderIntentId, row.ExternalRef);
        // Deliverable 6: the cap is resolved before payment and persisted on the intent.
        Assert.Equal(periodStart, row.BillingPeriodStart);
        Assert.Equal(periodEnd, row.BillingPeriodEnd);
    }

    [Fact]
    public async Task Create_WithTheSameClientKey_ReturnsTheExistingIntent_WithoutASecondRow()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(CreateMock());
        var command = TopUpCommand(orgId, idempotencyKey: "checkout-key-1");

        var first = await service.CreateAsync(command);
        var second = await service.CreateAsync(command);

        Assert.Equal(first.PaymentIntentId, second.PaymentIntentId);
        Assert.Single(await _context.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task Create_WithTheSameClientKey_AndADifferentSku_ThrowsIdempotencyKeyReuse()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(CreateMock());
        await service.CreateAsync(TopUpCommand(orgId, "pack_500", idempotencyKey: "checkout-key-1"));

        var reuse = await Assert.ThrowsAsync<PaymentIdempotencyKeyReuseException>(() =>
            service.CreateAsync(TopUpCommand(orgId, "pack_1000", idempotencyKey: "checkout-key-1")));

        Assert.Equal(409, reuse.StatusCode);
        Assert.Equal("idempotency-key-reuse", reuse.ErrorCode);
        Assert.Single(await _context.PaymentIntents.ToListAsync());
    }

    /// <summary>
    /// M4 — an unknown outcome is not a failure, and an intent with no provider intent is
    /// unactionable, so a transport failure must leave no row behind.
    /// </summary>
    [Fact]
    public async Task Create_WhenTheProviderTransportFails_PersistsNoIntentRow()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(new ThrowingProvider());

        await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            service.CreateAsync(TopUpCommand(orgId, idempotencyKey: "checkout-timeout")));

        await using var verify = NewContext();
        Assert.Empty(await verify.PaymentIntents.ToListAsync());
    }

    /// <summary>
    /// The same rule as M4, asserted against the context the create ran on rather than a fresh one.
    /// The failed create leaves its entity tracked as <c>Added</c>; unless it is detached, the next
    /// <c>SaveChangesAsync</c> in the same scope (an audit write, for instance) commits the row M4
    /// says must not exist. Payments P5's plan-change path does exactly that, which is how the hole
    /// was found.
    /// </summary>
    [Fact]
    public async Task Create_WhenTheProviderTransportFails_LeavesNothingForTheNextSaveToCommit()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(new ThrowingProvider());

        await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            service.CreateAsync(TopUpCommand(orgId, idempotencyKey: "checkout-timeout-then-audit")));

        await _context.SaveChangesAsync();

        Assert.Empty(await _context.PaymentIntents.ToListAsync());
        await using var verify = NewContext();
        Assert.Empty(await verify.PaymentIntents.ToListAsync());
    }

    // ── Reads and cancellation ──────────────────────────────────────────────────────────────

    [Fact]
    public async Task Get_AnIntentOfAnotherOrganisation_ThrowsNotFound()
    {
        var orgId = await SeedOrganizationAsync();
        var otherOrgId = await SeedOrganizationAsync();
        var service = CreateService(CreateMock());
        var created = await service.CreateAsync(TopUpCommand(orgId, idempotencyKey: "checkout-key-1"));

        await Assert.ThrowsAsync<PaymentIntentNotFoundException>(() =>
            service.GetAsync(otherOrgId, created.PaymentIntentId));
    }

    [Fact]
    public async Task Get_ReportsExpired_WhenTheIntentIsPastItsExpiry_AndUnsettled()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(CreateMock());
        var created = await service.CreateAsync(TopUpCommand(orgId, idempotencyKey: "checkout-key-1"));

        var row = await _context.PaymentIntents.SingleAsync();
        row.ExpiresAt = Now.UtcDateTime.AddMinutes(-1);
        await _context.SaveChangesAsync();

        var view = await service.GetAsync(orgId, created.PaymentIntentId);

        Assert.Equal("Expired", view.Status);
    }

    [Fact]
    public async Task Cancel_AnUnpaidIntent_MovesItToCancelled()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(CreateMock());
        var created = await service.CreateAsync(TopUpCommand(orgId, idempotencyKey: "checkout-key-1"));

        var cancelled = await service.CancelAsync(
            orgId, created.PaymentIntentId, "Customer abandoned the checkout.");

        Assert.Equal("Cancelled", cancelled.Status);
        Assert.Equal("Cancelled", (await _context.PaymentIntents.SingleAsync()).Status.ToString());
    }

    // ── Refunds ─────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Seeds a Succeeded intent whose provider-side charge the returned mock also knows about, so
    /// the refund path can reach the real adapter state.
    /// </summary>
    private async Task<(Guid OrgId, Guid IntentId, string ProviderIntentId, MockPaymentProvider Provider)>
        SeedSettledIntentAsync(decimal amountLkr = 9000m, string? credential = null)
    {
        var orgId = await SeedOrganizationAsync();
        var intentId = Guid.CreateVersion7();
        var mock = CreateMock(credential ?? MockPaymentProvider.SucceedToken, autoSettle: true);

        var provider = await mock.CreatePaymentIntentAsync(new CreateProviderIntentRequest(
            intentId,
            Money.Lkr(amountLkr),
            PaymentPurpose.BlossomTopUp,
            "Top-up purchase pack_500 (500 Blossoms).",
            orgId.ToString(),
            intentId.ToString(),
            null,
            null));

        _context.PaymentIntents.Add(new PaymentIntent
        {
            Id = intentId,
            OrganizationId = orgId,
            Provider = "mock",
            ProviderIntentId = provider.ProviderIntentId,
            ExternalRef = provider.ProviderIntentId,
            Purpose = PaymentPurpose.BlossomTopUp,
            Status = PaymentProviderStatus.Succeeded,
            AmountMinor = (long)(amountLkr * 100m),
            Currency = "LKR",
            PriceLkr = amountLkr,
            SkuCode = "pack_500",
            BlossomQuantity = 500m,
            Description = "Top-up purchase pack_500 (500 Blossoms).",
            IdempotencyKey = "settled-key",
            // A real checkout is initiated by an authenticated owner, and the settlement writes its
            // receipt under the top-up namespace because of it; the refund rule looks up that same
            // identity (plan §9.6).
            CreatedByUserId = Guid.CreateVersion7(),
            CreatedAt = Now.UtcDateTime,
            UpdatedAt = Now.UtcDateTime,
            SettledAt = Now.UtcDateTime,
        });
        _context.IncomeLedgerEntries.Add(new IncomeLedgerEntry
        {
            OrganizationId = orgId,
            Kind = IncomeEntryKind.TopUpPurchase,
            SourceKind = IncomeSourceKind.BlossomTopUp,
            SourceRef = provider.ProviderIntentId,
            ChargeBasis = IncomeChargeBasis.Verified,
            Status = IncomeEntryStatus.Recorded,
            Currency = "LKR",
            Amount = amountLkr,
            Reason = "Top-up purchase pack_500 (500 Blossoms).",
            OccurredAt = Now.UtcDateTime,
            RecordedByUserId = Guid.CreateVersion7(),
        });
        await _context.SaveChangesAsync();

        return (orgId, intentId, provider.ProviderIntentId, mock);
    }

    /// <summary>
    /// M14 — a full refund returns the money in the ledger, moves the intent to <c>Refunded</c>,
    /// and leaves the reconciliation identity balanced.
    /// </summary>
    [Fact]
    public async Task RequestRefund_InFull_MovesTheIntentToRefunded_AndWritesARefundRow()
    {
        var (orgId, intentId, _, provider) = await SeedSettledIntentAsync();
        var service = CreateService(provider);

        var result = await service.RequestRefundAsync(
            orgId, intentId, amountLkr: null, reason: "Customer requested a full refund.");

        Assert.Equal("Refunded", result.Intent.Status);
        Assert.NotNull(result.Intent.RefundedAt);
        // §9.6: the response reports the provider's refund id and the ledger entry it wrote.
        Assert.False(string.IsNullOrWhiteSpace(result.ProviderRefundId));
        Assert.NotEqual(Guid.Empty, result.LedgerEntryId);

        var income = await _context.IncomeLedgerEntries
            .Where(entry => entry.OrganizationId == orgId)
            .ToListAsync();

        Assert.Equal(2, income.Count);
        var refund = income.Single(entry => entry.Kind == IncomeEntryKind.Refund);
        Assert.Equal(refund.Id, result.LedgerEntryId);
        Assert.Equal(refund.SourceRef, result.ProviderRefundId);
        Assert.Equal(IncomeChargeBasis.Verified, refund.ChargeBasis);
        Assert.Equal(9000m, refund.Amount);
        Assert.True(RevenueReconciliation.Of(income).IsBalanced);
    }

    /// <summary>M15 — a refund of an unsettled intent is a state error, not a silent no-op.</summary>
    [Fact]
    public async Task RequestRefund_OnAPendingIntent_ThrowsState409()
    {
        var orgId = await SeedOrganizationAsync();
        var service = CreateService(CreateMock());
        var created = await service.CreateAsync(TopUpCommand(orgId, idempotencyKey: "checkout-key-1"));

        var state = await Assert.ThrowsAsync<PaymentIntentStateException>(() =>
            service.RequestRefundAsync(
                orgId, created.PaymentIntentId, null, "Customer requested a full refund."));

        Assert.Equal(409, state.StatusCode);
        Assert.Equal("payment-intent-state", state.ErrorCode);
    }

    /// <summary>M16 — a provider refusal must not be recorded as money returned.</summary>
    [Fact]
    public async Task RequestRefund_WhenTheProviderRefuses_LeavesTheIntentAndLedgerUntouched()
    {
        var (orgId, intentId, _, provider) = await SeedSettledIntentAsync(
            credential: MockPaymentProvider.RefundFailToken);
        var service = CreateService(provider);

        await Assert.ThrowsAsync<PaymentProviderTransportException>(() =>
            service.RequestRefundAsync(orgId, intentId, null, "Customer requested a full refund."));

        var row = await _context.PaymentIntents.SingleAsync(intent => intent.Id == intentId);
        Assert.Equal(PaymentProviderStatus.Succeeded, row.Status);
        Assert.Null(row.RefundedAt);
        Assert.DoesNotContain(
            await _context.IncomeLedgerEntries.ToListAsync(),
            entry => entry.Kind == IncomeEntryKind.Refund);
    }

    /// <summary>
    /// The shipped revenue rule is kept for the provider route (plan §9.6, decision D8): a refund
    /// requires an existing <c>Verified</c> receipt for the same <c>(SourceKind, SourceRef)</c>. A
    /// settled intent whose receipt is missing is refused with the same
    /// <c>409 refund-not-allowed</c> the admin route returns, and the provider is never asked.
    /// </summary>
    [Fact]
    public async Task RequestRefund_WithNoVerifiedReceipt_IsRefusedBeforeTheProviderIsAsked()
    {
        var (orgId, intentId, _, provider) = await SeedSettledIntentAsync();
        _context.IncomeLedgerEntries.RemoveRange(_context.IncomeLedgerEntries);
        await _context.SaveChangesAsync();

        var service = CreateService(provider);

        var exception = await Assert.ThrowsAsync<RevenueRefundNotAllowedException>(
            () => service.RequestRefundAsync(orgId, intentId, null, "Customer requested a full refund."));

        Assert.Equal("refund-not-allowed", exception.Code);

        var row = await _context.PaymentIntents.SingleAsync(intent => intent.Id == intentId);
        Assert.Null(row.RefundedAt);
        Assert.DoesNotContain(
            await _context.IncomeLedgerEntries.ToListAsync(),
            entry => entry.Kind == IncomeEntryKind.Refund);
    }

    /// <summary>
    /// Plan §9.6 leaves the refund policy unresolved, so the window is configuration. Absent means
    /// "no automatic window; the operator decides"; a configured window refuses a refund struck
    /// outside it without asking the provider.
    /// </summary>
    [Fact]
    public async Task RequestRefund_OutsideTheConfiguredWindow_IsRefused()
    {
        var (orgId, intentId, _, provider) = await SeedSettledIntentAsync();
        var service = CreateService(
            provider,
            clock: new FixedClock(Now.AddDays(8)),
            payments: new PaymentsOptions { RefundWindowDays = 7 });

        var exception = await Assert.ThrowsAsync<PaymentIntentStateException>(
            () => service.RequestRefundAsync(orgId, intentId, null, "Customer requested a full refund."));

        Assert.Equal(409, exception.StatusCode);
        Assert.Contains("window", exception.Message, StringComparison.OrdinalIgnoreCase);

        var row = await _context.PaymentIntents.SingleAsync(intent => intent.Id == intentId);
        Assert.Null(row.RefundedAt);
    }

    /// <summary>The window's other side: inside it, the refund is still the operator's call.</summary>
    [Fact]
    public async Task RequestRefund_InsideTheConfiguredWindow_IsAllowed()
    {
        var (orgId, intentId, _, provider) = await SeedSettledIntentAsync();
        var service = CreateService(
            provider,
            clock: new FixedClock(Now.AddDays(6)),
            payments: new PaymentsOptions { RefundWindowDays = 7 });

        var result = await service.RequestRefundAsync(
            orgId, intentId, null, "Customer requested a full refund.");

        Assert.Equal("Refunded", result.Intent.Status);
    }

    /// <summary>
    /// The default is deliberately "no automatic window" (plan §9.6: the seven-day proposal is not
    /// final), so an unconfigured deployment keeps the operator's discretion.
    /// </summary>
    [Fact]
    public async Task RequestRefund_WithNoConfiguredWindow_IsAllowedHoweverOldTheChargeIs()
    {
        var (orgId, intentId, _, provider) = await SeedSettledIntentAsync();
        var service = CreateService(provider, clock: new FixedClock(Now.AddDays(365)));

        var result = await service.RequestRefundAsync(
            orgId, intentId, null, "Customer requested a full refund.");

        Assert.Equal("Refunded", result.Intent.Status);
    }
}
