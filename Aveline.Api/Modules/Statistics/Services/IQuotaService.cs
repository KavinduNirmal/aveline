namespace Aveline.Api.Modules.Statistics.Services;

/// <summary>One evaluated quota meter for a scope and period.</summary>
public sealed record QuotaCheck(
    string MetricKey,
    long Limit,
    long Used,
    DateTime PeriodStart,
    DateTime PeriodEnd)
{
    /// <summary><c>0</c> (or negative) means "not configured" and is unlimited.</summary>
    public bool IsUnlimited => Limit <= 0;

    public bool IsExhausted => !IsUnlimited && Used >= Limit;

    public long? Remaining => IsUnlimited ? null : Math.Max(0, Limit - Used);

    public bool IsWarning(int warningPercent) =>
        !IsUnlimited && !IsExhausted && Used * 100 >= Limit * warningPercent;

    public DateTime ResetsAt => PeriodEnd;
}

/// <summary>The outcome of evaluating every quota meter for a request's scope.</summary>
public sealed record QuotaEvaluation(IReadOnlyList<QuotaCheck> Checks, bool Enforced)
{
    public QuotaCheck? Exhausted => Checks.FirstOrDefault(check => check.IsExhausted);

    public bool IsExhausted => Exhausted is not null;
}

/// <summary>
/// Resolves and consumes the per-plan request quotas (<c>api.requests.monthly</c> and
/// <c>api.requests.perMinute</c>) via <c>IEntitlementResolver</c> and an atomic counter.
/// </summary>
public interface IQuotaService
{
    /// <summary>True only when <c>Quotas:EnforcementEnabled</c> is set.</summary>
    bool EnforcementEnabled { get; }

    /// <summary>Consumes one request from each configured meter and reports exhaustion.</summary>
    Task<QuotaEvaluation> EvaluateAsync(
        Guid organizationId, Guid? apiKeyId, CancellationToken cancellationToken = default);

    /// <summary>Reads the current usage without consuming anything (S-30).</summary>
    Task<QuotaEvaluation> GetStatusAsync(
        Guid organizationId, Guid? apiKeyId, CancellationToken cancellationToken = default);
}
