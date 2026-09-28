using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class CatalogTagDto
{
    public Guid Id { get; set; }
    public Guid OrgId { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string? ColorHex { get; set; }
    public int SortOrder { get; set; }
    public bool IsArchived { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public int ItemCount { get; set; }

    public static CatalogTagDto FromDomain(CatalogTag tag, int? itemCount = null)
    {
        return new CatalogTagDto
        {
            Id = tag.Id,
            OrgId = tag.OrgId,
            Slug = tag.Slug,
            Label = tag.Label,
            ColorHex = tag.ColorHex,
            SortOrder = tag.SortOrder,
            IsArchived = tag.IsArchived,
            CreatedAtUtc = tag.CreatedAtUtc,
            UpdatedAtUtc = tag.UpdatedAtUtc,
            ItemCount = itemCount ?? tag.ItemTags?.Count(it => it.Item == null || it.Item.DeletedAt == null) ?? 0
        };
    }
}
