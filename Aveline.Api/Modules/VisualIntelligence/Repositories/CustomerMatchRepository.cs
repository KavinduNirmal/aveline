using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public class CustomerMatchRepository : ICustomerMatchRepository
{
    private readonly AppDbContext _db;

    public CustomerMatchRepository(AppDbContext db)
    {
        _db = db ?? throw new ArgumentNullException(nameof(db));
    }

    public async Task<IReadOnlyList<CustomerMatch>> GetByItemIdAsync(Guid itemId, Guid orgId, double minScore = 0.7, CancellationToken cancellationToken = default)
    {
        return await _db.CustomerMatches
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.ItemId == itemId && x.MatchScore >= minScore)
            .OrderByDescending(x => x.MatchScore)
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<CustomerMatch>> GetByCustomerIdAsync(Guid customerId, Guid orgId, CancellationToken cancellationToken = default)
    {
        return await _db.CustomerMatches
            .AsNoTracking()
            .Where(x => x.OrgId == orgId && x.CustomerId == customerId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);
    }

    public async Task AddRangeAsync(IEnumerable<CustomerMatch> matches, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(matches);
        await _db.CustomerMatches.AddRangeAsync(matches, cancellationToken);
        await _db.SaveChangesAsync(cancellationToken);
    }
}
