using Aveline.Api.Modules.Handbook.DTOs;

namespace Aveline.Api.Modules.Handbook.Services;

/// <summary>
/// The handbook knowledge base: chunk ingestion, hybrid retrieval, and source management.
/// Consumed by the Python agent service through the internal endpoints (ADR-009) and by the
/// seeder CLI (ADR-025).
/// </summary>
public interface IHandbookService
{
    /// <summary>
    /// Inserts or updates one chunk at <c>(SourceKey, Ordinal)</c>, embedding its content. An
    /// unchanged content hash is a true no-op: nothing is written and no embedding is regenerated.
    /// </summary>
    Task<HandbookChunkDto> UpsertAsync(
        SaveHandbookChunkRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Runs the requested search mode over the index.</summary>
    Task<IReadOnlyList<HandbookSearchResultDto>> SearchAsync(
        HandbookSearchRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Removes every chunk for a source. Returns how many rows were deleted.</summary>
    Task<int> DeleteSourceAsync(string sourceKey, CancellationToken cancellationToken = default);

    /// <summary>Every indexed source with its chunk count.</summary>
    Task<IReadOnlyList<HandbookSourceSummaryDto>> ListSourcesAsync(
        CancellationToken cancellationToken = default);
}
