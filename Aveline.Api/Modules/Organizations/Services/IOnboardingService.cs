using Aveline.Api.Modules.Organizations.DTOs;

namespace Aveline.Api.Modules.Organizations.Services;

public interface IOnboardingService
{
    Task<OnboardingStatusResponse> GetStatusAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task<OnboardingOrganizationDto> SaveBoutiqueDetailsAsync(
        Guid userId,
        SaveBoutiqueDetailsRequest request,
        CancellationToken cancellationToken = default);

    Task<OnboardingOrganizationDto> SelectPlanAsync(
        Guid userId,
        SelectPlanRequest request,
        CancellationToken cancellationToken = default);

    Task<OnboardingOrganizationDto> SaveAiCustomizationAsync(
        Guid userId,
        SaveAiCustomizationRequest request,
        CancellationToken cancellationToken = default);

    Task<CompleteOnboardingResponse> CompleteOnboardingAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}
