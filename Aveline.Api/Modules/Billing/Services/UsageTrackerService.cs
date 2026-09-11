using Aveline.Api.Modules.Billing.Domain;
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
/// When <c>Pricing:UseLegacyFormula</c> is false the conversion rule resolved for the
/// workflow's (provider, model) at ingest time is applied (BR-1.12). The legacy
/// formula — <c>ceil((input + output + cached) / 1000, 1 dp)</c>, minimum 0.1 —
/// remains the escape hatch and the fallback when no rule matches.
/// </remarks>
public sealed class UsageTrackerService(
    IUsageRepository usageRepository,
    ILogger<UsageTrackerService> logger,
    IConfiguration configuration,
    IPricingService? pricingService = null,
    IEntitlementResolver? entitlementResolver = null) : IUsageTrackerService
{
    /// <summary>Fallback allowance when no entitlement resolver is registered.</summary>
    private const decimal SeedFallbackLimit = 150m;

    /// <summary>The entitlement key that supplies the monthly Blossom allowance.</summary>
    public const string MonthlyBlossomsKey = "blossoms.monthly";

    /// <inheritdoc/>
    public async Task<AiUsageRecord> RecordWorkflowUsageAsync(
        RecordUsageRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);

        var (blossomUnits, pricingSnapshot) = await CalculateBlossomUnitsAsync(request, cancellationToken);
        decimal blossomLimit = await ResolveBlossomLimitAsync(request.OrganizationId, cancellationToken);

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
            PricingRuleId  = pricingSnapshot?.RuleId,
            PricingRuleVersion = pricingSnapshot?.Version,
            UnitsPerBlossom = pricingSnapshot?.UnitsPerBlossom,
            RoundingMode = pricingSnapshot?.RoundingMode,
            RoundingDecimals = pricingSnapshot is null ? null : (short)pricingSnapshot.RoundingDecimals,
            NormalizedUnits = pricingSnapshot is null
                ? null
                : (long)request.InputTokens + request.OutputTokens + request.CachedTokens,
        };

        await usageRepository.AddUsageRecordAndUpdateAccountAsync(record, blossomLimit, cancellationToken);

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
    public async Task<UsageAccount> GetOrCreateCurrentAccountAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var (periodStart, periodEnd) = GetCurrentPeriod();
        var limit = await ResolveBlossomLimitAsync(organizationId, cancellationToken);
        return await usageRepository.GetOrCreateAccountAsync(
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

    /// <summary>
    /// Resolves the conversion rule at ingest time and applies it, unless the legacy
    /// formula is enabled or no pricing service is available. Callers can never choose
    /// their own price: the timestamp is always the API ingest time (BR-1.12).
    /// </summary>
    private async Task<(decimal BlossomUnits, PricingRuleSnapshot? Snapshot)> CalculateBlossomUnitsAsync(
        RecordUsageRequest request, CancellationToken cancellationToken)
    {
        var useLegacyFormula = configuration.GetValue("Pricing:UseLegacyFormula", defaultValue: true);
        if (useLegacyFormula || pricingService is null)
        {
            return (CalculateBlossomUnits(request.InputTokens, request.OutputTokens, request.CachedTokens), null);
        }

        var resolution = await pricingService.ResolveAsync(
            request.Provider, request.Model, DateTime.UtcNow, cancellationToken);

        var rule = resolution.Rule;
        long normalizedUnits = (long)request.InputTokens + request.OutputTokens + request.CachedTokens;

        var blossomUnits = BlossomCalculator.Calculate(
            normalizedUnits,
            rule.UnitsPerBlossom,
            rule.RoundingMode,
            rule.RoundingDecimals,
            rule.MinimumChargeBlossoms);

        // A fallback resolution is not a real rule, so no snapshot is stored (NULL means
        // "assume 1000" on the read path).
        return (blossomUnits, resolution.IsFallback ? null : rule);
    }

    /// <summary>
    /// Resolves the organisation's monthly Blossom allowance from the entitlement catalog
    /// (fixes defects D-1 and D-2: the limit was previously always the Seed value).
    /// </summary>
    private async Task<decimal> ResolveBlossomLimitAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        if (entitlementResolver is null)
        {
            return SeedFallbackLimit;
        }

        return await entitlementResolver.GetDecimalAsync(
            organizationId, MonthlyBlossomsKey, SeedFallbackLimit, at: null, cancellationToken);
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
