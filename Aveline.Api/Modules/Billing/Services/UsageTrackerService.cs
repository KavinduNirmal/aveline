using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Repositories;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Billing.Services;

/// <summary>
/// Default implementation of <see cref="IUsageTrackerService"/>.
/// Calculates Blossom units from token counts, persists the immutable usage record,
/// and updates the organisation's billing period ledger — all within a single transaction.
/// </summary>
/// <remarks>
/// Blossom formula (ADR-010, initial implementation):
/// <c>blossom_units = ceil((input + output + cached) / 1000, 1 dp)</c>, minimum 0.1.
/// </remarks>
public sealed class UsageTrackerService(
    IUsageRepository usageRepository,
    ILogger<UsageTrackerService> logger,
    IConfiguration configuration) : IUsageTrackerService
{
    // Default plan limits per tier (Blossoms/month). These values are used when creating
    // a new UsageAccount for the period and no billing integration has assigned a limit yet.
    // They mirror the pricing_plan.md tables and will be superseded by subscription data.
    private static readonly Dictionary<PlanTier, decimal> DefaultBlossomLimits = new()
    {
        { PlanTier.Seed,       150m },
        { PlanTier.Bloom,      750m },
        { PlanTier.Orchid,    2000m },
        { PlanTier.Rose,      5000m },
        { PlanTier.Enterprise, 9999m },
    };

    // The default tier assigned to new usage accounts until the subscription system sets one.
    private const PlanTier DefaultNewAccountTier = PlanTier.Seed;

    /// <inheritdoc/>
    public async Task<AiUsageRecord> RecordWorkflowUsageAsync(
        RecordUsageRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        decimal blossomUnits = CalculateBlossomUnits(
            request.InputTokens, request.OutputTokens, request.CachedTokens);

        var record = new AiUsageRecord
        {
            OrganizationId = request.OrganizationId,
            RequestId      = request.RequestId,
            WorkflowId     = request.WorkflowId,
            Provider       = request.Provider,
            Model          = request.Model,
            InputTokens    = request.InputTokens,
            OutputTokens   = request.OutputTokens,
            CachedTokens   = request.CachedTokens,
            ActualCostUsd  = request.ActualCostUsd,
            BlossomUnits   = blossomUnits,
        };

        await usageRepository.AddUsageRecordAndUpdateAccountAsync(record, cancellationToken);

        logger.LogInformation(
            "Usage recorded: org={OrganizationId} workflow={WorkflowId} model={Model} " +
            "tokens={TotalTokens} blossoms={BlossomUnits} cost_usd={ActualCostUsd}",
            record.OrganizationId, record.WorkflowId, record.Model,
            record.InputTokens + record.OutputTokens + record.CachedTokens,
            record.BlossomUnits, record.ActualCostUsd);

        CheckAbnormalCost(record);

        return record;
    }

    /// <inheritdoc/>
    public Task<UsageAccount> GetOrCreateCurrentAccountAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var (periodStart, periodEnd) = GetCurrentPeriod();
        decimal limit = DefaultBlossomLimits[DefaultNewAccountTier];
        return usageRepository.GetOrCreateAccountAsync(
            organizationId, periodStart, periodEnd, limit, cancellationToken);
    }

    /// <inheritdoc/>
    public async Task<UsageSummary> GetUsageSummaryAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var account = await GetOrCreateCurrentAccountAsync(organizationId, cancellationToken);
        return new UsageSummary(
            account.OrganizationId,
            account.PeriodStart,
            account.PeriodEnd,
            account.MonthlyBlossomLimit,
            account.BlossomUsed,
            account.BlossomRemaining,
            account.Status);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Calculates Blossom units from raw token counts.
    /// Formula: ceil((input + output + cached) / 1000, 1 dp), minimum 0.1.
    /// See ADR-010 for the rationale and planned evolution to cost-based normalization.
    /// </summary>
    public static decimal CalculateBlossomUnits(int inputTokens, int outputTokens, int cachedTokens)
    {
        decimal totalTokens = inputTokens + outputTokens + cachedTokens;
        decimal raw = totalTokens / 1000m;
        // Ceiling to 1 decimal place
        decimal ceiled = Math.Ceiling(raw * 10m) / 10m;
        return Math.Max(ceiled, 0.1m);
    }

    private static (DateTime periodStart, DateTime periodEnd) GetCurrentPeriod()
    {
        var now = DateTime.UtcNow;
        var start = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Utc);
        var end   = start.AddMonths(1);
        return (start, end);
    }

    private static void ValidateRequest(RecordUsageRequest request)
    {
        if (request.OrganizationId == Guid.Empty)
            throw new Models.InvalidUsageRecordException("OrganizationId must not be empty.");
        if (string.IsNullOrWhiteSpace(request.WorkflowId))
            throw new Models.InvalidUsageRecordException("WorkflowId must not be empty.");
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new Models.InvalidUsageRecordException("Model must not be empty.");
        if (request.InputTokens < 0 || request.OutputTokens < 0 || request.CachedTokens < 0)
            throw new Models.InvalidUsageRecordException("Token counts must be non-negative.");
        if (request.ActualCostUsd < 0)
            throw new Models.InvalidUsageRecordException("ActualCostUsd must be non-negative.");
    }

    private void CheckAbnormalCost(AiUsageRecord record)
    {
        // Read the threshold from configuration; default to 1.00 USD if not configured.
        // Operators can tune this via Billing:AbnormalCostThresholdUsd in appsettings.
        decimal threshold = configuration.GetValue<decimal>(
            "Billing:AbnormalCostThresholdUsd", defaultValue: 1.00m);

        if (record.ActualCostUsd > threshold)
        {
            logger.LogWarning(
                "[ABNORMAL_USAGE] Workflow cost exceeded threshold: org={OrganizationId} " +
                "workflow={WorkflowId} model={Model} cost_usd={ActualCostUsd} threshold={Threshold}",
                record.OrganizationId, record.WorkflowId, record.Model,
                record.ActualCostUsd, threshold);
        }
    }
}
