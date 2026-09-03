using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Shared.DTOs;

public class UserDto
{
    public Guid Id { get; set; }
    public string ClerkId { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? DisplayName { get; set; }
    public string Username { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string? Address { get; set; }
    public string? ProfileImageUrl { get; set; }
    public string UserRole { get; set; } = string.Empty;
    public string OrganizationRole { get; set; } = string.Empty;
    public string OrganizationId { get; set; } = string.Empty;
    public bool HasCompletedOnboarding { get; set; }
    public ContactPreferences ContactPreference { get; set; }
    public bool PushNotificationsEnabled { get; set; }
    public bool IsActive { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public static UserDto FromEntity(User user)
    {
        return new UserDto
        {
            Id = user.Id,
            ClerkId = user.ClerkId,
            Email = user.Email,
            FirstName = user.FirstName,
            LastName = user.LastName,
            DisplayName = user.DisplayName,
            Username = user.Username,
            PhoneNumber = user.PhoneNumber,
            Address = user.Address,
            ProfileImageUrl = user.ProfileImageUrl,
            UserRole = user.UserRole,
            OrganizationRole = user.OrganizationRole,
            OrganizationId = user.OrganizationId,
            HasCompletedOnboarding = user.HasCompletedOnboarding,
            ContactPreference = user.ContactPreference,
            PushNotificationsEnabled = user.PushNotificationsEnabled,
            IsActive = user.IsActive,
            CreatedAt = user.CreatedAt,
            UpdatedAt = user.UpdatedAt
        };
    }
}
