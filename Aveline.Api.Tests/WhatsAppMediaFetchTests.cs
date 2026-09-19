using System.Net;
using System.Text;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// The inbound media fetch (T10): Meta's two-step resolve-then-download, with the bearer on
/// both hops.
/// </summary>
public class WhatsAppMediaFetchTests
{
    private sealed class TwoStepHandler : HttpMessageHandler
    {
        private readonly byte[] _bytes;
        private readonly bool _failResolve;
        private readonly bool _failDownload;

        public TwoStepHandler(byte[] bytes, bool failResolve = false, bool failDownload = false)
        {
            _bytes = bytes;
            _failResolve = failResolve;
            _failDownload = failDownload;
        }

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);

            if (Requests.Count == 1)
            {
                if (_failResolve)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized)
                    {
                        Content = new StringContent("""{"error":{"message":"expired"}}""", Encoding.UTF8, "application/json"),
                    });
                }

                // Step 1: the media id resolves to a short-lived CDN URL, with the mime type.
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"id":"media-1","url":"https://lookaside.example.test/media-1","mime_type":"image/png","file_size":4,"sha256":"abc"}""",
                        Encoding.UTF8,
                        "application/json"),
                });
            }

            if (_failDownload)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
            }

            var download = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(_bytes),
            };
            download.Content.Headers.ContentType = new("image/png");
            return Task.FromResult(download);
        }
    }

    private static WhatsAppService Service(HttpMessageHandler handler)
        => new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.example.test/v1/") },
            NullLogger<WhatsAppService>.Instance);

    [Fact]
    public async Task GetMediaAsync_ResolvesThenDownloadsWithTheBearerOnBothHops()
    {
        var handler = new TwoStepHandler([1, 2, 3, 4]);

        var result = await Service(handler).GetMediaAsync("token-1", "media-1");

        Assert.True(result.IsSuccess);
        Assert.Equal([1, 2, 3, 4], result.Bytes);
        Assert.Equal("image/png", result.ContentType);
        Assert.Equal(4, result.SizeBytes);
        Assert.Equal(2, handler.Requests.Count);
        // The second hop is Meta's absolute URL, still carrying the tenant's bearer: the URL is
        // not public.
        Assert.Equal("https://lookaside.example.test/media-1", handler.Requests[1].RequestUri!.ToString());
        Assert.All(handler.Requests, request =>
            Assert.Equal("Bearer", request.Headers.Authorization!.Scheme));
    }

    [Fact]
    public async Task GetMediaAsync_ReportsAFailedResolveWithoutDownloading()
    {
        var handler = new TwoStepHandler([1], failResolve: true);

        var result = await Service(handler).GetMediaAsync("token-1", "media-1");

        Assert.False(result.IsSuccess);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task GetMediaAsync_ReportsAFailedDownload()
    {
        var handler = new TwoStepHandler([1], failDownload: true);

        var result = await Service(handler).GetMediaAsync("token-1", "media-1");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Bytes);
    }
}
