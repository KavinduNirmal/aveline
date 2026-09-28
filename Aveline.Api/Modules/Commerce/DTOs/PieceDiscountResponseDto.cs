namespace Aveline.Api.Modules.Commerce.DTOs;

public record PieceDiscountResponseDto(
    Guid RuleId,
    Guid ItemId,
    string ItemName,
    decimal DiscountPercentage,
    bool IsActive,
    string? Description,
    DateTime CreatedAt,
    DateTime? UpdatedAt);
