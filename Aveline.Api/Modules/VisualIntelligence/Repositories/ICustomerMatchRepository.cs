using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;

namespace Aveline.Api.Modules.VisualIntelligence.Repositories;

public interface ICustomerMatchRepository
{
    Task<IReadOnlyList<CustomerMatch>> GetByItemIdAsync(Guid itemId, Guid orgId, double minScore = 0.7, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerMatch>> GetByCustomerIdAsync(Guid customerId, Guid orgId, CancellationToken cancellationToken = default);
    Task AddRangeAsync(IEnumerable<CustomerMatch> matches, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerMatchDto>> GetEnrichedMatchesByItemIdAsync(Guid itemId, Guid orgId, double minScore = 0.7, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<CustomerMatchDto>> GenerateMatchesForInventoryItemAsync(Guid itemId, Guid orgId, int maxMatches = 10, CancellationToken cancellationToken = default);
}
