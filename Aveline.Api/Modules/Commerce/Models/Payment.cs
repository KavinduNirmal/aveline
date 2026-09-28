using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Aveline.Api.Common.MultiTenancy;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Payments.Models;

namespace Aveline.Api.Modules.Commerce.Models;

[Table("Payments")]
public class Payment : ITenantEntity
{
    [Key]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Required]
    public Guid OrganizationId { get; set; }

    [Required]
    public Guid OrderId { get; set; }

    [Required]
    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [MaxLength(50)]
    public string PaymentType { get; set; } = "deposit"; // deposit, balance, full

    [Required]
    [MaxLength(50)]
    public string PaymentMethod { get; set; } = "online"; // card, cash, online, bank_transfer

    [Required]
    [MaxLength(50)]
    public string Status { get; set; } = "pending"; // pending, confirmed, failed, expired, refunded

    /// <summary>
    /// The provider-neutral intent this order charge was created through, when it was created
    /// through one (plan §9.7, Phase 9). Null for a counter payment an operator recorded directly,
    /// which is the <c>manual</c> adapter's shape rather than a missing link.
    /// </summary>
    /// <remarks>
    /// <b>This is the pointer that makes the intents table the settlement authority.</b> The
    /// confirmation route reads the intent's provider state instead of trusting the caller, so
    /// without this column it would have no provider resource to ask and would have to believe
    /// whatever transaction id arrived (the defect at
    /// <c>docs/reports/PR-290-slice3-review.md:269</c>).
    /// </remarks>
    public Guid? PaymentIntentId { get; set; }

    /// <summary>
    /// The provider's transaction reference, written only from a provider answer (Phase 9). It was
    /// previously free text taken from the request body, which is why it carried no evidentiary
    /// weight; the unique index on it is what turns a duplicate provider reference into a rejected
    /// write rather than a second row.
    /// </summary>
    [MaxLength(100)]
    public string? GatewayTransactionId { get; set; }

    /// <summary>
    /// The provider's hosted checkout URL, or null when the provider settles in place (plan §9.7).
    /// Never a value Aveline constructs: it is what the adapter returned.
    /// </summary>
    [MaxLength(500)]
    public string? PaymentLink { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ConfirmedAt { get; set; }
    public DateTime? ExpiresAt { get; set; }

    // Navigation properties
    public Organization? Organization { get; set; }
    [ForeignKey(nameof(OrderId))]
    public Order Order { get; set; } = null!;

    /// <summary>
    /// The intent this charge was created through, when there is one. Deliberately **not** a
    /// required relationship: a counter payment has no intent, and a tenant's old rows keep a null
    /// link rather than being deleted or rewritten by the migration (plan §8.4 S8).
    /// </summary>
    [ForeignKey(nameof(PaymentIntentId))]
    public PaymentIntent? PaymentIntent { get; set; }
}