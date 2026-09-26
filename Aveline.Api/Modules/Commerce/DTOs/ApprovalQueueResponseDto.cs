namespace Aveline.Api.Modules.Commerce.DTOs;

public class ApprovalQueueResponseDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid OrderId { get; set; }
    public string ApprovalType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public bool ThresholdExceeded { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? DecisionComment { get; set; }
    public Guid? DecidedBy { get; set; }
    public string? ThreadId { get; set; }
    public Guid? ConversationId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? DecidedAt { get; set; }
    public OrderResponseDto? Order { get; set; }
}
