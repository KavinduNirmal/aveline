using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Eventing;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Audit.Services;
using Aveline.Api.Modules.Notifications.Models;
using Aveline.Api.Modules.Notifications.Repositories;
using Aveline.Api.Modules.Notifications.Services;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Default <see cref="IAlertService"/>. Evaluation is data-driven: each enabled rule
/// aggregates the samples in its window and compares the result with its threshold. A
/// breaching rule either opens a new alert or, while the open alert is inside its cooldown,
/// increments the existing row instead of re-firing (BR-7.6). A non-breaching evaluation
/// increments the persisted consecutive-OK counter and auto-resolves the alert once it
/// reaches <c>Observability:AutoResolveConsecutiveOk</c> (BR-7.7). A critical fire resolves
/// the owner/manager recipients through <see cref="IRecipientResolver"/> and stores a
/// <see cref="NotificationRecord"/> (BR-7.11).
/// </summary>
public sealed class AlertService : IAlertService
{
    private const string AlertFiredEvent = "system.alert.fired";
    private const string AlertAcknowledgedEvent = "system.alert.acknowledged";
    private const string AlertResolvedEvent = "system.alert.resolved";

    private readonly AppDbContext _db;
    private readonly IAuditService _audit;
    private readonly IEventBus _eventBus;
    private readonly INotificationRepository _notifications;
    private readonly IRecipientResolver _recipients;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AlertService> _logger;

    public AlertService(
        AppDbContext db,
        IAuditService audit,
        IEventBus eventBus,
        INotificationRepository notifications,
        IRecipientResolver recipients,
        IConfiguration configuration,
        ILogger<AlertService> logger)
    {
        _db = db;
        _audit = audit;
        _eventBus = eventBus;
        _notifications = notifications;
        _recipients = recipients;
        _configuration = configuration;
        _logger = logger;
    }

    private int AutoResolveConsecutiveOk => _configuration.GetValue("Observability:AutoResolveConsecutiveOk", 3);

    public async Task<int> EvaluateAsync(CancellationToken cancellationToken = default)
    {
        var rules = await _db.SystemAlertRules
            .Where(rule => rule.IsEnabled)
            .OrderBy(rule => rule.Name)
            .ToListAsync(cancellationToken);

        var touched = 0;
        foreach (var rule in rules)
        {
            if (await EvaluateRuleAsync(rule, organizationId: null, cancellationToken) is not null)
            {
                touched++;
            }
        }

        return touched;
    }

    public async Task<SystemAlert?> EvaluateRuleAsync(
        SystemAlertRule rule, Guid? organizationId = null, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var samples = await LoadSamplesAsync(rule, now, cancellationToken);
        var observed = Aggregate(rule.Aggregation, samples, rule.WindowSeconds);

        var open = await _db.SystemAlerts
            .Where(alert => alert.RuleId == rule.Id && alert.Status != AlertStatus.Resolved)
            .OrderByDescending(alert => alert.FiredAt)
            .FirstOrDefaultAsync(cancellationToken);

        // An absent sample is not a healthy reading. Counting it as OK would let a
        // telemetry read failure auto-resolve a firing alert after three passes - exactly
        // the ledger-integrity signal the alert exists to protect (§3.3(c)). Leave the
        // alert state untouched until real data arrives.
        if (observed is null)
        {
            _logger.LogDebug(
                "No {Metric} samples for alert rule {Rule} in the last {Window}s; "
                + "leaving the alert state unchanged.",
                rule.MetricName, rule.Name, rule.WindowSeconds);
            return open;
        }

        if (!Breaches(rule.ComparisonOperator, observed.Value, rule.Threshold))
        {
            return await HandleOkEvaluationAsync(open, now, cancellationToken);
        }

        var observedValue = observed.Value;

        if (open is not null)
        {
            // The cooldown is measured from the last actual fire, not the last observation:
            // a sustained breach must be able to re-fire once the cooldown elapses (BR-7.6).
            var withinCooldown = now - open.FiredAt < TimeSpan.FromSeconds(rule.CooldownSeconds);
            open.LastObservedAt = now;
            open.ObservedValue = observedValue;
            open.Threshold = rule.Threshold;
            open.ConsecutiveOkCount = 0;

            if (withinCooldown)
            {
                open.OccurrenceCount++;
                await _db.SaveChangesAsync(cancellationToken);
                return open;
            }

            // The cooldown elapsed while the breach continued: re-fire, but under the same
            // rolling-hour storm guard as a brand-new alert. Without this a sustained
            // Critical breach re-notified on every cooldown with no throttle (§3.3(a)).
            if (!TryConsumeFireQuota(rule, now))
            {
                _logger.LogWarning(
                    "Alert storm guard suppressed the re-fire of rule {Rule}: "
                    + "{Count} fires in the current rolling hour (max {Max}).",
                    rule.Name, rule.FiresInWindow, rule.MaxAlertsPerHour);

                open.OccurrenceCount++;
                await _db.SaveChangesAsync(cancellationToken);
                return open;
            }

            open.FiredAt = now;
            open.OccurrenceCount = 1;
            open.NotificationRecordId = null;
            rule.LastTriggeredAt = now;
            await _db.SaveChangesAsync(cancellationToken);
            await FireAsync(rule, open, observedValue, cancellationToken);
            return open;
        }

        if (!TryConsumeFireQuota(rule, now))
        {
            _logger.LogWarning(
                "Alert storm guard suppressed rule {Rule}: {Count} fires in the current "
                + "rolling hour (max {Max}).",
                rule.Name, rule.FiresInWindow, rule.MaxAlertsPerHour);
            return null;
        }

        var alert = new SystemAlert
        {
            RuleId = rule.Id,
            OrganizationId = organizationId,
            MetricName = rule.MetricName,
            Severity = rule.Severity,
            Status = AlertStatus.Firing,
            Title = Title(rule),
            Detail = Detail(rule, observedValue),
            ObservedValue = observedValue,
            Threshold = rule.Threshold,
            OccurrenceCount = 1,
            FiredAt = now,
            LastObservedAt = now,
        };

        _db.SystemAlerts.Add(alert);
        rule.LastTriggeredAt = now;
        await _db.SaveChangesAsync(cancellationToken);
        await FireAsync(rule, alert, observedValue, cancellationToken);
        return alert;
    }

