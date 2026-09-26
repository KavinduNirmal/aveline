using Aveline.Api.Modules.Statistics.Models;

namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>
/// Evaluates the data-driven <see cref="SystemAlertRule"/>s, manages the alerts they
/// produce and supports acknowledgement and rule management (FR-7.11–FR-7.12, BR-7.5–BR-7.7,
/// BR-7.11).
/// </summary>
public interface IAlertService
{
    /// <summary>
    /// Evaluates every enabled rule and returns the number of alerts created, updated or
    /// resolved by this pass.
    /// </summary>
    Task<int> EvaluateAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Evaluates one rule. <paramref name="organizationId"/> scopes a produced alert (and its
    /// critical notification) to an organization; the scheduled job evaluates system-wide.
    /// System metric samples are org-agnostic (they have no organization column and carry
    /// <c>{}</c> dimensions), so this parameter does not filter the aggregated samples — it
    /// is reserved for the fired alert and would only scope a metric that carried the
    /// organization in its dimensions (M-22).
    /// </summary>
    Task<SystemAlert?> EvaluateRuleAsync(
        SystemAlertRule rule, Guid? organizationId = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Acknowledges an alert, or returns <c>null</c> when it does not exist. A
    /// <c>Resolved</c> alert is terminal and throws <see cref="AlertStateConflictException"/>
    /// (HTTP 409) rather than being silently re-opened to <c>Acknowledged</c>.
    /// </summary>
    Task<SystemAlert?> AcknowledgeAsync(
        Guid alertId, Guid userId, string? note = null, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SystemAlertRule>> ListRulesAsync(CancellationToken cancellationToken = default);

    Task<SystemAlertRule> CreateRuleAsync(
        SystemAlertRule rule, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<SystemAlertRule> UpdateRuleAsync(
        SystemAlertRule rule, Guid actorUserId, CancellationToken cancellationToken = default);

    Task<bool> DeleteRuleAsync(Guid ruleId, Guid actorUserId, CancellationToken cancellationToken = default);
}
