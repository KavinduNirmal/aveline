using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Semantic memory persistence, search, and interaction-brief generation.</summary>
public interface ICustomerMemoryService
{
    /// <summary>
    /// Saves a memory, embedding <see cref="SaveMemoryRequest.Content"/> when no embedding is
    /// supplied. Does nothing further if consent is revoked.
    /// </summary>
    Task<CustomerMemoryDto?> SaveMemoryAsync(
        Guid customerId,
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Semantically searches a customer's memories by free-text query.</summary>
    Task<IReadOnlyList<MemorySearchResultDto>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a concise staff-facing interaction brief for a customer.</summary>
    Task<InteractionBriefDto?> GenerateBriefAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
