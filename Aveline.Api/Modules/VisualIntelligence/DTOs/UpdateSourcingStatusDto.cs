namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public record UpdateSourcingStatusDto(
    string Status,
    string? Notes = null
);