    /// <summary>
    /// Consumes one fire from the rule's rolling-hour quota (BR-7.6). Returns false when the
    /// quota is exhausted, in which case the caller must not fire or notify. The counter is
    /// persisted on the rule because a re-fire resets the alert row's own timestamps.
    /// </summary>
    private static bool TryConsumeFireQuota(SystemAlertRule rule, DateTime now)
    {
        if (rule.FireWindowStart is not { } windowStart
            || now - windowStart >= TimeSpan.FromHours(1)
            || now < windowStart)
        {
            rule.FireWindowStart = now;
            rule.FiresInWindow = 0;
        }

        if (rule.FiresInWindow >= rule.MaxAlertsPerHour)
        {
            return false;
        }

        rule.FiresInWindow++;
        return true;
    }

    public async Task<SystemAlert?> AcknowledgeAsync(
        Guid alertId, Guid userId, string? note = null, CancellationToken cancellationToken = default)
    {
        var alert = await _db.SystemAlerts.FirstOrDefaultAsync(
            candidate => candidate.Id == alertId, cancellationToken);
        if (alert is null)
        {
            return null;
        }

        alert.Status = AlertStatus.Acknowledged;
        alert.AcknowledgedAt = DateTime.UtcNow;
        alert.AcknowledgedByUserId = userId;
        if (!string.IsNullOrWhiteSpace(note))
        {
            alert.ResolutionNote = note;
        }

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntryRequest(
                AuditAction.SystemAlertAcknowledged,
                "SystemAlert",
                alert.Id.ToString(),
                OrganizationId: alert.OrganizationId,
                ActorKind: AuditActorKind.User,
                ActorUserId: userId,
                Reason: note),
            cancellationToken);

