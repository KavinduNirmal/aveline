using System.Net;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Organizations.Services;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

public class OnboardingServiceTests
{
    private readonly AppDbContext _context;
    private readonly OnboardingService _sut;
    private readonly IUserCacheService _cacheService;
    private readonly FakeAgentServiceClient _agentClient;

    public OnboardingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OnboardingServiceTests_{Guid.NewGuid()}")
            .Options;

        _context = new AppDbContext(options);
        var memCacheOptions = Options.Create(new MemoryDistributedCacheOptions());
        var distCache = new MemoryDistributedCache(memCacheOptions);
        _cacheService = new UserCacheService(distCache, NullLogger<UserCacheService>.Instance);
        _agentClient = new FakeAgentServiceClient();

        SeedPlanEntitlements();

        _sut = new OnboardingService(
            new OrganizationRepository(_context),
            new UserRepository(_context),
            new UsageRepository(_context),
            _cacheService,
            _agentClient,
            NullLogger<OnboardingService>.Instance,
            new EntitlementResolver(new EntitlementRepository(_context)),
            new SubscriptionProvisioner(_context, new SubscriptionPriceResolver(new PricingRepository(_context))));
    }

    /// <summary>
    /// Seeds an active <c>PlanAllowance</c> row into the price book, exactly as the admin console
    /// would (G9 / FR-1.11). The book is empty in the test host, so an unseeded tier has no price.
    /// </summary>
    private void SeedPlanAllowance(PlanTier tier, decimal priceLkr, Guid? organizationId = null) =>
        _context.BlossomPriceEntries.Add(new BlossomPriceEntry
        {
            PlanTier = tier,
            OrganizationId = organizationId,
            SkuKind = BlossomSkuKind.PlanAllowance,
            BlossomQuantity = 750m,
            PriceLkr = priceLkr,
            EffectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Status = BlossomRuleStatus.Active,
            ChangeReason = "Seeded by the onboarding plan-selection tests.",
            CreatedByUserId = Guid.CreateVersion7(),
        });

    /// <summary>
    /// Seeds the entitlement rows the onboarding flow reads, mirroring the M4 seed
    /// (blossoms.monthly and ai.customContext per tier).
    /// </summary>
    private void SeedPlanEntitlements()
    {
        var effectiveFrom = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var blossoms = new Dictionary<PlanTier, decimal>
        {
            [PlanTier.Seed] = 150m,
            [PlanTier.Bloom] = 750m,
            [PlanTier.Orchid] = 2000m,
            [PlanTier.Rose] = 5000m,
            [PlanTier.Enterprise] = 9999m,
        };
        var customContext = new Dictionary<PlanTier, string>
        {
            [PlanTier.Seed] = "none",
            [PlanTier.Bloom] = "basic",
            [PlanTier.Orchid] = "full",
            [PlanTier.Rose] = "full",
            [PlanTier.Enterprise] = "full",
        };

        foreach (var (tier, limit) in blossoms)
        {
            _context.PlanEntitlements.Add(new PlanEntitlement
            {
                PlanTier = tier,
                Key = "blossoms.monthly",
                ValueType = EntitlementValueType.Decimal,
                ValueDecimal = limit,
                IsEnabled = true,
                EffectiveFrom = effectiveFrom,
                CreatedAt = effectiveFrom,
            });
            _context.PlanEntitlements.Add(new PlanEntitlement
            {
                PlanTier = tier,
                Key = "ai.customContext",
                ValueType = EntitlementValueType.String,
                ValueText = customContext[tier],
                IsEnabled = true,
                EffectiveFrom = effectiveFrom,
                CreatedAt = effectiveFrom,
            });
        }

        _context.SaveChanges();
    }

    private async Task<User> CreateUserAsync(string clerkId = "user_clerk_1")
    {
        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@example.com",
            FirstName = "Aveline",
            LastName = "Owner",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = false,
            AccountState = AccountState.OnboardingPending,
        };
        _context.Users.Add(user);
        await _context.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task GetStatusAsync_WhenNoOrg_ReturnsStep2()
    {
        var user = await CreateUserAsync();

        var status = await _sut.GetStatusAsync(user.Id);

        Assert.False(status.HasCompletedOnboarding);
        Assert.Equal(2, status.CurrentStep);
        Assert.Null(status.Organization);
    }

    [Fact]
    public async Task SaveBoutiqueDetailsAsync_CreatesDraftOrgWithStep3()
    {
        var user = await CreateUserAsync();

        var request = new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567",
            Description: "Fine silks and atelier bridal",
            LogoUrl: "https://example.com/logo.png");

        var result = await _sut.SaveBoutiqueDetailsAsync(user.Id, request);

        Assert.Equal("The Silk Pavilion", result.Name);
        Assert.Equal("the-silk-pavilion", result.Slug);
        Assert.Equal(3, result.OnboardingStep);
        Assert.False(result.HasCompletedOnboarding);
    }

    [Fact]
    public async Task SelectPlanAsync_UpdatesPlanTierAndStep4()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));

        var result = await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));

        Assert.Equal(PlanTier.Bloom, result.PlanTier);
        Assert.Equal(4, result.OnboardingStep);
    }

    /// <summary>
    /// Plan §9.1 F1 acceptance (2). Selecting a paid tier prices and subscribes in the same call:
    /// the resolved list price lands on the row, the status is <c>Trialing</c> (defer mode collects
    /// nothing yet), and exactly one subscription exists.
    /// </summary>
    [Fact]
    public async Task SelectPlanAsync_ForABloomPlan_CreatesExactlyOneTrialingSubscriptionAtTheResolvedPrice()
    {
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));

        var result = await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));

        Assert.Equal(3500m, result.PriceLkr);
        Assert.Equal("LKR", result.Currency);
        Assert.Equal("Trialing", result.SubscriptionStatus);
        // Defer mode (plan §14 Q1): no checkout and no intent is ever raised at plan selection.
        Assert.Null(result.PaymentIntentId);
        Assert.Null(result.CheckoutUrl);

        var org = await _context.Organizations.SingleAsync(o => o.OwnerUserId == user.Id);
        var subscription = await _context.OrganizationSubscriptions
            .SingleAsync(s => s.OrganizationId == org.Id);
        Assert.Equal(PlanTier.Bloom, subscription.PlanTier);
        Assert.Equal(3500m, subscription.PriceLkr);
        Assert.Equal(SubscriptionStatus.Trialing, subscription.Status);
        Assert.Null(subscription.ExternalProvider);
        Assert.Null(subscription.ExternalSubscriptionId);
    }

    [Fact]
    public async Task SelectPlanAsync_ForTheFreeSeedTier_CreatesNoPaidSubscription()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));

        var result = await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Seed));

        Assert.Equal(0m, result.PriceLkr);
        Assert.Equal("LKR", result.Currency);

        var org = await _context.Organizations.SingleAsync(o => o.OwnerUserId == user.Id);
        var subscriptions = await _context.OrganizationSubscriptions
            .Where(s => s.OrganizationId == org.Id)
            .ToListAsync();

        // Seed is free by definition: either no row at all, or one explicit zero-price row. Never
        // a priced one, and never a second row.
        Assert.True(subscriptions.Count <= 1);
        Assert.All(subscriptions, s =>
        {
            Assert.Equal(0m, s.PriceLkr);
            Assert.Equal(PlanTier.Seed, s.PlanTier);
        });
    }

    /// <summary>
    /// Idempotency: the wizard can be re-entered and the same card pressed twice. Re-selecting the
    /// same tier must not create a second subscription row.
    /// </summary>
    [Fact]
    public async Task SelectPlanAsync_SelectingTheSameTierTwice_IsIdempotent()
    {
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));

        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));
        var second = await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));

        Assert.Equal(3500m, second.PriceLkr);
        Assert.Equal("Trialing", second.SubscriptionStatus);

        var org = await _context.Organizations.SingleAsync(o => o.OwnerUserId == user.Id);
        Assert.Equal(1, await _context.OrganizationSubscriptions
            .CountAsync(s => s.OrganizationId == org.Id));
    }

    /// <summary>
    /// P1's rule: a missing price row is <c>null</c>, never coerced to zero, so the response says
    /// <c>priceLkr = null</c> and no <c>Derived</c> charge can be written for it later.
    /// </summary>
    [Fact]
    public async Task SelectPlanAsync_WithNoPriceRow_ReportsANullPriceAndStillTrials()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));

        var result = await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));

        Assert.Null(result.PriceLkr);
        Assert.Equal("Trialing", result.SubscriptionStatus);

        var org = await _context.Organizations.SingleAsync(o => o.OwnerUserId == user.Id);
        var subscription = await _context.OrganizationSubscriptions
            .SingleAsync(s => s.OrganizationId == org.Id);
        Assert.Equal(0m, subscription.PriceLkr);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_WithAPaidTier_StillActivatesInDeferMode()
    {
        SeedPlanAllowance(PlanTier.Bloom, priceLkr: 3500m);
        await _context.SaveChangesAsync();

        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));

        var response = await _sut.CompleteOnboardingAsync(user.Id);

        // Defer mode (plan §14 Q1): activation never blocks on a settlement.
        Assert.True(response.Organization.HasCompletedOnboarding);
        Assert.Equal(6, response.Organization.OnboardingStep);
        Assert.Equal(3500m, response.Organization.PriceLkr);
        Assert.Null(response.Organization.CheckoutUrl);
        Assert.Null(response.Organization.PaymentIntentId);
        Assert.Empty(await _context.PaymentIntents.ToListAsync());
    }

    [Fact]
    public async Task SaveAiCustomizationAsync_WhenSeedTierAttemptsCustomContext_ThrowsArgumentException()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Seed));

        var req = new SaveAiCustomizationRequest(
            BrandVoice: "Poised and discreet",
            BusinessRules: "Max discount 10%");

        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _sut.SaveAiCustomizationAsync(user.Id, req));

        Assert.Contains("not available on the Seed plan", ex.Message);
    }

    [Fact]
    public async Task SaveAiCustomizationAsync_WhenOrchidTier_SavesFullContextAndStep5()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "The Silk Pavilion",
            Address: "123 Galle Road, Colombo",
            PhoneNumber: "+94 77 123 4567"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Orchid));

        var req = new SaveAiCustomizationRequest(
            BrandVoice: "Poised and discreet",
            BusinessRules: "Max discount 15%, 14-day exchange",
            PreferredColorsFabrics: "Raw silk, Pashmina, Hand-woven linen",
            CustomerPreferences: "Greet with Ceylon tea, note sizing preferences");

        var result = await _sut.SaveAiCustomizationAsync(user.Id, req);

        Assert.Equal("Poised and discreet", result.BrandVoice);
        Assert.Equal("Raw silk, Pashmina, Hand-woven linen", result.PreferredColorsFabrics);
        Assert.Equal(5, result.OnboardingStep);
    }

    [Fact]
    public async Task CompleteOnboardingAsync_ActivatesOrgUserAndProvisionsBlossoms()
    {
        var user = await CreateUserAsync();
        await _sut.SaveBoutiqueDetailsAsync(user.Id, new SaveBoutiqueDetailsRequest(
            Name: "Atelier Luxe",
            Address: "Colombo 07",
            PhoneNumber: "+94 11 234 5678"));
        await _sut.SelectPlanAsync(user.Id, new SelectPlanRequest(PlanTier.Bloom));
        await _sut.SaveAiCustomizationAsync(user.Id, new SaveAiCustomizationRequest(
            BrandVoice: "Warm and inviting"));

        var response = await _sut.CompleteOnboardingAsync(user.Id);

        Assert.True(response.Organization.HasCompletedOnboarding);
        Assert.Equal(6, response.Organization.OnboardingStep);
        Assert.Equal("owner", response.UserRole);
        Assert.Equal(Roles.BoutiqueOwner, response.OrganizationRole);
        Assert.Equal(AccountState.Active.ToString(), response.AccountState);
        Assert.Equal(750m, response.BlossomAllocation);
        Assert.True(response.AgentWarmedUp);

        // Verify database state
        var updatedUser = await _context.Users.FindAsync(user.Id);
        Assert.NotNull(updatedUser);
        Assert.True(updatedUser.HasCompletedOnboarding);
        Assert.Equal(AccountState.Active, updatedUser.AccountState);
        Assert.Equal("owner", updatedUser.UserRole);
        // Defect D-9: a literal that is absent from the role catalog grants nothing.
        Assert.Equal(Roles.BoutiqueOwner, updatedUser.OrganizationRole);
    }

    private class FakeAgentServiceClient : IAgentServiceClient
    {
        public Task<HttpResponseMessage> PostAsync(string path, HttpContent content, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }

        public Task<HttpResponseMessage> GetAsync(string path, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
        }
    }
}
