using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// Text-embedding client that supports two providers over the <c>Embeddings</c> config section
/// (<c>BaseUrl</c>, <c>ApiKey</c>, <c>Model</c>):
///
/// <list type="bullet">
///   <item><b>OpenAI-compatible</b> (default, e.g. <c>text-embedding-3-small</c>) - POSTs to
///     <c>v1/embeddings</c> with an OpenAI body and a Bearer token.</item>
///   <item><b>Gemini</b> - selected automatically when the configured base URL host is
///     <c>generativelanguage.googleapis.com</c>. POSTs to
///     <c>v1beta/models/&#123;model&#125;:embedContent</c> with Gemini's native body and the
///     <c>X-Goog-Api-Key</c> header.</item>
/// </list>
///
/// Throws at construction when <c>Embeddings:ApiKey</c> is absent, so unconfigured dev
/// environments and tests never make a real provider call. Vectors are coerced to 1536
/// dimensions to match the pgvector <c>vector(1536)</c> column (ADR-017): Gemini requests set
/// <c>outputDimensionality</c> to 1536.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private const int ExpectedDimensions = 1536;
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;
    private readonly bool _isGemini;

    public EmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Embeddings:ApiKey"] ?? throw new InvalidOperationException("Embeddings:ApiKey is not configured.");
        _model = configuration["Embeddings:Model"] ?? "text-embedding-3-small";
        var host = _httpClient.BaseAddress?.Host ?? "api.openai.com";
        _isGemini = host.Contains("generativelanguage", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
        => _isGemini
            ? await GenerateGeminiAsync(text, cancellationToken)
            : await GenerateOpenAiAsync(text, cancellationToken);

    private async Task<float[]> GenerateOpenAiAsync(string text, CancellationToken cancellationToken)
    {
        var payload = new { model = _model, input = text };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<OpenAiEmbeddingsResponse>(await response.Content.ReadAsStringAsync(cancellationToken));
        if (body?.Data is null || body.Data.Count == 0 || body.Data[0].Embedding is null)
        {
            throw new InvalidOperationException("Embedding provider returned an empty response.");
        }

        return body.Data[0].Embedding;
    }

    private async Task<float[]> GenerateGeminiAsync(string text, CancellationToken cancellationToken)
    {
        // The model may be configured as "gemini-embedding-2" or "models/gemini-embedding-2".
        var bareModel = _model.StartsWith("models/", StringComparison.OrdinalIgnoreCase)
            ? _model["models/".Length..]
            : _model;

        var payload = new
        {
            model = $"models/{bareModel}",
            content = new { parts = new[] { new { text } } },
            // Keep vectors at 1536 dimensions to match the pgvector column (ADR-017).
            outputDimensionality = ExpectedDimensions,
        };
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"v1beta/models/{bareModel}:embedContent")
        {
            Content = JsonContent.Create(payload),
        };
        // Gemini uses an API-key header rather than an Authorization: Bearer token.
        request.Headers.TryAddWithoutValidation("X-Goog-Api-Key", _apiKey);

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<GeminiEmbeddingsResponse>(await response.Content.ReadAsStringAsync(cancellationToken));
        if (body?.Embedding?.Values is not { Length: > 0 })
        {
            throw new InvalidOperationException("Embedding provider returned an empty response.");
        }

        return body.Embedding.Values;
    }

    private sealed class OpenAiEmbeddingsResponse
    {
        [JsonPropertyName("data")]
        public List<OpenAiEmbeddingDatum>? Data { get; set; }
    }

    private sealed class OpenAiEmbeddingDatum
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }

    private sealed class GeminiEmbeddingsResponse
    {
        [JsonPropertyName("embedding")]
        public GeminiEmbedding? Embedding { get; set; }
    }

    private sealed class GeminiEmbedding
    {
        [JsonPropertyName("values")]
        public float[]? Values { get; set; }
    }
}
