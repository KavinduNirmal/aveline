namespace Aveline.Api.Modules.VisualIntelligence.DTOs;

public class UploadImagePayloadDto
{
    public string ImageData { get; set; } = string.Empty;
    public string? FileName { get; set; }
    public string? ContentType { get; set; }
}
