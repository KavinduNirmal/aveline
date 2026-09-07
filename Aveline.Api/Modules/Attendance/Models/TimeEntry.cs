using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Attendance.Models;

/// <summary>
/// A staff clock-in / clock-out session for a boutique. One user may have many
/// time entries across organizations; an open entry (no <see cref="ClockOutAt"/>)
/// represents an active shift. The clock-in/out endpoints and mobile UI are a
/// deferred feature — this schema is landed ahead of time so they have a stable
/// model to build against.
/// </summary>
public class TimeEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    /// <summary>The staff member who clocked in.</summary>
    public Guid UserId { get; set; }

    /// <summary>The boutique the shift belongs to.</summary>
    public Guid OrganizationId { get; set; }

    public DateTime ClockInAt { get; set; } = DateTime.UtcNow;

    /// <summary>When the shift ended; <c>null</c> while the shift is open.</summary>
    public DateTime? ClockOutAt { get; set; }

    /// <summary>How the entry was recorded (e.g. <c>app</c>, <c>manual</c>).</summary>
    public string Source { get; set; } = "app";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public User? User { get; set; }

    public Organization? Organization { get; set; }
}
