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

/// <summary>
/// The onboarding organization, widened by plan §9.1 F1 (G8) with the subscription the plan
/// selection created. The five new members are optional so existing positional constructions keep
/// compiling; the endpoint always serialises them.
/// </summary>
/// <param name="PriceLkr">
/// The list price the price book resolved for the tier, or <c>null</c> when no row was effective.
/// A missing price is deliberately not reported as zero (P1).
/// </param>
/// <param name="SubscriptionStatus">The subscription lifecycle state, or <c>null</c> before one exists.</param>
/// <param name="PaymentIntentId">
/// Always <c>null</c> in defer mode (plan §14 Q1). It exists so a require-settlement deployment can
/// report the first intent without changing the contract again.
/// </param>
/// <param name="CheckoutUrl">Always <c>null</c> in defer mode (plan §14 Q1).</param>
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
    bool HasCompletedOnboarding,
    decimal? PriceLkr = null,
    string Currency = "LKR",
    string? SubscriptionStatus = null,
    Guid? PaymentIntentId = null,
    string? CheckoutUrl = null
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
