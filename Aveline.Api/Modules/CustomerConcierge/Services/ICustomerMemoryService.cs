using Aveline.Api.Modules.CustomerConcierge.DTOs;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>Semantic memory persistence, search, correction, and interaction-brief generation.</summary>
public interface ICustomerMemoryService
{
    /// <summary>
    /// Saves a memory, embedding <see cref="SaveMemoryRequest.Content"/> when no embedding is
    /// supplied. Does nothing further if consent is revoked.
    /// </summary>
    /// <remarks>
    /// A restatement of a note the customer already has returns the existing row instead of
    /// creating a second one (gap A3). The result is therefore "the memory that now holds this
    /// statement", which is what both the create and the collapse case answer with.
    /// </remarks>
    Task<CustomerMemoryDto?> SaveMemoryAsync(
        Guid customerId,
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Rewrites the statement of one of a customer's memories and re-embeds it (gap A5).
    /// Returns null when the memory does not exist in the org+customer scope, or when the new
    /// statement would collapse onto another of the customer's notes.
    /// </summary>
    Task<CustomerMemoryDto?> CorrectMemoryAsync(
        Guid orgId,
        Guid customerId,
        Guid memoryId,
        CorrectMemoryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a stated preference in the customer's preferences table (gap C4).
    /// </summary>
    /// <remarks>
    /// Upserted on <c>(key, value)</c> within the customer: restating a preference the customer
    /// already has refreshes its confidence and timestamp instead of adding a duplicate row, while
    /// a genuinely new value is added beside the old one. Returns null when consent is revoked.
    /// </remarks>
    Task<CustomerPreferenceDto?> SavePreferenceAsync(
        Guid customerId,
        SavePreferenceRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Withdraws one memory by setting its soft-delete column (gaps A5, A6). Returns true only when
    /// a live memory in the org+customer scope was actually withdrawn.
    /// </summary>
    Task<bool> RemoveMemoryAsync(
        Guid orgId,
        Guid customerId,
        Guid memoryId,
        CancellationToken cancellationToken = default);

    /// <summary>A customer's live memories, newest first. Empty when none, or when consent is revoked.</summary>
    Task<IReadOnlyList<CustomerMemoryDto>> ListMemoriesAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Searches a customer's memories by free-text query using <see cref="MemorySearchRequest.Mode"/>
    /// (<c>hybrid</c> by default; the single-leg modes exist for retrieval evaluation). Returns an
    /// empty list when the customer's consent is revoked (or unreadable), and throws
    /// <see cref="ArgumentException"/> for a mode this store cannot run.
    /// </summary>
    Task<IReadOnlyList<MemorySearchResultDto>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Builds a concise staff-facing interaction brief for a customer.
    /// Returns null when the customer is unknown or their consent is revoked (or unreadable).</summary>
    Task<InteractionBriefDto?> GenerateBriefAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default);
}
