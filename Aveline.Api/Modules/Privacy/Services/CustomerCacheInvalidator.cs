using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Removes the cached copies of a customer that a database erasure cannot reach (plan §7.3 "Redis
/// caches", §11 item 5.6, risk R-10).
/// </summary>
/// <remarks>
/// <para>
/// <b>What it can name, and what it cannot.</b> The agent service caches a profile under
/// <c>customer_profile:{customerId}</c> (<c>app/services/profile_cache.py</c>) and this API caches a
/// lookup under <c>customer:lookup:{orgId}:{name}:{phone}:{email}</c>
/// (<c>CustomerService.BuildLookupKey</c>). Those are the two customer-keyed caches; both are
/// invalidated here. The agent service's <c>semantic_cache</c> is keyed by prompt + system + model
/// and holds no customer key, so it cannot be invalidated per subject — it is left to its TTL, and
/// that limitation is recorded in <c>docs/backend/domain-model.md</c>.
/// </para>
/// <para>
/// <b>Best-effort, never fatal.</b> This runs after the erasure transaction has committed, so a
/// Redis outage must not turn a completed deletion into a <c>500</c>; the failure is logged and the
/// caller still gets its counts. The short (60 s) lookup-cache TTL bounds the exposure either way.
/// </para>
/// </remarks>
public interface ICustomerCacheInvalidator
{
    /// <summary>
    /// Evicts the profile and lookup entries that could still surface the erased customer. Returns
    /// the number of keys the store confirmed removed.
    /// </summary>
    Task<int> InvalidateAsync(
        Guid organizationId,
        Guid customerId,
        string phoneE164,
        string? fullName = null,
        string? email = null,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class CustomerCacheInvalidator : ICustomerCacheInvalidator
{
    /// <summary>The Python agent's profile-cache prefix (<c>profile_cache.py</c>).</summary>
    public const string ProfilePrefix = "customer_profile:";

    /// <summary>The API-side lookup-cache prefix (<c>CustomerService.BuildLookupKey</c>).</summary>
    public const string LookupPrefix = "customer:lookup:";

    private readonly IDistributedCache _cache;
    private readonly ILogger<CustomerCacheInvalidator> _logger;

    public CustomerCacheInvalidator(IDistributedCache cache, ILogger<CustomerCacheInvalidator> logger)
    {
        _cache = cache;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<int> InvalidateAsync(
        Guid organizationId,
        Guid customerId,
        string phoneE164,
        string? fullName = null,
        string? email = null,
        CancellationToken cancellationToken = default)
    {
        var removed = 0;

        foreach (var key in BuildKeys(organizationId, customerId, phoneE164, fullName, email))
        {
            try
            {
                await _cache.RemoveAsync(key, cancellationToken);
                removed++;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Best-effort: the database is authoritative and the write already committed. The
                // key itself is never logged: the lookup keys contain the name/phone/email, so a log
                // line would be the PII leak the erasure just removed.
                _logger.LogWarning(
                    ex,
                    "Could not evict one customer cache entry after an erasure; it expires on its own TTL.");
            }
        }

        return removed;
    }

    /// <summary>
    /// The keys that can name this customer. <c>CustomerService.BuildLookupKey</c> joins the trimmed
    /// name, phone and email, so every subset of the values the customer actually has is rebuilt
    /// here: a partial-name lookup cannot be enumerated, and is left to the 60-second TTL.
    /// </summary>
    internal static IEnumerable<string> BuildKeys(
        Guid organizationId, Guid customerId, string phoneE164, string? fullName, string? email)
    {
        yield return $"{ProfilePrefix}{customerId:D}";

        var org = organizationId.ToString("D");
        var name = fullName?.Trim().ToLowerInvariant() ?? string.Empty;
        var phone = phoneE164.Trim();
        var mail = email?.Trim().ToLowerInvariant() ?? string.Empty;

        foreach (var (candidateName, candidatePhone, candidateEmail) in Subsets(name, phone, mail))
        {
            yield return $"{LookupPrefix}{org}:{candidateName}:{candidatePhone}:{candidateEmail}";
        }
    }

    private static IEnumerable<(string Name, string Phone, string Email)> Subsets(
        string name, string phone, string email)
    {
        // Empty values are still meaningful: a lookup by phone alone writes an empty name segment.
        var names = name.Length == 0 ? new[] { string.Empty } : new[] { string.Empty, name };
        var phones = phone.Length == 0 ? new[] { string.Empty } : new[] { string.Empty, phone };
        var emails = email.Length == 0 ? new[] { string.Empty } : new[] { string.Empty, email };

        foreach (var candidateName in names)
        {
            foreach (var candidatePhone in phones)
            {
                foreach (var candidateEmail in emails)
                {
                    if (candidateName.Length == 0 && candidatePhone.Length == 0 && candidateEmail.Length == 0)
                    {
                        continue;
                    }

                    yield return (candidateName, candidatePhone, candidateEmail);
                }
            }
        }
    }
}