        await PublishAsync(AlertAcknowledgedEvent, alert, cancellationToken);
        return alert;
    }

    public async Task<IReadOnlyList<SystemAlertRule>> ListRulesAsync(CancellationToken cancellationToken = default)
        => await _db.SystemAlertRules
            .AsNoTracking()
            .OrderBy(rule => rule.Name)
            .ToListAsync(cancellationToken);

    public async Task<SystemAlertRule> CreateRuleAsync(
        SystemAlertRule rule, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        rule.CreatedByUserId = actorUserId;
        rule.CreatedAt = DateTime.UtcNow;
        rule.UpdatedAt = rule.CreatedAt;

        _db.SystemAlertRules.Add(rule);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntryRequest(
                AuditAction.SystemAlertRuleCreated,
                "SystemAlertRule",
                rule.Id.ToString(),
                ActorKind: AuditActorKind.User,
                ActorUserId: actorUserId,
                After: new { rule.Name, rule.MetricName, rule.Threshold }),
            cancellationToken);

        return rule;
    }

    public async Task<SystemAlertRule> UpdateRuleAsync(
        SystemAlertRule rule, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.SystemAlertRules.FirstOrDefaultAsync(
            candidate => candidate.Id == rule.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Alert rule {rule.Id} was not found.");

        existing.Name = rule.Name;
        existing.MetricName = rule.MetricName;
        existing.Aggregation = rule.Aggregation;
        existing.ComparisonOperator = rule.ComparisonOperator;
        existing.Threshold = rule.Threshold;
        existing.WindowSeconds = rule.WindowSeconds;
        existing.Severity = rule.Severity;
        existing.IsEnabled = rule.IsEnabled;
        existing.CooldownSeconds = rule.CooldownSeconds;
        existing.MaxAlertsPerHour = rule.MaxAlertsPerHour;
        existing.TargetRoles = rule.TargetRoles;
        existing.DimensionFiltersJson = rule.DimensionFiltersJson;
        existing.UpdatedAt = DateTime.UtcNow;

        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntryRequest(
                AuditAction.SystemAlertRuleUpdated,
                "SystemAlertRule",
                existing.Id.ToString(),
                ActorKind: AuditActorKind.User,
                ActorUserId: actorUserId,
                After: new { existing.Name, existing.MetricName, existing.Threshold }),
            cancellationToken);

        return existing;
    }

    public async Task<bool> DeleteRuleAsync(
        Guid ruleId, Guid actorUserId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.SystemAlertRules.FirstOrDefaultAsync(
            candidate => candidate.Id == ruleId, cancellationToken);
        if (existing is null)
        {
            return false;
        }

        _db.SystemAlertRules.Remove(existing);
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntryRequest(
                AuditAction.SystemAlertRuleDeleted,
                "SystemAlertRule",
                ruleId.ToString(),
                ActorKind: AuditActorKind.User,
                ActorUserId: actorUserId,
                Before: new { existing.Name }),
            cancellationToken);

        return true;
    }

    private async Task<SystemAlert?> HandleOkEvaluationAsync(
        SystemAlert? open, DateTime now, CancellationToken cancellationToken)
    {
        if (open is null)
        {
            return null;
        }

        open.ConsecutiveOkCount++;
        open.LastObservedAt = now;
        await _db.SaveChangesAsync(cancellationToken);

        if (open.ConsecutiveOkCount < AutoResolveConsecutiveOk)
        {
            return open;
        }

        open.Status = AlertStatus.Resolved;
        open.ResolvedAt = now;
        open.ResolutionNote ??= "Auto-resolved after consecutive below-threshold evaluations.";
        await _db.SaveChangesAsync(cancellationToken);

        await _audit.RecordAsync(
            new AuditEntryRequest(
                AuditAction.SystemAlertResolved,
                "SystemAlert",
                open.Id.ToString(),
                OrganizationId: open.OrganizationId,
                ActorKind: AuditActorKind.System,
                Reason: open.ResolutionNote),
            cancellationToken);

        await PublishAsync(AlertResolvedEvent, open, cancellationToken);
        return open;
    }

    private async Task FireAsync(
        SystemAlertRule rule, SystemAlert alert, decimal observedValue, CancellationToken cancellationToken)
    {
        await PublishAsync(AlertFiredEvent, alert, cancellationToken);

        if (rule.Severity != AlertSeverity.Critical || alert.NotificationRecordId is not null)
        {
            return;
        }

        if (alert.OrganizationId is not { } organizationId)
        {
            _logger.LogInformation(
                "Critical alert {Rule} is system-wide; no organization-scoped notification was created.",
                rule.Name);
            return;
        }

        var notification = new Notification(
            NotificationType.SystemAlert,
            alert.Title,
            alert.Detail ?? alert.Title,
            new NotificationTarget(organizationId, rule.TargetRoles),
            new Dictionary<string, string?>
            {
                ["alertId"] = alert.Id.ToString(),
                ["ruleId"] = rule.Id.ToString(),
                ["metricName"] = rule.MetricName,
                ["observedValue"] = observedValue.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["threshold"] = rule.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture),
            },
            NotificationChannel.Realtime | NotificationChannel.Push);

        var recipients = await _recipients.ResolveAsync(notification, cancellationToken);
        if (recipients.Count == 0)
        {
            _logger.LogInformation(
                "Critical alert {Rule} resolved to no organization recipients.", rule.Name);
            return;
        }

        var record = await _notifications.AddAsync(
            new NotificationRecord
            {
                OrganizationId = organizationId,
                Type = notification.Type,
                Title = notification.Title,
                Body = notification.Body,
                DataJson = JsonSerializer.Serialize(notification.Data),
            },
            cancellationToken);

        alert.NotificationRecordId = record.Id;
        await _db.SaveChangesAsync(cancellationToken);
    }

    private async Task PublishAsync(string eventType, SystemAlert alert, CancellationToken cancellationToken)
    {
        try
        {
            await _eventBus.PublishAsync(
                eventType,
                alert.OrganizationId,
                new
                {
                    alertId = alert.Id,
                    ruleId = alert.RuleId,
                    metricName = alert.MetricName,
                    severity = alert.Severity.ToString(),
                    status = alert.Status.ToString(),
                    observedValue = alert.ObservedValue,
                    threshold = alert.Threshold,
                    firedAt = alert.FiredAt,
                },
                cancellationToken: cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Publishing is best-effort (ADR-014); the alert row is the durable record.
            _logger.LogWarning(exception, "Failed to publish {EventType} for alert {AlertId}.", eventType, alert.Id);
        }
    }

    /// <summary>
    /// Loads the samples a rule aggregates. <see cref="SystemMetricSample"/> has no
    /// organization column and the collector writes org-agnostic <c>{}</c> dimensions, so
    /// this query cannot and does not filter by organization (M-22). The
    /// <c>organizationId</c> accepted by <see cref="EvaluateRuleAsync"/> only scopes the
    /// alert it fires; a future org-scoped metric would carry the organization in
    /// <c>DimensionsJson</c> and be selected through the rule's dimension filter.
    /// </summary>
    private async Task<IReadOnlyList<SystemMetricSample>> LoadSamplesAsync(
        SystemAlertRule rule, DateTime now, CancellationToken cancellationToken)
    {
        var windowStart = now.AddSeconds(-rule.WindowSeconds);
        var query = _db.SystemMetricSamples
            .AsNoTracking()
            .Where(sample => sample.MetricName == rule.MetricName
                             && sample.WindowStart >= windowStart
                             && sample.WindowStart <= now);

        if (!string.IsNullOrWhiteSpace(rule.DimensionFiltersJson))
        {
            query = query.Where(sample => sample.DimensionsJson == rule.DimensionFiltersJson);
        }

        return await query.OrderBy(sample => sample.WindowStart).ToListAsync(cancellationToken);
    }

    /// <summary>Aggregates the window; <c>null</c> when there is nothing to aggregate.</summary>
    internal static decimal? Aggregate(
        AlertAggregation aggregation, IReadOnlyList<SystemMetricSample> samples, int windowSeconds)
    {
        var values = samples
            .Select(sample => sample.Value)
            .Where(value => value is not null)
            .Select(value => value!.Value)
            .ToList();

        if (values.Count == 0)
        {
            return null;
        }

        return aggregation switch
        {
            AlertAggregation.Avg => values.Average(),
            AlertAggregation.Max => values.Max(),
            AlertAggregation.Min => values.Min(),
            AlertAggregation.Sum => values.Sum(),
            AlertAggregation.Count => values.Count,
            AlertAggregation.Rate => Rate(samples, windowSeconds),
            _ => null,
        };
    }

    internal static bool Breaches(AlertComparisonOperator comparison, decimal value, decimal threshold)
        => comparison switch
        {
            AlertComparisonOperator.Gt => value > threshold,
            AlertComparisonOperator.Gte => value >= threshold,
            AlertComparisonOperator.Lt => value < threshold,
            AlertComparisonOperator.Lte => value <= threshold,
            AlertComparisonOperator.Eq => value == threshold,
            _ => false,
        };

    /// <summary>
    /// Per-minute increase over the window. Positive deltas are summed so a counter reset
    /// does not produce a negative rate.
    /// </summary>
    private static decimal Rate(IReadOnlyList<SystemMetricSample> samples, int windowSeconds)
    {
        var ordered = samples.OrderBy(sample => sample.WindowStart).ToList();
        if (ordered.Count < 2)
        {
            return 0m;
        }

        var increase = 0m;
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1].Value ?? 0m;
            var current = ordered[i].Value ?? 0m;
            if (current > previous)
            {
                increase += current - previous;
            }
        }

        var minutes = Math.Max(1m, windowSeconds / 60m);
        return increase / minutes;
    }

    private static string Title(SystemAlertRule rule) => rule.ComparisonOperator switch
    {
        AlertComparisonOperator.Gt or AlertComparisonOperator.Gte => $"{rule.Name} above threshold",
        AlertComparisonOperator.Lt or AlertComparisonOperator.Lte => $"{rule.Name} below threshold",
        _ => $"{rule.Name} met threshold",
    };

    private static string Detail(SystemAlertRule rule, decimal observedValue) =>
        $"{rule.MetricName} {rule.Aggregation.ToString().ToLowerInvariant()} was " +
        $"{observedValue.ToString(System.Globalization.CultureInfo.InvariantCulture)} over the last " +
        $"{rule.WindowSeconds}s (threshold {rule.Threshold.ToString(System.Globalization.CultureInfo.InvariantCulture)}).";
}
