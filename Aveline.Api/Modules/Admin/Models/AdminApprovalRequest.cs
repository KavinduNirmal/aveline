using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Aveline.Api.Modules.Admin.Models;

/// <summary>
/// An administrator access request. Created by an admin sign-up after email
/// verification; an existing administrator approves or rejects it. Approval
/// grants the <c>admin</c> team role in Clerk (public metadata), which the
/// authorization layer reads from the <c>jwt-aveline-v1</c> <c>user_role</c> claim.
/// </summary>
[Table("AdminApprovalRequests")]
public class AdminApprovalRequest
{
    [Key]
    public Guid Id { get; set; } = Guid.CreateVersion7();

    [Required]
    [MaxLength(255)]
    public string ClerkUserId { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    public AdminApprovalStatus Status { get; set; } = AdminApprovalStatus.Pending;

    public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

    public DateTime? ReviewedAt { get; set; }

    [MaxLength(255)]
    public string? ReviewedByClerkUserId { get; set; }
}
