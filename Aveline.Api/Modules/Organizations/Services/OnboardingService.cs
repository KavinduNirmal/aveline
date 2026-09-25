using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.Domain;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Organizations.Services;

public partial class OnboardingService : IOnboardingService
{
    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUsageRepository _usageRepository;
    private readonly IUserCacheService _userCacheService;
    private readonly IAgentServiceClient _agentServiceClient;
    private readonly IEntitlementResolver? _entitlementResolver;
    private readonly ISubscriptionProvisioner? _subscriptionProvisioner;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IUsageRepository usageRepository,
        IUserCacheService userCacheService,
        IAgentServiceClient agentServiceClient,
        ILogger<OnboardingService> logger,
        IEntitlementResolver? entitlementResolver = null,
        ISubscriptionProvisioner? subscriptionProvisioner = null)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _usageRepository = usageRepository;
        _userCacheService = userCacheService;
        _agentServiceClient = agentServiceClient;
        _entitlementResolver = entitlementResolver;
        _subscriptionProvisioner = subscriptionProvisioner;
        _logger = logger;
    }

    public async Task<OnboardingStatusResponse> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User '{userId}' was not found.");

        var org = await _organizationRepository.GetByOwnerUserIdAsync(userId, cancellationToken);
        if (org is null)
        {
            return new OnboardingStatusResponse(
                HasCompletedOnboarding: user.HasCompletedOnboarding,
                CurrentStep: 2,
                Organization: null);
        }

        return new OnboardingStatusResponse(
            HasCompletedOnboarding: org.HasCompletedOnboarding && user.HasCompletedOnboarding,
            CurrentStep: org.HasCompletedOnboarding ? 6 : org.OnboardingStep,
            Organization: await MapToDtoAsync(org, cancellationToken));
    }

    public async Task<OnboardingOrganizationDto> SaveBoutiqueDetailsAsync(
        Guid userId,
        SaveBoutiqueDetailsRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Name);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.Address);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.PhoneNumber);

        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User '{userId}' was not found.");

        // Normalize the requested slug (or derive it from the name). The result is
        // always lowercase + alphanumeric/hyphen and capped at the column length, so a
        // stored slug always matches the case-sensitive by-slug tenant lookup.
        var requestedSlug = string.IsNullOrWhiteSpace(request.Slug) ? request.Name : request.Slug;
        var slug = OrgSlug.From(requestedSlug);
        if (string.IsNullOrEmpty(slug))
        {
            slug = OrgSlug.From(request.Name);
        }

        var existingOrg = await _organizationRepository.GetByOwnerUserIdAsync(userId, cancellationToken);
        if (existingOrg is null)
        {
            if (await _organizationRepository.ExistsBySlugAsync(slug, cancellationToken))
            {
                slug = $"{slug}-{Guid.NewGuid().ToString("N")[..6]}";
            }

            var newOrg = new Organization
            {
                Name = request.Name.Trim(),
                Slug = slug,
                OwnerUserId = userId,
                Address = request.Address.Trim(),
                PhoneNumber = request.PhoneNumber.Trim(),
                Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim(),
                LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim(),
                OnboardingStep = 3,
                HasCompletedOnboarding = false,
                IsActive = true,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            };

            newOrg = await _organizationRepository.CreateAsync(newOrg, cancellationToken);

            var existingMembership = await _organizationRepository.GetMembershipAsync(newOrg.Id, userId, cancellationToken);
            if (existingMembership is null)
            {
                await _organizationRepository.AddMembershipAsync(new OrganizationMembership
                {
                    OrganizationId = newOrg.Id,
                    UserId = userId,
                    BoutiqueRole = Roles.BoutiqueOwner,
                    Status = MembershipStatus.Active,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                }, cancellationToken);
            }

            _logger.LogInformation("Draft boutique created for owner. orgId={OrgId} ownerId={OwnerId}", newOrg.Id, userId);
            return MapToDto(newOrg);
        }

        existingOrg.Name = request.Name.Trim();
        existingOrg.Address = request.Address.Trim();
        existingOrg.PhoneNumber = request.PhoneNumber.Trim();
        existingOrg.Description = string.IsNullOrWhiteSpace(request.Description) ? null : request.Description.Trim();
        existingOrg.LogoUrl = string.IsNullOrWhiteSpace(request.LogoUrl) ? null : request.LogoUrl.Trim();
        if (existingOrg.OnboardingStep < 3)
        {
            existingOrg.OnboardingStep = 3;
        }
        existingOrg.UpdatedAt = DateTime.UtcNow;

        await _organizationRepository.UpdateAsync(existingOrg, cancellationToken);
        _logger.LogInformation("Draft boutique details updated. orgId={OrgId}", existingOrg.Id);
        return await MapToDtoAsync(existingOrg, cancellationToken);
    }

    public async Task<OnboardingOrganizationDto> SelectPlanAsync(
        Guid userId,
        SelectPlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var org = await _organizationRepository.GetByOwnerUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Boutique details must be submitted before choosing a plan.");

        org.PlanTier = request.PlanTier;
        if (org.OnboardingStep < 4)
        {
            org.OnboardingStep = 4;
        }
        org.UpdatedAt = DateTime.UtcNow;

        await _organizationRepository.UpdateAsync(org, cancellationToken);

        // Plan §9.1 F1 (G8): the tier change and the subscription it implies are one call from the
        // client. The provisioner is optional so a deployment (or a unit test) that has not wired the
        // Billing price book still selects a plan; the response then reports no subscription.
        //
        // Defer mode (plan §14 Q1) is the accepted answer: this creates **no** payment intent, asks
        // for **no** settlement, and returns no checkout URL. Payment is collected later, out of band.
        var provisioned = _subscriptionProvisioner is null
            ? null
            : await _subscriptionProvisioner.ProvisionFromTierAsync(
                org.Id, request.PlanTier, at: null, cancellationToken);

        _logger.LogInformation(
            "Plan selected for onboarding boutique. orgId={OrgId} tier={Tier} priceLkr={PriceLkr} status={Status}",
            org.Id, org.PlanTier, provisioned?.PriceLkr, provisioned?.Status);

        return provisioned is not null
            ? MapToDto(org, provisioned.PriceLkr, provisioned.Currency, provisioned.Status)
            : MapToDto(org);
    }

    public async Task<OnboardingOrganizationDto> SaveAiCustomizationAsync(
        Guid userId,
        SaveAiCustomizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var org = await _organizationRepository.GetByOwnerUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Boutique details and plan selection must precede AI customization.");

        // Validate plan capabilities from the entitlement catalog, not a hardcoded tier
        // check (defect D-12).
        var customContext = _entitlementResolver is null
            ? "full"
            : (await _entitlementResolver.GetAsync(org.Id, "ai.customContext", at: null, cancellationToken))?.Text
              ?? "none";

        if (customContext == "none")
        {
            if (!string.IsNullOrWhiteSpace(request.BrandVoice) ||
                !string.IsNullOrWhiteSpace(request.BusinessRules) ||
                !string.IsNullOrWhiteSpace(request.PreferredColorsFabrics) ||
                !string.IsNullOrWhiteSpace(request.CustomerPreferences))
            {
                throw new ArgumentException("Custom AI context is not available on the Seed plan. Please upgrade to Bloom or Orchid to configure bespoke brand voice and rules.");
            }
        }
        else if (customContext == "basic")
        {
            // Bloom tier allows basic business rules and store voice tone, but not full memory tuning
            if (!string.IsNullOrWhiteSpace(request.CustomerPreferences))
            {
                throw new ArgumentException("Deep customer memory rules require Orchid or Rose plans.");
            }
        }

        org.BrandVoice = string.IsNullOrWhiteSpace(request.BrandVoice) ? null : request.BrandVoice.Trim();
        org.BusinessRules = string.IsNullOrWhiteSpace(request.BusinessRules) ? null : request.BusinessRules.Trim();
        org.PreferredColorsFabrics = string.IsNullOrWhiteSpace(request.PreferredColorsFabrics) ? null : request.PreferredColorsFabrics.Trim();
        org.CustomerPreferences = string.IsNullOrWhiteSpace(request.CustomerPreferences) ? null : request.CustomerPreferences.Trim();

        if (org.OnboardingStep < 5)
        {
            org.OnboardingStep = 5;
        }
        org.UpdatedAt = DateTime.UtcNow;

        await _organizationRepository.UpdateAsync(org, cancellationToken);
        _logger.LogInformation("AI context saved for onboarding boutique. orgId={OrgId}", org.Id);
        return await MapToDtoAsync(org, cancellationToken);
    }

    public async Task<CompleteOnboardingResponse> CompleteOnboardingAsync(
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var user = await _userRepository.GetByIdAsync(userId, cancellationToken)
            ?? throw new KeyNotFoundException($"User '{userId}' was not found.");

        var org = await _organizationRepository.GetByOwnerUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Cannot complete onboarding without registering boutique details.");

        // 1. Finalize Organization
        org.IsActive = true;
        org.HasCompletedOnboarding = true;
        org.OnboardingStep = 6;
        org.UpdatedAt = DateTime.UtcNow;
        await _organizationRepository.UpdateAsync(org, cancellationToken);

        // 2. Ensure owner membership exists and is active
        var membership = await _organizationRepository.GetMembershipAsync(org.Id, userId, cancellationToken);
        if (membership is null)
        {
            await _organizationRepository.AddMembershipAsync(new OrganizationMembership
            {
                OrganizationId = org.Id,
                UserId = userId,
                BoutiqueRole = Roles.BoutiqueOwner,
                Status = MembershipStatus.Active,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
            }, cancellationToken);
        }
        else if (membership.Status != MembershipStatus.Active)
        {
            membership.Status = MembershipStatus.Active;
            membership.BoutiqueRole = Roles.BoutiqueOwner;
            membership.UpdatedAt = DateTime.UtcNow;
            await _organizationRepository.UpdateMembershipAsync(membership, cancellationToken);
        }

        // 3. Update User record
        user.OrganizationId = org.Id.ToString();
        user.UserRole = Roles.Owner;
        // Defect D-9: the previous literal "org:principal" is absent from Roles.cs and
        // therefore granted nothing. Assign the canonical owner role instead.
        user.OrganizationRole = Roles.BoutiqueOwner;
        user.HasCompletedOnboarding = true;
        user.AccountState = AccountState.Active;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        // 4. Provision initial Blossom quota in UsageAccount for current period
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);
        var blossomLimit = _entitlementResolver is null
            ? 150m
            : await _entitlementResolver.GetDecimalAsync(
                org.Id, UsageTrackerService.MonthlyBlossomsKey, 150m, at: null, cancellationToken);

        var usageAccount = await _usageRepository.GetOrCreateAccountAsync(
            org.Id, periodStart, periodEnd, blossomLimit, cancellationToken);

        // Invalidate cache
        await _userCacheService.InvalidateAsync(user.ClerkId, cancellationToken);

        // 5. Initialize & warm up Customer Memory Agent
        var agentWarmedUp = false;
        try
        {
            var warmupPayload = new
            {
                organizationId = org.Id,
                boutiqueName = org.Name,
                planTier = org.PlanTier.ToString(),
                brandVoice = org.BrandVoice,
                businessRules = org.BusinessRules,
                preferredColorsFabrics = org.PreferredColorsFabrics,
                customerPreferences = org.CustomerPreferences,
            };

            using var content = JsonContent.Create(warmupPayload);
            var warmupResponse = await _agentServiceClient.PostAsync("/agents/warmup", content, cancellationToken);
            agentWarmedUp = warmupResponse.IsSuccessStatusCode;
            if (!agentWarmedUp)
            {
                _logger.LogWarning("Agent warmup returned non-success code {StatusCode} for org {OrgId}", warmupResponse.StatusCode, org.Id);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not contact agent service for warmup on org {OrgId}. Non-fatal.", org.Id);
        }

        _logger.LogInformation("Owner onboarding completed successfully. orgId={OrgId} userId={UserId} tier={Tier}", org.Id, userId, org.PlanTier);

        return new CompleteOnboardingResponse(
            Organization: await MapToDtoAsync(org, cancellationToken),
            UserRole: user.UserRole,
            OrganizationRole: user.OrganizationRole,
            AccountState: user.AccountState.ToString(),
            BlossomAllocation: usageAccount.MonthlyBlossomLimit,
            AgentWarmedUp: agentWarmedUp);
    }

    /// <summary>
    /// Maps the organization plus the subscription facts the response now reports (plan §9.1 F1).
    /// The payment members are always null in defer mode (§14 Q1): plan selection creates no intent
    /// and returns no checkout URL. A <c>null</c> price means "no effective price row", which is not
    /// the same as the zero price of the free Seed plan (P1).
    /// </summary>
    private static OnboardingOrganizationDto MapToDto(
        Organization org,
        decimal? priceLkr = null,
        string currency = "LKR",
        string? subscriptionStatus = null) => new(
        Id: org.Id,
        Name: org.Name,
        Slug: org.Slug,
        Address: org.Address,
        PhoneNumber: org.PhoneNumber,
        Description: org.Description,
        LogoUrl: org.LogoUrl,
        PlanTier: org.PlanTier,
        BrandVoice: org.BrandVoice,
        BusinessRules: org.BusinessRules,
        PreferredColorsFabrics: org.PreferredColorsFabrics,
        CustomerPreferences: org.CustomerPreferences,
        OnboardingStep: org.OnboardingStep,
        HasCompletedOnboarding: org.HasCompletedOnboarding,
        PriceLkr: priceLkr,
        Currency: currency,
        SubscriptionStatus: subscriptionStatus,
        PaymentIntentId: null,
        CheckoutUrl: null);

    /// <summary>
    /// The projection for the reads that have to report what the plan selection recorded, so a
    /// reload of the wizard keeps the server's price rather than falling back to client copy. The
    /// Billing module owns <see cref="OrganizationSubscription"/>; the repository exposes the one
    /// row for an organization so this stays a read the module can make without a DbContext.
    /// </summary>
    private async Task<OnboardingOrganizationDto> MapToDtoAsync(
        Organization org, CancellationToken cancellationToken)
    {
        var subscription = await _organizationRepository.GetSubscriptionAsync(org.Id, cancellationToken);
        return subscription is null
            ? MapToDto(org)
            : MapToDto(org, subscription.PriceLkr, "LKR", subscription.Status.ToString());
    }
}
