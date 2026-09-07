using Aveline.Api.Modules.Billing.Models;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Request DTO submitted by the agent service after each LangGraph workflow completes.
/// BlossomUnits must never be included — they are always calculated server-side.
/// </summary>
public sealed record RecordUsageRequest(
    Guid OrganizationId,
    string RequestId,
    string WorkflowId,
    string Provider,
    string Model,
    int InputTokens,
    int OutputTokens,
    int CachedTokens,
    decimal ActualCostUsd);

/// <summary>
/// A lightweight summary of an organisation's Blossom usage for the current billing period.
/// </summary>
public sealed record UsageSummary(
    Guid OrganizationId,
    DateTime PeriodStart,
    DateTime PeriodEnd,
    decimal MonthlyBlossomLimit,
    decimal BlossomUsed,
    decimal BlossomRemaining,
    UsageAccountStatus Status);

public interface IUsageTrackerService
{
    /// <summary>
    /// Records the AI usage from a completed agent workflow and updates the
    /// organisation's Blossom balance for the current billing period.
    /// Both operations execute within a single database transaction.
    /// </summary>
    Task<AiUsageRecord> RecordWorkflowUsageAsync(
        RecordUsageRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns or creates the active <see cref="UsageAccount"/> for the given organisation
    /// and the current calendar month.
    /// </summary>
    Task<UsageAccount> GetOrCreateCurrentAccountAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Returns a lightweight summary of the current billing period for an organisation.
    /// </summary>
    Task<UsageSummary> GetUsageSummaryAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default);
}
