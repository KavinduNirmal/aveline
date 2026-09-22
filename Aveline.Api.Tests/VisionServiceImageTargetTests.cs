using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// The target-classification contract of <see cref="VisionService.AnalyzeAsync"/>.
/// </summary>
/// <remarks>
/// <para>
/// The production defect these pin: the service forwarded whatever string the caller supplied into
/// the provider's <c>image_url.url</c> field. A <c>data:</c> URL and an absolute <c>http(s)</c> URL
/// work (the provider either reads the bytes inline or fetches the URL); everything else - most
/// importantly the <b>relative</b> Aveline media route
/// <c>/api/v1/orgs/{orgId}/catalog/images/{id}</c> that the catalog upload response stores in
/// <c>InventoryImages.ImageUrl</c> - makes the provider answer
/// <c>400 {"error":{"message":".messages[0]: Unsupported image_url format"}}</c>. The service then
/// silently answered from the deterministic fallback, which derives attributes from the file name
/// and hint text and never reads a pixel, while reporting a primed <c>ConfidenceScore = 0.95</c>.
/// </para>
/// <para>
/// The fix: classify the target before spending a provider call and before fabricating a result.
/// Inline bytes must be one of the four types the provider reads and must carry a payload; a
/// provider-fetchable target is an absolute <c>http</c>/<c>https</c> URI; anything else is a caller
/// error (<see cref="ArgumentException"/>), not a fallback. The deterministic fallback is
/// deliberately unchanged for the honest failures: no configured key, a non-success status, an
/// unparsable response, and a transport fault.
/// </para>
/// <para>
/// Every refusal case asserts <see cref="RecordingHttpMessageHandler.CallCount"/> is zero: the
/// whole point is that the provider is never asked and no fabricated analysis is ever returned.
/// </para>
/// </remarks>
public class VisionServiceImageTargetTests
{
    private const string ProviderJson = """
    {
        "choices": [
            {
                "message": {
                    "content": "{\"category\":\"saree\",\"primary_color\":\"emerald\",\"secondary_colors\":[\"gold\"],\"pattern\":\"zari\",\"style\":\"traditional\",\"fabric\":\"silk\",\"confidence_score\":0.93,\"suggested_keywords\":[\"saree\"]}"
                }
            }
        ],
        "usage": { "prompt_tokens": 10, "completion_tokens": 5 }
    }
    """;

    // =======================================================================================
    // Refused targets: a caller error, never a silent fallback
    // =======================================================================================

    [Fact]
    public async Task AnalyzeAsync_WithARelativePathTarget_ThrowsAndNeverCallsTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        // The literal production shape: the string stored in `InventoryImages.ImageUrl`.
        const string relative = "/api/v1/orgs/2c8e6f14-5f0a-4d1e-9a3b-6b0f0d2c9a11/catalog/images/4f0a1e2d-3c4b-5a69-8d7e-9f0a1b2c3d4e";
        var act = () => service.AnalyzeAsync(relative, Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.Message.Should().Contain(
            "absolute http(s) URL",
            "an operator reading the 400 must see that the call was wrong, not that the provider was down");
        handler.CallCount.Should().Be(0, "a target the provider cannot fetch is refused before any request");
    }

