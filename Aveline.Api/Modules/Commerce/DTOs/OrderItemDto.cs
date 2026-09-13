using System.ComponentModel.DataAnnotations;

namespace Aveline.Api.Modules.Commerce.DTOs;

public class OrderItemDto
{
    public Guid? Id { get; set; }

    [Required]
    public Guid ItemId { get; set; }

    [Required]
    [MaxLength(200)]
    public string ItemName { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Quantity must be at least 1.")]
    public int Quantity { get; set; } = 1;

    [Range(0, double.MaxValue, ErrorMessage = "UnitPrice cannot be negative.")]
    public decimal UnitPrice { get; set; }

    [Range(0, double.MaxValue, ErrorMessage = "WholesaleCost cannot be negative.")]
    public decimal WholesaleCost { get; set; }

    public decimal TotalPrice { get; set; }
}
