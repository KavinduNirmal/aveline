using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Infrastructure.Caching;

public class UserOnboardingCacheItem
{
    public Guid Id { get; set; }
    public string ClerkId { get; set; } = string.Empty;
    public bool HasCompletedOnboarding { get; set; }
    public AccountState AccountState { get; set; } = AccountState.OnboardingPending;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Address { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public string OrganizationRole { get; set; } = string.Empty;
}
