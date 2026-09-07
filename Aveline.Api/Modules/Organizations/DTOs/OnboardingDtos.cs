using System.ComponentModel.DataAnnotations;
using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Organizations.DTOs;

public record SaveBoutiqueDetailsRequest(
    [Required]
    [MaxLength(200)]
    string Name,

    [Required]
    [MaxLength(500)]
    string Address,

    [Required]
    [MaxLength(50)]
    string PhoneNumber,

    [MaxLength(1000)]
    string? Description = null,

    [MaxLength(1000)]
    string? LogoUrl = null,

    [MaxLength(100)]
    string? Slug = null
);

public record SelectPlanRequest(
    [Required]
    PlanTier PlanTier
);

public record SaveAiCustomizationRequest(
    [MaxLength(500)]
    string? BrandVoice = null,

    [MaxLength(1000)]
    string? BusinessRules = null,

    [MaxLength(1000)]
    string? PreferredColorsFabrics = null,

    [MaxLength(1000)]
    string? CustomerPreferences = null
);

public record OnboardingOrganizationDto(
    Guid Id,
    string Name,
    string Slug,
    string? Address,
    string? PhoneNumber,
    string? Description,
    string? LogoUrl,
    PlanTier PlanTier,
    string? BrandVoice,
    string? BusinessRules,
    string? PreferredColorsFabrics,
    string? CustomerPreferences,
    int OnboardingStep,
    bool HasCompletedOnboarding
);

public record OnboardingStatusResponse(
    bool HasCompletedOnboarding,
    int CurrentStep,
    OnboardingOrganizationDto? Organization
);

public record CompleteOnboardingResponse(
    OnboardingOrganizationDto Organization,
    string UserRole,
    string OrganizationRole,
    string AccountState,
    decimal BlossomAllocation,
    bool AgentWarmedUp
);
