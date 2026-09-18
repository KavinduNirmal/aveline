namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Dispatches reminders for upcoming customer events and records that a reminder was sent
/// (Issue #170). A background worker drives <see cref="ProcessDueEventsAsync"/> on a schedule.
/// </summary>
public interface IEventReminderService
{
    /// <summary>
    /// Finds active, not-yet-reminded events with an upcoming date inside the reminder horizon,
    /// dispatches a reminder to the boutique's staff, and marks each event as reminded.
    /// Returns the number of reminders dispatched.
    /// </summary>
    Task<int> ProcessDueEventsAsync(CancellationToken cancellationToken = default);
}
