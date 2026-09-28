using System.Collections.Frozen;
using System.Diagnostics;
using Aveline.Api.Common.Jobs;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Jobs;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Telemetry;

/// <summary>
/// Rebuilds the Clerk-id → GUID claim map off the request path, every five minutes.
/// </summary>
/// <remarks>
/// <para>
/// This exists because a Clerk token carries Clerk's native ids (<c>user_…</c>/<c>org_…</c>)
/// while Aveline stores GUIDs, so <c>RequestPrincipal</c>'s <c>Guid.TryParse</c> chain fails for
/// every human session and <c>ApiRequestMetric.UserId</c> is <c>null</c> (B1). The map is
/// consulted synchronously, so the telemetry middleware keeps its "does no I/O, well under
/// 1 ms to p99" contract.
/// </para>
/// <para>
/// A rebuild reads the two unique, indexed external-id columns. The soft-delete query filter is
/// deliberately ignored: a soft-deleted user is still a real identity for requests already in
/// flight. A failed rebuild keeps the previous contents rather than emptying them, because a
/// stale answer beats no answer for attribution.
/// </para>
/// </remarks>
public sealed class ClaimIdentityMapRefresher(
    IServiceScopeFactory scopeFactory,
    IDistributedJobLock jobLock,
    ClaimIdentityMap map,
    ILogger<ClaimIdentityMapRefresher> logger)
    : StatisticsJobBase(scopeFactory, jobLock, logger)
{
    protected override string JobName => "claim-identity-map";

    protected override TimeSpan Interval => TimeSpan.FromMinutes(5);

    public override async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Users.ClerkId is unique and indexed. IgnoreQueryFilters so a soft-deleted user
            // still resolves; the raw Clerk id is never persisted by this job.
            var users = await db.Users
                .IgnoreQueryFilters()
                .AsNoTracking()
                .Where(user => user.ClerkId != string.Empty)
                .Select(user => new KeyValuePair<string, Guid>(user.ClerkId, user.Id))
                .ToListAsync(cancellationToken);

            // Organizations.ClerkOrgId is unique and indexed.
            var organizations = await db.Organizations
                .AsNoTracking()
                .Where(organization => organization.ClerkOrgId != null && organization.ClerkOrgId != string.Empty)
                .Select(organization => new KeyValuePair<string, Guid>(organization.ClerkOrgId!, organization.Id))
                .ToListAsync(cancellationToken);

            // Build both dictionaries before the swap, so a failure anywhere leaves the
            // previous contents untouched.
            var userMap = users
                .Where(entry => !string.IsNullOrEmpty(entry.Key))
                .ToFrozenDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);
            var organizationMap = organizations
                .Where(entry => !string.IsNullOrEmpty(entry.Key))
                .ToFrozenDictionary(entry => entry.Key, entry => entry.Value, StringComparer.Ordinal);

            map.Replace(userMap, organizationMap);

            return userMap.Count + organizationMap.Count;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(
                exception,
                "ClaimIdentityMap refresh failed; keeping the previous map. unresolvedRequests={UnresolvedCount}",
                map.UnresolvedCount);
            return 0;
        }
    }
}
