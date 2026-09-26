using System.Net;
using Aveline.Api.Common.Media;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.3 (strategy §5.1 S4, salon §7.4) — the inbound media download is streaming and
/// size-capped. It used to call <c>ReadAsByteArrayAsync</c> with no bound, so a hostile or
/// oversized inbound media object was unbounded memory.
/// </summary>
/// <remarks>
/// An over-cap body is refused cleanly (<see cref="WhatsAppMediaResult.IsSuccess"/> is false and
/// no bytes are returned), never truncated: half an image silently stored is worse than a
/// recorded skip. The cap is the attachment tier's own bound
/// (<see cref="MediaContentTypes.MaxFileBytes"/>), so the fetch and the store agree on "too big".
/// </remarks>
public class WhatsAppMediaCapTests
{
    private const long Cap = MediaContentTypes.MaxFileBytes;

    [Fact]
    public async Task GetMediaAsync_AtTheCap_IsAccepted()
    {
        var handler = new MediaHandler(new byte[Cap]);

        var result = await Service(handler).GetMediaAsync("token", "media-at-cap");

        Assert.True(result.IsSuccess);
        Assert.Equal(Cap, result.Bytes!.LongLength);
        Assert.Equal(Cap, result.SizeBytes);
    }

    [Fact]
    public async Task GetMediaAsync_OneByteOverTheCap_IsRefusedCleanly()
    {
        var handler = new MediaHandler(new byte[Cap + 1]);

        var result = await Service(handler).GetMediaAsync("token", "media-over-cap");

        Assert.False(result.IsSuccess);
        Assert.Null(result.Bytes);
        Assert.Contains("limit", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMediaAsync_DeclaredOverCapLength_IsRefusedWithoutReadingTheBody()
    {
        var content = new LazyContent(Cap + 1);
        var handler = new MediaHandler(content);

        var result = await Service(handler).GetMediaAsync("token", "media-declared-over-cap");

        Assert.False(result.IsSuccess);
        Assert.Equal(0, content.Served);
    }

    [Fact]
    public async Task GetMediaAsync_OverCapBodyWithoutADeclaredLength_StopsReadingAtTheCap()
    {
        // No `Content-Length`, so only a streaming read can bound this. A buffering reader
        // consumes the whole 7 MB; the capped reader stops just past the 5 MB cap.
        var content = new LazyContent(Cap + (2 * 1024 * 1024), declareLength: false);
        var handler = new MediaHandler(content);

        var result = await Service(handler).GetMediaAsync("token", "media-streaming-cap");

        Assert.False(result.IsSuccess);
        Assert.True(
            content.Served <= Cap + (64 * 1024),
            $"the capped read consumed {content.Served} bytes, which is not bounded by the cap");
    }

    private static WhatsAppService Service(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { BaseAddress = new Uri("https://graph.example.test/v1/") },
            NullLogger<WhatsAppService>.Instance);

    /// <summary>Meta's two hops: the resolve, then the download the test controls.</summary>
    private sealed class MediaHandler : HttpMessageHandler
    {
        private readonly HttpContent _download;
        private int _calls;

        public MediaHandler(byte[] bytes)
            : this(new ByteArrayContent(bytes), contentType: "image/png")
        {
        }

        public MediaHandler(HttpContent download, string? contentType = null)
        {
            _download = download;
            if (contentType is not null)
            {
                _download.Headers.ContentType = new(contentType);
            }
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            _calls++;
            if (_calls == 1)
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        """{"id":"media-1","url":"https://lookaside.example.test/media-1","mime_type":"image/png"}""",
                        System.Text.Encoding.UTF8,
                        "application/json"),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = _download });
        }
    }

    /// <summary>
    /// A body of a known size that counts how many bytes a reader actually consumed, and whose
    /// length can be withheld so only a streaming read can bound it.
    /// </summary>
    private sealed class LazyContent : HttpContent
    {
        private readonly long _length;
        private readonly bool _declareLength;

        public LazyContent(long length, bool declareLength = true)
        {
            _length = length;
            _declareLength = declareLength;
            Headers.ContentType = new("image/png");
        }

        public long Served { get; private set; }

        protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
        {
            // A buffering reader (`ReadAsByteArrayAsync`) comes through here and therefore
            // consumes the whole body; the capped streaming reader goes through
            // `CreateContentReadStreamAsync` and stops early. That difference is what the
            // two over-cap tests measure.
            var source = new CountingStream(this, _length);
            await source.CopyToAsync(stream);
        }

        protected override bool TryComputeLength(out long length)
        {
            length = _declareLength ? _length : 0;
            return _declareLength;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new CountingStream(this, _length));

        private sealed class CountingStream(LazyContent owner, long length) : Stream
        {
            private long _served;

            public override bool CanRead => true;

            public override bool CanSeek => false;

            public override bool CanWrite => false;

            public override long Length => throw new NotSupportedException();

            public override long Position
            {
                get => throw new NotSupportedException();
                set => throw new NotSupportedException();
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                var remaining = length - _served;
                if (remaining <= 0)
                {
                    return 0;
                }

                var read = (int)Math.Min(count, remaining);
                _served += read;
                owner.Served = _served;
                return read;
            }

            public override async ValueTask<int> ReadAsync(
                Memory<byte> buffer, CancellationToken cancellationToken = default)
            {
                await Task.Yield();
                var remaining = length - _served;
                if (remaining <= 0)
                {
                    return 0;
                }

                var read = (int)Math.Min(buffer.Length, remaining);
                buffer.Span[..read].Clear();
                _served += read;
                owner.Served = _served;
                return read;
            }

            public override void Flush()
            {
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

            public override void SetLength(long value) => throw new NotSupportedException();

            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
