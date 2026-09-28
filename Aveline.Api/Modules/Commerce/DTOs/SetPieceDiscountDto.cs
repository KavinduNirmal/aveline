namespace Aveline.Api.Modules.Commerce.DTOs;

public record SetPieceDiscountDto(
    Guid ItemId,
    string ItemName,
    decimal DiscountPercentage,
    string? Description = null);
