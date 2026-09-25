using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Repositories;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments;
using Aveline.Api.Modules.Payments.Domain;
using Aveline.Api.Modules.Payments.Providers;
using Aveline.Api.Modules.Payments.Repositories;
using Aveline.Api.Modules.Payments.Services;
using Aveline.Api.Modules.Revenue.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using OptionsOptions = Microsoft.Extensions.Options.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #404 (Payments P1) — <c>UpsertSubscriptionAsync</c> must write a real price from the
/// price book (G9). Until this slice the column was never assigned, so every subscription was
/// zero-priced, the rollover wrote no <c>Derived</c> charge and MRR read as "not measurable".
///
/// The first test is the issue's own RED case:
/// <c>UpsertSubscriptionAsync_AssignsPriceLkrFromThePriceBook</c>.
///
/// Payments P5 then added <c>Upgrade_WithProrationCapability_CreatesProrationIntent</c> (plan §9.3
/// F3): an immediate upgrade leaves an invoice-like <c>SubscriptionProration</c> charge.
/// </summary>
public class SubscriptionServiceTests
{
    /// <summary>A mid-month instant with a February period end, so the proration is exact.</summary>
    private static readonly DateTimeOffset Now = new(2026, 2, 10, 12, 0, 0, TimeSpan.Zero);

    private readonly AppDbContext _context = new(
        new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"Subscription_{Guid.NewGuid()}")
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

    private AuditService CreateAudit() => new(
        new AuditRepository(_context), new AuditRedactor(),
        new HttpContextAccessor(), NullLogger<AuditService>.Instance);

    /// <summary>The reference adapter that advertises proration (plan §7.3).</summary>
    private static MockPaymentProvider CreateMock()
    {
        var options = new PaymentsOptions { Currency = "LKR" };
        options.Mock.Enabled = true;
        options.Mock.AutoSettle = true;
        options.Mock.WebhookSigningSecret = "whsec_subscription-service-test";

        return new MockPaymentProvider(
            OptionsOptions.Create(options),
            new FixedClock(Now),
            NullLogger<MockPaymentProvider>.Instance,
            new PaymentMetrics());
    }

    private PaymentIntentService CreateIntentService(IPaymentProvider provider) => new(
        _context,
        new PaymentIntentRepository(_context),
        new StubProviderFactory(provider),
        new IncomeLedgerService(_context, NullLogger<IncomeLedgerService>.Instance),
        new InMemoryEventBus(),
        CreateAudit(),
        new PaymentMetrics(),
        new FixedClock(Now),
        NullLogger<PaymentIntentService>.Instance);