    [Fact]
    public async Task AnalyzeAsync_WithASchemeRelativeTarget_ThrowsAndNeverCallsTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var act = () => service.AnalyzeAsync("//cdn.example.com/saree.jpg", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_WithANonHttpAbsoluteTarget_ThrowsAndNeverCallsTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        // `Uri.TryCreate(..., UriKind.Absolute)` accepts this; the scheme check must still refuse it.
        var act = () => service.AnalyzeAsync("file:///tmp/saree.jpg", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_WithAnUnanalysableInlineType_ThrowsAndNeverCallsTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var act = () => service.AnalyzeAsync("data:image/tiff;base64,AAAA", Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.Message.Should().Contain("image/tiff", "the refusal names the media type it cannot analyse");
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_WithInlineDataCarryingNoPayload_ThrowsAndNeverCallsTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var act = () => service.AnalyzeAsync("data:image/jpeg;base64,", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_WithInlineDataMissingThePayloadSeparator_ThrowsAndNeverCallsTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var act = () => service.AnalyzeAsync("data:image/jpeg;base64", Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task AnalyzeAsync_WithAPrefixedInlinePayload_RefusesWithoutEchoingThePayload()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        // Not recognised as inline data (it does not start with `data:`), so it takes the
        // "everything else" refusal. The payload must still never be echoed into the error or a log.
        const string payload = "U0VDUkVUQkFTRTY0UEFZTE9BRA==";
        var act = () => service.AnalyzeAsync($"  data:image/jpeg;base64,{payload}", Guid.NewGuid());

        var exception = await act.Should().ThrowAsync<ArgumentException>();
        exception.Which.Message.Should().NotContain(
            payload,
            "an inline image payload must never be echoed into an error message or a log line");
        handler.CallCount.Should().Be(0);
    }

    // =======================================================================================
    // Accepted targets: the provider is reached
    // =======================================================================================

    [Fact]
    public async Task AnalyzeAsync_WithInlineJpegData_ReachesTheProviderAndReturnsItsParsedResult()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var result = await service.AnalyzeAsync(
            "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP",
            Guid.NewGuid());

        handler.CallCount.Should().Be(1, "inline bytes are the shape the app itself produces and must not be refused");
        result.IsFallback.Should().BeFalse("a provider answer was parsed, so this is not the deterministic fallback");
        result.Category.Should().Be("saree");
        result.PrimaryColor.Should().Be("emerald");
    }

    [Fact]
    public async Task AnalyzeAsync_WithInlineDataSchemeInUpperCase_StillReachesTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var result = await service.AnalyzeAsync(
            "DATA:IMAGE/JPEG;BASE64,/9j/4AAQSkZJRg==",
            Guid.NewGuid());

        handler.CallCount.Should().Be(1, "the `data:` and media-type match is case-insensitive");
        result.PrimaryColor.Should().Be("emerald");
    }

    [Fact]
    public async Task AnalyzeAsync_WithAnAbsoluteHttpsTarget_ReachesTheProvider()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var result = await service.AnalyzeAsync("https://example.com/saree.jpg", Guid.NewGuid());

        handler.CallCount.Should().Be(1);
        result.PrimaryColor.Should().Be("emerald");
    }

    // =======================================================================================
    // The refusal is a caller error even when no key is configured: it never fabricates
    // =======================================================================================

    [Fact]
    public async Task AnalyzeAsync_WithoutAConfiguredKeyAndARelativeTarget_RefusesRatherThanFabricating()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler, withKey: false);

        var act = () => service.AnalyzeAsync("/api/v1/orgs/x/catalog/images/y", Guid.NewGuid());

        // The no-key path is a documented fallback for an image we *could* have read. A target that
        // names no readable image is a caller error, so it must throw rather than return the
        // filename-derived guess the production operator saw presented as a model reading.
        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task AnalyzeAsync_WithABlankTarget_StillThrowsArgumentException(string target)
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler);

        var act = () => service.AnalyzeAsync(target, Guid.NewGuid());

        await act.Should().ThrowAsync<ArgumentException>();
        handler.CallCount.Should().Be(0);
    }

    // =======================================================================================
    // Provider-specific request fields: `thinking` is DeepSeek-only
    // =======================================================================================

    [Fact]
    public async Task AnalyzeAsync_WithTheDeepSeekProvider_SendsReasoningDisabled()
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler, baseUrl: "https://api.deepseek.com");

        await service.AnalyzeAsync("https://cdn.example.com/saree.jpg", Guid.NewGuid());

        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.TryGetProperty("thinking", out var thinking)
            .Should().BeTrue("DeepSeek spends the whole output budget on reasoning tokens unless told not to");
        thinking.GetProperty("type").GetString().Should().Be("disabled");
    }

    [Theory]
    [InlineData("https://api.openai.com")]
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai/")]
    public async Task AnalyzeAsync_WithANonDeepSeekProvider_OmitsTheThinkingField(string baseUrl)
    {
        var handler = new RecordingHttpMessageHandler(ProviderResponse());
        var service = BuildService(handler, baseUrl: baseUrl);

        await service.AnalyzeAsync("https://cdn.example.com/saree.jpg", Guid.NewGuid());

        // Measured against Gemini: `400 Invalid JSON payload received. Unknown name "thinking":
        // Cannot find field.` The field must be absent, not null, or the request is refused and the
        // caller silently receives the filename-derived deterministic fallback.
        using var body = JsonDocument.Parse(handler.LastRequestBody!);
        body.RootElement.TryGetProperty("thinking", out _)
            .Should().BeFalse("a provider that does not know the field rejects the whole request");
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private static VisionService BuildService(
        HttpMessageHandler handler,
        bool withKey = true,
        string baseUrl = "https://api.openai.com")
    {
        var settings = new Dictionary<string, string?>();
        if (withKey)
        {
            settings["Vision:ApiKey"] = "test-vision-key";
        }

        var config = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var client = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        return new VisionService(client, config, NullLogger<VisionService>.Instance);
    }

    private static HttpResponseMessage ProviderResponse() => new(HttpStatusCode.OK)
    {
        Content = new StringContent(ProviderJson, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        /// <summary>The serialised request body of the most recent call, for field-level assertions.</summary>
        public string? LastRequestBody { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            LastRequestBody = request.Content?.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult();
            return Task.FromResult(response);
        }
    }
}
