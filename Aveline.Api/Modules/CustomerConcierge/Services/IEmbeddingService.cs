namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Generates a text embedding vector for a memory (or query). Implementations must return a
/// 1536-dimensional vector to match the pgvector <c>vector(1536)</c> column (ADR-017).
/// Abstracted so tests can stub it and avoid real provider calls.
/// </summary>
public interface IEmbeddingService
{
    /// <summary>Embeds <paramref name="text"/> into a 1536-dimensional vector.</summary>
    Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default);
}
