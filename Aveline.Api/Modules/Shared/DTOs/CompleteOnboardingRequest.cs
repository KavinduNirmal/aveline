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

    [Required]
    public string Address { get; set; } = string.Empty;

    public string? ProfileImageUrl { get; set; }

    public ContactPreferences ContactPreference { get; set; } = ContactPreferences.None;

    public bool PushNotificationsEnabled { get; set; } = false;
}
