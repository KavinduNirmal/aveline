using Microsoft.Extensions.Caching.Memory;

namespace Aveline.Api.Modules.Billing.Domain;

/// <summary>
/// Small in-process (L1) cache for resolved conversion rules (FR-1.10). A generation
/// counter is part of every key, so a rule activation or cancellation invalidates every
/// cached entry atomically without enumerating keys.
/// </summary>
public sealed class PricingRuleCache(IMemoryCache cache)
{
    public static readonly TimeSpan DefaultTtl = TimeSpan.FromSeconds(5);

    private long _generation;

    public bool TryGet(string scopeKey, DateTime at, out PricingRuleSnapshot? snapshot)
    {
        if (cache.TryGetValue(Key(scopeKey, at), out var value) && value is PricingRuleSnapshot cached)
        {
            snapshot = cached;
            return true;
        }

        snapshot = null;
        return false;
    }

    public void Set(string scopeKey, DateTime at, PricingRuleSnapshot snapshot)
    {
        cache.Set(Key(scopeKey, at), snapshot, DefaultTtl);
    }

    public void Invalidate() => Interlocked.Increment(ref _generation);

    private string Key(string scopeKey, DateTime at) =>
        $"pricing:rule:g{Interlocked.Read(ref _generation)}:{scopeKey}:{at:yyyyMMddHHmm}";
}
