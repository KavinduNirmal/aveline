using System.ComponentModel.DataAnnotations;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Shared.DTOs;

public class CompleteOnboardingRequest
{
    [Required]
    [MaxLength(200)]
    public string DisplayName { get; set; } = string.Empty;

    [Required]
    [MaxLength(20)]
    [Phone]
    public string PhoneNumber { get; set; } = string.Empty;

    // Address is optional: staff do not provide one during onboarding. Owners
    // capture their boutique address at the organization level instead.
    [MaxLength(500)]
    public string? Address { get; set; }

    public string? ProfileImageUrl { get; set; }

    public ContactPreferences ContactPreference { get; set; } = ContactPreferences.None;

    public bool PushNotificationsEnabled { get; set; } = false;
}
