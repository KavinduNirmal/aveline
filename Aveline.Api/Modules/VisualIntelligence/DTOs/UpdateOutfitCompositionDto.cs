namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

/// <summary>
/// The editable metadata of a composed lookbook. Every field is optional: an omitted field keeps
/// the value the row already stores, exactly like <see cref="UpdateInventoryItemDto"/>, so a rename
/// cannot silently clear the occasion or the stylist notes.
/// </summary>
/// <remarks>
/// The item composition is deliberately not editable here. Adding or removing a piece is the
/// stylist flow (<c>/lookbooks/compose</c>) and goes through Elle's complementary-item search;
/// letting a PUT rewrite the item set would bypass that and leave <c>TotalPrice</c> describing a
/// composition the row no longer holds.
/// </remarks>
public class UpdateOutfitCompositionDto
{
    public Guid OrganizationId { get; set; }

    public Guid OrgId
    {
        get => OrganizationId;
        set => OrganizationId = value;
    }

    public string? Name { get; set; }

    public string? Occasion { get; set; }

    public string? StyleNotes { get; set; }
}
