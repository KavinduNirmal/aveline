using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Aveline.Api.Modules.CustomerConcierge.Services;

namespace Aveline.Api.Tests;

/// <summary>
/// Tests for the <see cref="EmbeddingService"/> provider branches. The OpenAI-compatible branch
/// is used against a non-Gemini base URL; the Gemini branch is selected when the base URL host is
/// <c>generativelanguage.googleapis.com</c>. Each uses a stub handler so no real provider is hit.
/// </summary>
public class EmbeddingServiceTests
{
    private sealed class StubHandler : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        public string? LastBody { get; private set; }
        public string ResponseJson { get; set; } = "{}";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            LastRequest = request;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(ResponseJson, Encoding.UTF8, "application/json"),
            };
        }
    }

    private static IConfiguration Config(string baseUrl, string key = "test-key", string model = "text-embedding-3-small")
        => new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Embeddings:ApiKey"] = key,
                ["Embeddings:BaseUrl"] = baseUrl,
                ["Embeddings:Model"] = model,
            })
            .Build();

    private static HttpClient ClientWith(StubHandler handler, string baseUrl)
        => new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };

    [Fact]
    public async Task OpenAI_Branch_PostsToV1Embeddings_WithBearerToken()
    {
        var handler = new StubHandler
        {
            ResponseJson = """{"data":[{"embedding":[0.1,0.2,0.3]}]}""",
        };
        var service = new EmbeddingService(
            ClientWith(handler, "https://api.openai.com"),
            Config("https://api.openai.com", model: "text-embedding-3-small"));

        var vector = await service.GenerateAsync("hello");

        Assert.Equal(new[] { 0.1f, 0.2f, 0.3f }, vector);
        Assert.Equal("/v1/embeddings", handler.LastRequest!.RequestUri!.PathAndQuery);
        Assert.Equal("Bearer test-key", handler.LastRequest.Headers.Authorization!.ToString());

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastBody!);
        Assert.Equal("text-embedding-3-small", body.GetProperty("model").GetString());
        Assert.Equal("hello", body.GetProperty("input").GetString());
    }

    [Fact]
    public async Task Gemini_Branch_PostsToEmbedContent_WithApiKeyHeader_And1536Output()
    {
        var handler = new StubHandler
        {
            ResponseJson = """{"embedding":{"values":[0.9,0.8,0.7]}}""",
        };
        var service = new EmbeddingService(
            ClientWith(handler, "https://generativelanguage.googleapis.com"),
            Config("https://generativelanguage.googleapis.com", model: "gemini-embedding-2"));

        var vector = await service.GenerateAsync("hello");

        Assert.Equal(new[] { 0.9f, 0.8f, 0.7f }, vector);
        Assert.Equal(
            "/v1beta/models/gemini-embedding-2:embedContent",
            handler.LastRequest!.RequestUri!.PathAndQuery);
        Assert.Equal("test-key", handler.LastRequest.Headers.GetValues("X-Goog-Api-Key").Single());

        var body = JsonSerializer.Deserialize<JsonElement>(handler.LastBody!);
        Assert.Equal("models/gemini-embedding-2", body.GetProperty("model").GetString());
        Assert.Equal("hello", body.GetProperty("content").GetProperty("parts")[0].GetProperty("text").GetString());
        Assert.Equal(1536, body.GetProperty("outputDimensionality").GetInt32());
    }

    [Fact]
    public async Task Gemini_Branch_AcceptsModelsPrefixedModelName()
    {
        var handler = new StubHandler
        {
            ResponseJson = """{"embedding":{"values":[1.0]}}""",
        };
        var service = new EmbeddingService(
            ClientWith(handler, "https://generativelanguage.googleapis.com"),
            Config("https://generativelanguage.googleapis.com", model: "models/gemini-embedding-2"));

        await service.GenerateAsync("hello");

        Assert.Equal(
            "/v1beta/models/gemini-embedding-2:embedContent",
            handler.LastRequest!.RequestUri!.PathAndQuery);
    }

    [Fact]
    public async Task EmptyResponse_Throws()
    {
        var handler = new StubHandler { ResponseJson = "{}" };
        var service = new EmbeddingService(
            ClientWith(handler, "https://api.openai.com"),
            Config("https://api.openai.com"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.GenerateAsync("hello"));
    }

    [Fact]
    public void MissingApiKey_Throws()
    {
        var handler = new StubHandler();
        var config = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?> { ["Embeddings:BaseUrl"] = "https://api.openai.com" }).Build();

        Assert.Throws<InvalidOperationException>(() => new EmbeddingService(ClientWith(handler, "https://api.openai.com"), config));
    }
}
