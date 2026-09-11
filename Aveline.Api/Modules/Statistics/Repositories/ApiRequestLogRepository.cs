using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Statistics.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Statistics.Repositories;

/// <summary>Insert-only EF implementation of <see cref="IApiRequestLogRepository"/>.</summary>
public sealed class ApiRequestLogRepository(AppDbContext db) : IApiRequestLogRepository
{
    public async Task InsertAsync(
        IReadOnlyList<ApiRequestLog> logs, CancellationToken cancellationToken = default)
    {
        if (logs.Count == 0)
        {
            return;
        }

        db.ApiRequestLogs.AddRange(logs);
        await db.SaveChangesAsync(cancellationToken);
    }
}
