namespace Aveline.Api.Modules.Integrations.Models;

/// <summary>
/// Minimal audit record of a message exchanged through an integration channel (e.g. an
/// inbound WhatsApp message). Tenant-scoped and never carries secrets. This is a lightweight
/// log for audit + the inbound pipeline; richer customer/memory modelling lives in the
/// Customer Concierge slice.
/// </summary>
public class InboundMessageLog
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>Owning boutique. All lookups filter by this column (tenant isolation).</summary>
    public Guid OrganizationId { get; set; }

    /// <summary>Channel the message arrived on, e.g. <c>whatsapp</c>.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Direction of the message: <c>inbound</c> or <c>outbound</c>.</summary>
    public string Direction { get; set; } = "inbound";

    /// <summary>Provider message id (e.g. Meta <c>wamid</c>), when available.</summary>
    public string? ExternalId { get; set; }

    /// <summary>Sender identifier (e.g. the customer's WhatsApp number).</summary>
    public string? From { get; set; }

    /// <summary>Recipient identifier (e.g. the boutique's WhatsApp number).</summary>
    public string? To { get; set; }

    /// <summary>Message text content.</summary>
    public string? Content { get; set; }

    public DateTime ReceivedAt { get; set; } = DateTime.UtcNow;
}
