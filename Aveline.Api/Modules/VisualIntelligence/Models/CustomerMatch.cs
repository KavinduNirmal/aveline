namespace Aveline.Api.Modules.VisualIntelligence.Models;

public class CustomerMatch
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OrgId { get; set; }
    public Guid CustomerId { get; set; }
    public Guid ItemId { get; set; }
    public double MatchScore { get; set; }
    public string Reason { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
