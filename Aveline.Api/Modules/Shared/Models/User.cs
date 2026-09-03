using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.Shared.Models;

[Table("Users")]
public class User
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();
    [Required]
    public string ClerkId { get; set; } = string.Empty;
    public string? ProfileImageUrl { get; set; }

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;
    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;
    [Required]
    public string OrganizationId { get; set; } = string.Empty;
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
    [Required]
    [MaxLength(100)]
    public string Username { get; set; } = string.Empty;
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;
    [Required]
    [MaxLength(20)]
    public string UserRole { get; set; } = string.Empty;
    [Required]
    [MaxLength(20)]
    public string OrganizationRole { get; set; } = string.Empty;
    [MaxLength(200)]
    public string? DisplayName { get; set; }
    public string? Address { get; set; }
    public bool HasCompletedOnboarding { get; set; } = false;
    public ContactPreferences ContactPreference { get; set; } = ContactPreferences.None;
    public bool PushNotificationsEnabled { get; set; } = false;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? DeletedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum ContactPreferences
{
    Email,
    Phone,
    SMS,
    WhatsApp,
    None
}

