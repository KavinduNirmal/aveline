using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Services;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Domain service behind the event-reminder job (Issue #170). It finds due events and dispatches a
/// staff-facing reminder through the shared <see cref="INotificationDispatcher"/> (SignalR/email/
/// push per the org), then marks each event as reminded so it is only sent once.
/// </summary>
public sealed class EventReminderService(
    ICustomerEventRepository events,
    INotificationDispatcher dispatcher,
    ILogger<EventReminderService> logger) : IEventReminderService
{
    /// <summary>How far ahead of the event a reminder is due (rolling window).</summary>
    public static readonly TimeSpan ReminderHorizon = TimeSpan.FromDays(30);

    public async Task<int> ProcessDueEventsAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var due = await events.FindDueForReminderAsync(now, now.Add(ReminderHorizon), cancellationToken);

        var sent = 0;
        foreach (var customerEvent in due)
        {
            await dispatcher.DispatchAsync(BuildReminder(customerEvent), cancellationToken);
            await events.MarkReminderSentAsync(customerEvent.Id, now, cancellationToken);
            sent++;
            logger.LogInformation(
                "Event reminder dispatched for event {EventId} ({EventType}).", customerEvent.Id, customerEvent.EventType);
        }

        return sent;
    }

    private static Notification BuildReminder(CustomerEvent customerEvent)
    {
        var customerName = customerEvent.Customer?.FullName ?? "A customer";
        var body =
            $"{customerName}'s {customerEvent.EventType} is on {customerEvent.EventDate:yyyy-MM-dd}. " +
            "Consider preparing for the occasion.";
        return new Notification(
            NotificationType.EventReminder,
            "Upcoming customer event",
            body,
            new NotificationTarget(customerEvent.OrganizationId),
            new Dictionary<string, string?>
            {
                ["customerId"] = customerEvent.CustomerId.ToString(),
                ["eventId"] = customerEvent.Id.ToString(),
                ["eventType"] = customerEvent.EventType,
                ["eventDate"] = customerEvent.EventDate.ToString("yyyy-MM-dd"),
            },
            NotificationChannel.Realtime | NotificationChannel.Email);
    }
}
