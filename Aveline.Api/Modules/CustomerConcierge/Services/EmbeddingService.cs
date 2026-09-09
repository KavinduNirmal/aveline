using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

/// <summary>
/// OpenAI-compatible embeddings client (e.g. text-embedding-3-small). Reads its configuration
/// from the <c>Embeddings</c> section: <c>BaseUrl</c>, <c>ApiKey</c>, <c>Model</c>. Throws at
/// call time when not configured so tests and unconfigured dev environments never make a call.
/// </summary>
public class EmbeddingService : IEmbeddingService
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _model;

    public EmbeddingService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _apiKey = configuration["Embeddings:ApiKey"] ?? throw new InvalidOperationException("Embeddings:ApiKey is not configured.");
        _model = configuration["Embeddings:Model"] ?? "text-embedding-3-small";
    }

    public async Task<float[]> GenerateAsync(string text, CancellationToken cancellationToken = default)
    {
        var payload = new { model = _model, input = text };
        using var request = new HttpRequestMessage(HttpMethod.Post, "v1/embeddings")
        {
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

        var response = await _httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var body = JsonSerializer.Deserialize<EmbeddingsResponse>(await response.Content.ReadAsStringAsync(cancellationToken));
        if (body?.Data is null || body.Data.Count == 0 || body.Data[0].Embedding is null)
        {
            throw new InvalidOperationException("Embedding provider returned an empty response.");
        }

        return body.Data[0].Embedding;
    }

    private sealed class EmbeddingsResponse
    {
        [JsonPropertyName("data")]
        public List<EmbeddingDatum>? Data { get; set; }
    }

    private sealed class EmbeddingDatum
    {
        [JsonPropertyName("embedding")]
        public float[]? Embedding { get; set; }
    }
}
