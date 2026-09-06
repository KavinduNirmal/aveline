using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Caching;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Shared.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Organizations.Services;

public partial class OnboardingService : IOnboardingService
{
    private static readonly Dictionary<PlanTier, decimal> PlanBlossomLimits = new()
    {
        { PlanTier.Seed,       150m },
        { PlanTier.Bloom,      750m },
        { PlanTier.Orchid,    2000m },
        { PlanTier.Rose,      5000m },
        { PlanTier.Enterprise, 9999m },
    };

    private readonly IOrganizationRepository _organizationRepository;
    private readonly IUserRepository _userRepository;
    private readonly IUsageRepository _usageRepository;
    private readonly IUserCacheService _userCacheService;
    private readonly IAgentServiceClient _agentServiceClient;
    private readonly ILogger<OnboardingService> _logger;

    public OnboardingService(
        IOrganizationRepository organizationRepository,
        IUserRepository userRepository,
        IUsageRepository usageRepository,
        IUserCacheService userCacheService,
        IAgentServiceClient agentServiceClient,
        ILogger<OnboardingService> logger)
    {
        _organizationRepository = organizationRepository;
        _userRepository = userRepository;
        _usageRepository = usageRepository;
        _userCacheService = userCacheService;
        _agentServiceClient = agentServiceClient;
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
            Organization: MapToDto(org));
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

        var slug = string.IsNullOrWhiteSpace(request.Slug) ? ToSlug(request.Name) : request.Slug.Trim();

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
        return MapToDto(existingOrg);
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
        _logger.LogInformation("Plan selected for onboarding boutique. orgId={OrgId} tier={Tier}", org.Id, org.PlanTier);
        return MapToDto(org);
    }

    public async Task<OnboardingOrganizationDto> SaveAiCustomizationAsync(
        Guid userId,
        SaveAiCustomizationRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var org = await _organizationRepository.GetByOwnerUserIdAsync(userId, cancellationToken)
            ?? throw new InvalidOperationException("Boutique details and plan selection must precede AI customization.");

        // Validate plan tier capabilities per pricing_plan.md
        if (org.PlanTier == PlanTier.Seed)
        {
            // Seed tier does not permit custom AI context
            if (!string.IsNullOrWhiteSpace(request.BrandVoice) ||
                !string.IsNullOrWhiteSpace(request.BusinessRules) ||
                !string.IsNullOrWhiteSpace(request.PreferredColorsFabrics) ||
                !string.IsNullOrWhiteSpace(request.CustomerPreferences))
            {
                throw new ArgumentException("Custom AI context is not available on the Seed plan. Please upgrade to Bloom or Orchid to configure bespoke brand voice and rules.");
            }
        }
        else if (org.PlanTier == PlanTier.Bloom)
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
        return MapToDto(org);
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
        user.OrganizationRole = "org:principal";
        user.HasCompletedOnboarding = true;
        user.AccountState = AccountState.Active;
        user.UpdatedAt = DateTime.UtcNow;
        await _userRepository.UpdateAsync(user, cancellationToken);

        // 4. Provision initial Blossom quota in UsageAccount for current period
        var now = DateTime.UtcNow;
        var periodStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var periodEnd = periodStart.AddMonths(1);
        var blossomLimit = PlanBlossomLimits.GetValueOrDefault(org.PlanTier, 150m);

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
            Organization: MapToDto(org),
            UserRole: user.UserRole,
            OrganizationRole: user.OrganizationRole,
            AccountState: user.AccountState.ToString(),
            BlossomAllocation: usageAccount.MonthlyBlossomLimit,
            AgentWarmedUp: agentWarmedUp);
    }

    private static OnboardingOrganizationDto MapToDto(Organization org) => new(
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
        HasCompletedOnboarding: org.HasCompletedOnboarding);

    [GeneratedRegex("[^a-z0-9]+")]
    private static partial Regex NonAlphanumericRegex();

    private static string ToSlug(string name)
    {
        var slug = NonAlphanumericRegex().Replace(name.ToLowerInvariant(), "-");
        return slug.Trim('-');
    }
}
