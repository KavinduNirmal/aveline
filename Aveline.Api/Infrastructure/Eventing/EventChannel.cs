namespace Aveline.Api.Infrastructure.Eventing;

/// <summary>
/// Canonical channel naming for the Redis event bus (ADR-014).
///
/// Channels are org-scoped: <c>aveline:&lt;org_id&gt;:&lt;event_type&gt;</c>. Because a single
/// deployment (e.g. the agent service) may serve every organization, subscribers use the
/// pattern <c>aveline:*:&lt;event_type&gt;</c> (Redis <c>PSUBSCRIBE</c>) to receive events for
/// all orgs while the channel name still carries the org for routing and audit.
/// </summary>
public static class EventChannel
{
    /// <summary>Top-level namespace prefix for all Aveline event channels.</summary>
    public const string Prefix = "aveline";

    /// <summary>Wildcard segment used in subscribe patterns to match any organization.</summary>
    public const string AllOrganizations = "*";

    /// <summary>Builds the publish channel for a single organization.</summary>
    public static string ForOrganization(Guid organizationId, string eventType)
        => $"{Prefix}:{organizationId}:{eventType}";

    /// <summary>Builds the subscribe pattern matching the event type for every organization.</summary>
    public static string PatternForEventType(string eventType)
        => $"{Prefix}:{AllOrganizations}:{eventType}";
}