    /// <summary>
    /// The full service, with the payment collaborators wired so a proration charge is visible as a
    /// real <c>PaymentIntents</c> row. <paramref name="provider"/> defaults to the prorating mock.
    /// </summary>
    private SubscriptionService CreateService(
        IPaymentProvider? provider = null, TimeProvider? clock = null,
        IPaymentProviderFactory? providers = null)
    {
        var active = provider ?? CreateMock();

        return new SubscriptionService(
            _context,
            new EntitlementResolver(new EntitlementRepository(_context)),
            new BlossomService(
                new BlossomLedgerRepository(_context),
                new UsageRepository(_context),
                new EntitlementResolver(new EntitlementRepository(_context)),
                new InMemoryEventBus(),
                CreateAudit(),
                new ConfigurationBuilder()
                    .AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["Billing:MaxAdjustmentBlossoms"] = "100000",
                    })
                    .Build(),
                NullLogger<BlossomService>.Instance),
            new InMemoryEventBus(),
            CreateAudit(),
            new SubscriptionPriceResolver(new PricingRepository(_context)),
            NullLogger<SubscriptionService>.Instance,
            new ProrationCalculator(new StubProviderFactory(active)),
            CreateIntentService(active),
            clock ?? TimeProvider.System,
            providers ?? new StubProviderFactory(active));
    }

    /// <summary>
    /// A provider subscription whose cancellation the provider refuses. This is the P6 RED case's
    /// double: a cancellation that never reaches the provider is the worst outcome the service can
    /// have, so the refusal must surface and nothing may be persisted.
    /// </summary>
    private sealed class RefusingSubscriptionProvider : IPaymentProvider
    {
        public string Key => "mock";

        public PaymentProviderCapabilities Capabilities { get; } = new(
            SupportsRecurringSubscriptions: true,
            SupportsProration: true,
            SupportsPartialRefunds: true,
            SupportsCancelAtPeriodEnd: true,
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
            throw new PaymentProviderTransportException(
                "The provider refused to schedule the cancellation.");
    }

    /// <summary>An organisation with a provider-backed subscription, for the cancellation cases.</summary>
    private async Task<Guid> SeedProviderBackedSubscriptionAsync()
    {
        var organizationId = await SeedOrganizationAsync(PlanTier.Bloom);
        var periodStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        _context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organizationId,
            PlanTier = PlanTier.Bloom,
            Status = SubscriptionStatus.Active,
            PriceLkr = 3500m,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodStart.AddMonths(1),
            ExternalProvider = "mock",
            ExternalSubscriptionId = "mock_sub_provider_backed",
        });
        await _context.SaveChangesAsync();
        return organizationId;
    }

    private async Task<Guid> SeedOrganizationAsync(PlanTier tier = PlanTier.Seed)
    {
        var ownerId = Guid.CreateVersion7();
        _context.Users.Add(new User
        {
            Id = ownerId, ClerkId = $"sub_{ownerId:N}", Email = "sub@aveline.lk", FirstName = "S",
            LastName = "U", Username = $"sub_{ownerId:N}", UserRole = "owner",
            OrganizationRole = "org:boutique_owner",
        });
        var organization = new Organization
        {
            Name = "Subscription Boutique", Slug = $"sub-{ownerId:N}", OwnerUserId = ownerId,
            PlanTier = tier,
        };
        _context.Organizations.Add(organization);
        await _context.SaveChangesAsync();
        return organization.Id;
    }

    /// <summary>
    /// A priced plan-allowance row, effective well before any timestamp these tests use. The
    /// resolver must find it through <c>BlossomPriceEntries</c> where <c>SkuKind = PlanAllowance</c>.
    /// </summary>
    private void SeedPlanAllowance(PlanTier tier, decimal priceLkr, Guid? organizationId = null) =>
        _context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            PlanTier = organizationId is null ? tier : null,
            OrganizationId = organizationId,
            SkuKind = BlossomSkuKind.PlanAllowance,
            BlossomQuantity = 750m,
            PriceLkr = priceLkr,
            EffectiveFrom = DateTime.UtcNow.AddMonths(-1),
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Phase 1 price-book seed for the subscription price test.",
            CreatedByUserId = Guid.CreateVersion7(),
        });

    private static ChangePlanCommand UpgradeTo(PlanTier tier, Guid organizationId) =>
        new(organizationId, tier, Effective: "immediate", Reason: null,
            ActorUserId: Guid.CreateVersion7(), IdempotencyKey: null, IdempotencyScope: null);

    private static ChangePlanCommand ChangePlanTo(PlanTier tier, Guid organizationId, string? effective) =>
        new(organizationId, tier, effective, Reason: null,
            ActorUserId: Guid.CreateVersion7(), IdempotencyKey: null, IdempotencyScope: null);

    private Task<PaymentIntentRow?> FindProrationIntentAsync(Guid organizationId) =>
        _context.PaymentIntents
            .Where(row => row.OrganizationId == organizationId
                && row.Purpose == PaymentPurpose.SubscriptionProration)
            .Select(row => new PaymentIntentRow(
                row.Purpose, row.Provider, row.AmountMinor, row.PriceLkr, row.PlanTier,
                row.BillingPeriodStart, row.BillingPeriodEnd, row.IdempotencyKey))
            .SingleOrDefaultAsync();

    private sealed record PaymentIntentRow(
        PaymentPurpose Purpose,
        string Provider,
        long AmountMinor,
        decimal PriceLkr,
        PlanTier? PlanTier,
        DateTime? BillingPeriodStart,
        DateTime? BillingPeriodEnd,
        string? IdempotencyKey);

    [Fact]
    public async Task UpsertSubscriptionAsync_AssignsPriceLkrFromThePriceBook()
    {
        var organizationId = await SeedOrganizationAsync();
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var result = await CreateService().ChangePlanAsync(UpgradeTo(PlanTier.Bloom, organizationId));

        Assert.Equal(3500m, result.Subscription.PriceLkr);
        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.Equal(3500m, stored.PriceLkr);
    }

    [Fact]
    public async Task UpsertSubscriptionAsync_LeavesAnUnpricedSubscriptionAtZeroRatherThanInventing()
    {
        // No price-book row exists, so the resolver yields null; the subscription must not be
        // billed. The column is non-nullable, so it stays at its zero default — the production
        // behaviour for every subscription before this slice.
        var organizationId = await SeedOrganizationAsync();

        var result = await CreateService().ChangePlanAsync(UpgradeTo(PlanTier.Orchid, organizationId));

        Assert.Equal(0m, result.Subscription.PriceLkr);
        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.Equal(0m, stored.PriceLkr);
    }

    /// <summary>
    /// Payments P5 (plan §9.3 F3): an immediate upgrade to a more expensive plan must leave the
    /// tenant a <c>SubscriptionProration</c> charge for the remainder of the current period. This is
    /// the phase's RED case (plan §10 Phase 5): before the change the service created no intent at
    /// all, whatever the provider could do.
    /// </summary>
    [Fact]
    public async Task Upgrade_WithProrationCapability_CreatesProrationIntent()
    {
        var organizationId = await SeedOrganizationAsync();
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        // The provider advertises SupportsProration, so its own figure is the one charged. The
        // expected value is computed from the mock's documented formula independently of the
        // service, and 2026-02-10 to 2026-03-01 is 19 of February's 28 days.
        var expected = MockPaymentProvider.ProrateMonthly(
            3500m, new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 10));
        Assert.Equal(2375.00m, expected);

        var result = await CreateService(clock: new FixedClock(Now))
            .ChangePlanAsync(UpgradeTo(PlanTier.Bloom, organizationId));

        var intent = await FindProrationIntentAsync(organizationId);

        Assert.NotNull(intent);
        Assert.Equal(PaymentPurpose.SubscriptionProration, intent.Purpose);
        Assert.Equal("mock", intent.Provider);
        Assert.Equal(237_500L, intent.AmountMinor);
        Assert.Equal(expected, intent.PriceLkr);
        Assert.Equal(PlanTier.Bloom, intent.PlanTier!.Value);
        Assert.Equal(new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc), intent.BillingPeriodStart);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), intent.BillingPeriodEnd);

        // The client needs the id to poll the charge, and the obligation is visible in the result
        // even when the provider could not be reached (the local-fallback case below).
        Assert.NotNull(result.ProrationPaymentIntentId);
        Assert.Equal(2375.00m, result.ProrationAmountLkr);

        // Q2: the higher allowance and the tier are already the tenant's.
        Assert.Equal("Bloom", result.Subscription.PlanTier);
        Assert.Equal(600m, result.BlossomDelta);
    }

    /// <summary>
    /// Plan §9.3: a provider without proration must not stop the charge. Aveline prices it with the
    /// documented local formula and passes it as a normal intent amount.
    /// </summary>
    [Fact]
    public async Task Upgrade_WithProviderWithoutProration_ComputesTheFallbackLocally()
    {
        var organizationId = await SeedOrganizationAsync();
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var expected = PlanChangeProration.Local(
            0m, 3500m, new DateOnly(2026, 3, 1), new DateOnly(2026, 2, 10));

        var result = await CreateService(new ManualPaymentProvider(), new FixedClock(Now))
            .ChangePlanAsync(UpgradeTo(PlanTier.Bloom, organizationId));

        var intent = await FindProrationIntentAsync(organizationId);

        Assert.NotNull(intent);
        Assert.Equal("manual", intent.Provider);
        Assert.Equal(expected, intent.PriceLkr);
        Assert.Equal(2375.00m, intent.PriceLkr);
        Assert.NotNull(result.ProrationPaymentIntentId);
        Assert.Equal(expected, result.ProrationAmountLkr);
    }

    /// <summary>Plan §9.3: a downgrade that takes effect next period moves no money.</summary>
    [Fact]
    public async Task Downgrade_NextPeriod_CreatesNoProrationIntent()
    {
        var organizationId = await SeedOrganizationAsync(PlanTier.Rose);
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var result = await CreateService(clock: new FixedClock(Now))
            .ChangePlanAsync(ChangePlanTo(PlanTier.Bloom, organizationId, "nextPeriod"));

        Assert.Null(await FindProrationIntentAsync(organizationId));
        Assert.Null(result.ProrationPaymentIntentId);
        Assert.Null(result.ProrationAmountLkr);
    }

    /// <summary>
    /// Plan §9.3: the forced immediate downgrade is unchanged. It is a support action that the
    /// three-limit guard polices, and it moves no money however it is applied — the subscription
    /// being left is priced at LKR 3,500, so this is not a comparison against a zero default.
    /// </summary>
    [Fact]
    public async Task ForcedImmediateDowngrade_WithinLimits_CreatesNoProrationIntent()
    {
        var organizationId = await SeedOrganizationAsync(PlanTier.Bloom);
        var periodStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        _context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organizationId,
            PlanTier = PlanTier.Bloom,
            Status = SubscriptionStatus.Active,
            PriceLkr = 3500m,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodStart.AddMonths(1),
        });
        _context.UsageAccounts.Add(new UsageAccount
        {
            OrganizationId = organizationId,
            PeriodStart = periodStart,
            PeriodEnd = periodStart.AddMonths(1),
            MonthlyBlossomLimit = 750m,
            BlossomUsed = 0m,
            BlossomRemaining = 750m,
            PlanTierSnapshot = PlanTier.Bloom,
            Status = UsageAccountStatus.Active,
        });
        await _context.SaveChangesAsync();

        var result = await CreateService(clock: new FixedClock(Now))
            .ChangePlanAsync(ChangePlanTo(PlanTier.Seed, organizationId, "immediate"));

        Assert.Empty(await _context.PaymentIntents
            .Where(row => row.OrganizationId == organizationId
                && row.Purpose == PaymentPurpose.SubscriptionProration)
            .ToListAsync());
        Assert.Null(result.ProrationPaymentIntentId);
        Assert.Null(result.ProrationAmountLkr);
        Assert.Equal("Seed", result.Subscription.PlanTier);
    }

    /// <summary>
    /// An upgrade whose resolved price does not actually rise (no price book row, so the price stays
    /// at its zero default) owes nothing and must not invent a charge.
    /// </summary>
    [Fact]
    public async Task Upgrade_WithNoResolvedPriceRise_CreatesNoProrationIntent()
    {
        var organizationId = await SeedOrganizationAsync();

        var result = await CreateService(clock: new FixedClock(Now))
            .ChangePlanAsync(UpgradeTo(PlanTier.Bloom, organizationId));

        Assert.Null(await FindProrationIntentAsync(organizationId));
        Assert.Null(result.ProrationPaymentIntentId);
        Assert.Null(result.ProrationAmountLkr);
    }

    /// <summary>
    /// Decision Q2: a provider that cannot be reached leaves the charge outstanding. The plan
    /// change, the tier and the higher allowance stand, and the amount is still reported.
    /// </summary>
    [Fact]
    public async Task Upgrade_WhenTheProviderIsUnreachable_KeepsThePlanChangeAndReportsTheObligation()
    {
        var organizationId = await SeedOrganizationAsync();
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var result = await CreateService(new UnreachablePaymentProvider(), new FixedClock(Now))
            .ChangePlanAsync(UpgradeTo(PlanTier.Bloom, organizationId));

        Assert.Null(await FindProrationIntentAsync(organizationId));
        Assert.Null(result.ProrationPaymentIntentId);
        Assert.Equal(2375.00m, result.ProrationAmountLkr);

        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.Equal(PlanTier.Bloom, stored.PlanTier);
    }

    /// <summary>
    /// A provider whose create path is unreachable, so the Q2 path can be asserted. It prorates
    /// like the mock, so the failure under test is the charge creation and nothing else.
    /// </summary>
    private sealed class UnreachablePaymentProvider : IPaymentProvider, IProrationProvider
    {
        public string Key => "mock";

        public PaymentProviderCapabilities Capabilities { get; } = new(
            SupportsRecurringSubscriptions: true,
            SupportsProration: true,
            SupportsPartialRefunds: true,
            SupportsCancelAtPeriodEnd: true,
            SupportsHostedCheckout: true,
            SettlesAsynchronously: true);

        public decimal ComputeProration(ProrationRequest request) =>
            MockPaymentProvider.ProrateMonthly(
                request.MonthlyDifferenceLkr, request.PeriodEnd, request.At);

        public Task<ProviderPaymentIntent> CreatePaymentIntentAsync(
            CreateProviderIntentRequest request, CancellationToken cancellationToken = default) =>
            throw new PaymentProviderTransportException(
                "The configured provider could not be reached while creating the charge.");

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

    [Fact]
    public async Task SeedStaysFreeEvenWhenThePriceBookCarriesARow()
    {
        var organizationId = await SeedOrganizationAsync();
        SeedPlanAllowance(PlanTier.Seed, priceLkr: 999m);
        await _context.SaveChangesAsync();

        // Cancellation is the path that materialises a subscription for an organisation that has
        // never had one, and it does so at the organisation's live tier (Seed).
        await CreateService().CancelAsync(organizationId, reason: null);

        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.Equal(PlanTier.Seed, stored.PlanTier);
        Assert.Equal(0m, stored.PriceLkr);
    }

    // ── Plan §9.5 F5: cancellation, and the provider that must hear about it ─────────────────

    /// <summary>
    /// The phase's first failing test (plan §10 Phase 6). A cancellation the provider refuses is a
    /// hard failure: the row must not record a cancellation that never reached the provider, because
    /// the tenant would then keep being charged while Aveline believes the subscription is ending.
    /// </summary>
    [Fact]
    public async Task Cancel_WhenProviderRefuses_Returns502AndDoesNotPersistCancellation()
    {
        var organizationId = await SeedProviderBackedSubscriptionAsync();
        var service = CreateService(new RefusingSubscriptionProvider(), new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<PaymentProviderTransportException>(
            () => service.CancelAsync(organizationId, "The owner asked to stop renewing."));

        Assert.Equal(502, exception.StatusCode);
        Assert.Equal("payment-provider-error", exception.ErrorCode);

        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.False(stored.CancelAtPeriodEnd);
        Assert.Null(stored.CancelledAt);
    }

    /// <summary>
    /// The same rule stated the other way round: a provider that accepts the cancellation is the
    /// only path on which the flag is persisted, and it is asked for a period-end cancellation.
    /// </summary>
    [Fact]
    public async Task Cancel_WhenTheProviderAccepts_PersistsTheScheduledCancellation()
    {
        var organizationId = await SeedProviderBackedSubscriptionAsync();
        var recording = new RecordingSubscriptionProvider();
        var service = CreateService(recording, new FixedClock(Now));

        var view = await service.CancelAsync(organizationId, "The owner asked to stop renewing.");

        Assert.True(view.CancelAtPeriodEnd);
        Assert.NotNull(view.CancelledAt);
        Assert.Equal("mock_sub_provider_backed", recording.CancelledProviderSubscriptionId);
        Assert.True(recording.CancelledAtPeriodEnd);

        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.True(stored.CancelAtPeriodEnd);
        Assert.NotNull(stored.CancelledAt);
    }

    /// <summary>
    /// Plan §9.5(c): the un-cancel path is only meaningful where the provider supports it. The
    /// <c>manual</c> adapter cannot schedule (or unschedule) a provider-side cancellation, so the
    /// response is the documented <c>501 payment-provider-capability-missing</c>.
    /// </summary>
    [Fact]
    public async Task Resume_WhenTheProviderCannotResume_Returns501()
    {
        var organizationId = await SeedCancelledSubscriptionAsync();
        var service = CreateService(new ManualPaymentProvider(), new FixedClock(Now));

        var exception = await Assert.ThrowsAsync<PaymentProviderNotSupportedException>(
            () => service.ResumeAsync(organizationId));

        Assert.Equal(501, exception.StatusCode);
        Assert.Equal("payment-provider-capability-missing", exception.ErrorCode);

        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.True(stored.CancelAtPeriodEnd);
    }

    /// <summary>
    /// The provider that does support it (the mock advertises <c>SupportsCancelAtPeriodEnd</c>) is
    /// asked to restore the agreement and the local flag is cleared.
    /// </summary>
    [Fact]
    public async Task Resume_WhenTheProviderSupportsIt_ClearsTheScheduledCancellation()
    {
        var organizationId = await SeedCancelledSubscriptionAsync();
        var service = CreateService(clock: new FixedClock(Now));

        var view = await service.ResumeAsync(organizationId);

        Assert.False(view.CancelAtPeriodEnd);
        Assert.Null(view.CancelledAt);

        var stored = await _context.OrganizationSubscriptions
            .SingleAsync(subscription => subscription.OrganizationId == organizationId);
        Assert.False(stored.CancelAtPeriodEnd);
        Assert.Null(stored.CancelledAt);
    }

    private async Task<Guid> SeedCancelledSubscriptionAsync()
    {
        var organizationId = await SeedOrganizationAsync(PlanTier.Bloom);
        var periodStart = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc);

        _context.OrganizationSubscriptions.Add(new OrganizationSubscription
        {
            OrganizationId = organizationId,
            PlanTier = PlanTier.Bloom,
            Status = SubscriptionStatus.Active,
            PriceLkr = 3500m,
            CurrentPeriodStart = periodStart,
            CurrentPeriodEnd = periodStart.AddMonths(1),
            CancelAtPeriodEnd = true,
            CancelledAt = new DateTime(2026, 2, 5, 0, 0, 0, DateTimeKind.Utc),
        });
        await _context.SaveChangesAsync();
        return organizationId;
    }

    /// <summary>Records the cancellation the service asked the provider for.</summary>
    private sealed class RecordingSubscriptionProvider : IPaymentProvider
    {
        public string Key => "mock";

        public PaymentProviderCapabilities Capabilities { get; } = new(
            SupportsRecurringSubscriptions: true,
            SupportsProration: true,
            SupportsPartialRefunds: true,
            SupportsCancelAtPeriodEnd: true,
            SupportsHostedCheckout: true,
            SettlesAsynchronously: true);

        public string? CancelledProviderSubscriptionId { get; private set; }

        public bool? CancelledAtPeriodEnd { get; private set; }

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
            string providerSubscriptionId, bool atPeriodEnd, CancellationToken cancellationToken = default)
        {
            CancelledProviderSubscriptionId = providerSubscriptionId;
            CancelledAtPeriodEnd = atPeriodEnd;

            return Task.FromResult(new ProviderSubscription(
                providerSubscriptionId,
                "mock_customer",
                PaymentProviderStatus.Processing,
                Money.Lkr(3500m),
                "month",
                new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                CancelAtPeriodEnd: atPeriodEnd));
        }
    }
}
