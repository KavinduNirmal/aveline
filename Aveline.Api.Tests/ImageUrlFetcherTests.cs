using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using Aveline.Api.Common.Media;
using Aveline.Api.Modules.Conversations.Media;
using Aveline.Api.Modules.Media;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit U4.2 (strategy §5.1 S6; salon plan §7.5). The pasted-image-URL fetcher is the only
/// genuinely new attack surface in the media workstream, so every one of the fourteen specified
/// behaviours has a test here, and the security-relevant transport configuration
/// (<c>AllowAutoRedirect</c>, cookies, the connect timeout, address pinning) is exercised against
/// a real Kestrel socket rather than only against a double.
/// </summary>
/// <remarks>
/// <para>
/// Offline and deterministic (ADR-020): the only network is a loopback Kestrel server that this
/// class starts itself, and DNS is replaced by <see cref="FakeResolver"/> — which is also the
/// seam that lets the pipeline drive a real HTTP round trip while the address policy sees a
/// public address. The real DNS resolver and the real address policy are exercised by the
/// dedicated tests below.
/// </para>
/// <para>
/// The fetcher under test is the production <see cref="ImageUrlFetcher"/> with the production
/// handler factory (<see cref="ImageUrlPinningHandler"/>) for the transport tests; only the
/// resolver is a double in the pipeline tests, because resolving a public hostname is the one
/// thing a hermetic suite cannot do.
/// </para>
/// </remarks>
public sealed class ImageUrlFetcherTests
{
    /// <summary>A real, public IPv4 address (example.com). Never contacted: the pipeline tests
    /// substitute a loopback-capable transport or an in-memory handler.</summary>
    private static readonly IPAddress PublicAddress = IPAddress.Parse("93.184.216.34");

    private const string PublicHostUrl = "https://example.com/images/photo.jpg";

    private static readonly byte[] JpegBytes =
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0xFF, 0xD9];

    private static readonly byte[] PngBytes =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private static readonly byte[] HtmlBytes = Encoding.ASCII.GetBytes("<html><body>x</body></html>");

    // =======================================================================================
    // 1. Parse strictly
    // =======================================================================================

    [Theory]
    [InlineData("images/photo.jpg")]
    [InlineData("example.com/images/photo.jpg")]
    [InlineData("not a url")]
    [InlineData("")]
    public async Task FetchAsync_WithARelativeValue_IsRefusedAsUnparseable(string value)
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(value, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.Unparseable);
    }

    [Fact]
    public async Task FetchAsync_WithANullValue_IsRefusedAsUnparseable()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(null, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.Unparseable);
    }

    // =======================================================================================
    // 2. Scheme allow-list
    // =======================================================================================

    [Theory]
    [InlineData("ftp://example.com/photo.jpg")]
    [InlineData("file:///etc/passwd")]
    [InlineData("ws://example.com/photo.jpg")]
    [InlineData("gopher://example.com/photo.jpg")]
    [InlineData("data:image/png;base64,iVBORw0KGgo=")]
    public async Task FetchAsync_WithADisallowedScheme_IsRefused(string value)
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(value, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.SchemeNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithHttp_WhenInsecureFetchIsDisabled_IsRefused()
    {
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            options: BuildOptions(insecure: false));

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("http://example.com/photo.jpg", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.InsecureSchemeNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithHttp_WhenInsecureFetchIsEnabled_IsAccepted()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()), options: BuildOptions(insecure: true));

        var result = await fetcher.FetchAsync("http://example.com/photo.jpg", CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task FetchAsync_WithHttps_IsAccepted()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var result = await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
        result.Bytes.Should().Equal(JpegBytes);
    }

    // =======================================================================================
    // 3. No credentials, no odd ports
    // =======================================================================================

    [Theory]
    [InlineData("https://user:secret@example.com/photo.jpg")]
    [InlineData("https://user@example.com/photo.jpg")]
    public async Task FetchAsync_WithUserInfo_IsRefused(string value)
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(value, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.CredentialsNotAllowed);
    }

    [Theory]
    [InlineData("https://example.com:8443/photo.jpg")]
    [InlineData("https://example.com:80/photo.jpg")]
    [InlineData("https://example.com:22/photo.jpg")]
    public async Task FetchAsync_WithANonDefaultPort_IsRefused(string value)
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(value, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.PortNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithANonDefaultPort_OnInsecureHttp_IsRefused()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()), options: BuildOptions(insecure: true));

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("http://example.com:8080/photo.jpg", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.PortNotAllowed);
    }

    [Theory]
    [InlineData("https://example.com/photo.jpg")]
    [InlineData("https://example.com:443/photo.jpg")]
    public async Task FetchAsync_WithTheSchemeDefaultPort_IsAccepted(string value)
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()));

        var result = await fetcher.FetchAsync(value, CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    // =======================================================================================
    // 4. Optional host allow-list (the Webhook:AllowedIps convention)
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WithAnEmptyAllowlist_AdmitsAnyPublicHost()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()), options: BuildOptions());

        var result = await fetcher.FetchAsync("https://anything.example.org/photo.jpg", CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Theory]
    [InlineData("https://cdn.example.com/photo.jpg")]
    [InlineData("https://images.example.org/photo.jpg")]
    [InlineData("https://CDN.EXAMPLE.COM/photo.jpg")]
    public async Task FetchAsync_WithAnAllowlist_AdmitsAListedHost(string value)
    {
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            options: BuildOptions(allowlist: "cdn.example.com, images.example.org"));

        var result = await fetcher.FetchAsync(value, CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Theory]
    [InlineData("https://evil.example.com/photo.jpg")]
    [InlineData("https://cdn.example.com.evil.example/photo.jpg")]
    [InlineData("https://candidate.example.com/photo.jpg")]
    public async Task FetchAsync_WithAnAllowlist_RefusesAnUnlistedHost(string value)
    {
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            options: BuildOptions(allowlist: "cdn.example.com, images.example.org"));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(value, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.HostNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithAMalformedAllowlistEntry_FailsClosedBeforeResolving()
    {
        var resolver = new FakeResolver();
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            resolver,
            BuildOptions(allowlist: "https://cdn.example.com"));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.AllowlistMalformed);
        resolver.ResolvedHosts.Should().BeEmpty();
    }

    // =======================================================================================
    // 5. Resolve the host ourselves and reject non-public addresses
    // =======================================================================================

    [Theory]
    // Loopback (RFC1122) and unspecified.
    [InlineData("127.0.0.1")]
    [InlineData("127.255.255.254")]
    [InlineData("0.0.0.0")]
    // RFC1918 private.
    [InlineData("10.0.0.1")]
    [InlineData("172.16.0.1")]
    [InlineData("172.31.255.255")]
    [InlineData("192.168.1.1")]
    // Link-local, including the cloud metadata endpoint.
    [InlineData("169.254.169.254")]
    [InlineData("169.254.0.1")]
    // CGNAT (RFC6598).
    [InlineData("100.64.0.1")]
    // Reserved, benchmarking and documentation ranges.
    [InlineData("192.0.0.1")]
    [InlineData("192.0.2.1")]
    [InlineData("192.88.99.1")] // 6to4 relay anycast (RFC7526)
    [InlineData("198.18.0.1")]
    [InlineData("198.51.100.1")]
    [InlineData("203.0.113.1")]
    // Multicast and broadcast/reserved.
    [InlineData("224.0.0.1")]
    [InlineData("239.255.255.250")]
    [InlineData("240.0.0.1")]
    [InlineData("255.255.255.255")]
    // IPv6 loopback, link-local, unique-local, multicast, unspecified, Teredo, documentation.
    [InlineData("::1")]
    [InlineData("fe80::1")]
    [InlineData("fec0::1")]
    [InlineData("fc00::1")]
    [InlineData("fd12:3456::1")]
    [InlineData("ff02::1")]
    [InlineData("::")]
    [InlineData("100::1")] // discard-only prefix (RFC6666)
    [InlineData("5f00::1")] // SRv6 SIDs (RFC9602)
    [InlineData("::ffff:127.0.0.1")]
    [InlineData("::ffff:10.0.0.1")]
    [InlineData("2001::1")]
    [InlineData("2001:db8::1")]
    [InlineData("2002:7f00:1::")]
    [InlineData("64:ff9b::7f00:1")]
    [InlineData("3fff::1")]
    public async Task FetchAsync_WithANonPublicResolvedAddress_IsRefused(string address)
    {
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(IPAddress.Parse(address)));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.NonPublicAddress);
    }

    [Theory]
    [InlineData("93.184.216.34")]
    [InlineData("8.8.8.8")]
    [InlineData("1.1.1.1")]
    [InlineData("2606:4700:4700::1111")]
    public async Task FetchAsync_WithAPublicResolvedAddress_IsAccepted(string address)
    {
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(IPAddress.Parse(address)));

        var result = await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task FetchAsync_WhenAnyResolvedAddressIsNonPublic_IsRefused()
    {
        // A rebinding-style answer: one public address, one private. Any non-public answer
        // poisons the whole resolution (salon §7.5 item 5).
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(PublicAddress, IPAddress.Parse("10.0.0.7")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.NonPublicAddress);
    }

    [Fact]
    public async Task FetchAsync_ToTheCloudMetadataAddress_IsRefused()
    {
        // 169.254.169.254 is the one address an SSRF probe is really looking for.
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(IPAddress.Parse("169.254.169.254")));

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://169.254.169.254/latest/meta-data/", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.NonPublicAddress);
    }

    [Fact]
    public async Task FetchAsync_WhenTheHostResolvesToNothing_IsRefused()
    {
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(Array.Empty<IPAddress>()));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.UnresolvableHost);
    }

    // =======================================================================================
    // 6. Pin the connection to the validated IP
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_PinsTheConnectionToTheValidatedAddress()
    {
        var handler = new StubHandler(_ => OkImage());
        var fetcher = CreateFetcher(handler, new FakeResolver(PublicAddress));

        await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        handler.Requests.Should().ContainSingle();
        handler.Requests[0].Options.TryGetValue(ImageUrlPinningHandler.PinnedAddressKey, out var pinned)
            .Should().BeTrue();
        pinned.Should().Be(PublicAddress);
    }

    [Fact]
    public async Task FetchAsync_WhenAnyResolvedAddressIsNonPublic_SetsNoPin()
    {
        var handler = new StubHandler(_ => OkImage());
        var fetcher = CreateFetcher(
            handler,
            new FakeResolver(PublicAddress, IPAddress.Parse("192.168.0.9")));

        await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        handler.Requests.Should().BeEmpty();
    }

    // =======================================================================================
    // 7. Redirects: manual, capped, re-validated on every hop
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WithASingleRedirect_IsFollowed()
    {
        var fetcher = CreateFetcher(Redirecting(("/a", "/b")));

        var result = await fetcher.FetchAsync("https://example.com/a", CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task FetchAsync_FollowsAtMostTwoRedirects()
    {
        var fetcher = CreateFetcher(Redirecting(("/1", "/2"), ("/2", "/3")), options: BuildOptions(maxRedirects: 2));

        var result = await fetcher.FetchAsync("https://example.com/1", CancellationToken.None);

        result.ContentType.Should().Be("image/jpeg");
    }

    [Fact]
    public async Task FetchAsync_RefusesAThirdRedirect()
    {
        var fetcher = CreateFetcher(
            Redirecting(("/1", "/2"), ("/2", "/3"), ("/3", "/4")),
            options: BuildOptions(maxRedirects: 2));

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://example.com/1", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.TooManyRedirects);
    }

    [Fact]
    public async Task FetchAsync_WithMaxRedirectsZero_RefusesTheFirstRedirect()
    {
        var fetcher = CreateFetcher(Redirecting(("/a", "/b")), options: BuildOptions(maxRedirects: 0));

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://example.com/a", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.TooManyRedirects);
    }

    [Fact]
    public async Task FetchAsync_RefusesARedirectToANonPublicAddress()
    {
        // The first hop is public and accepted; the Location then names a host that resolves
        // to a private address, and the re-validation on hop two must refuse it.
        var resolver = new FakeResolver(host =>
            host == "metadata.internal" ? [IPAddress.Parse("169.254.169.254")] : [PublicAddress]);
        var handler = new StubHandler(request =>
            request.RequestUri!.AbsolutePath == "/a"
                ? RedirectTo(request, "https://metadata.internal/latest/meta-data/")
                : OkImage());
        var fetcher = CreateFetcher(handler, resolver);

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://example.com/a", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.NonPublicAddress);
        resolver.ResolvedHosts.Should().Contain("metadata.internal");
    }

    [Fact]
    public async Task FetchAsync_RefusesARedirectToANonPublicAddressByIpLiteral()
    {
        var fetcher = CreateFetcher(
            request => request.RequestUri!.AbsolutePath == "/a"
                ? RedirectTo(request, "https://169.254.169.254/latest/meta-data/")
                : OkImage());

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://example.com/a", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.NonPublicAddress);
    }

    [Fact]
    public async Task FetchAsync_RefusesARedirectToADifferentScheme()
    {
        var fetcher = CreateFetcher(request =>
            request.RequestUri!.AbsolutePath == "/a"
                ? RedirectTo(request, "ftp://example.com/photo.jpg")
                : OkImage());

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://example.com/a", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.SchemeNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_RefusesARedirectToAnAllowlistMiss()
    {
        var fetcher = CreateFetcher(
            request => request.RequestUri!.AbsolutePath == "/a"
                ? RedirectTo(request, "https://evil.example/photo.jpg")
                : OkImage(),
            options: BuildOptions(allowlist: "cdn.example.com"));

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://cdn.example.com/a", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.HostNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithARedirectStatusAndNoLocation_IsRefused()
    {
        var fetcher = CreateFetcher(request =>
            request.RequestUri!.AbsolutePath == "/a"
                ? new HttpResponseMessage(HttpStatusCode.Found)
                : OkImage());

        var refusal = await RefusedAsync(
            () => fetcher.FetchAsync("https://example.com/a", CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.InvalidRedirect);
    }

    [Fact]
    public async Task FetchAsync_WithANonSuccessStatus_IsRefused()
    {
        var fetcher = CreateFetcher(_ => new HttpResponseMessage(HttpStatusCode.NotFound));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.FetchFailed);
    }

    // =======================================================================================
    // 8. Timeout budget
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WhenThePeerDoesNotSendHeadersWithinTheBudget_IsRefusedAsATimeout()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return OkImage();
        });
        var fetcher = CreateFetcher(handler, options: BuildOptions(timeoutSeconds: 1));

        var stopwatch = Stopwatch.StartNew();
        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));
        stopwatch.Stop();

        refusal.Reason.Should().Be(ImageUrlFetchReasons.Timeout);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task FetchAsync_WhenTheBodyStalls_IsRefusedAsATimeout()
    {
        // A slow-loris body: the headers arrive, the bytes do not. The same total budget must
        // cover the body read, or a peer can hold the request open forever.
        var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StallingImageContent(),
        });
        var fetcher = CreateFetcher(handler, options: BuildOptions(timeoutSeconds: 1));

        var stopwatch = Stopwatch.StartNew();
        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));
        stopwatch.Stop();

        refusal.Reason.Should().Be(ImageUrlFetchReasons.Timeout);
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task FetchAsync_WhenTheCallerCancels_PropagatesTheCancellation()
    {
        var handler = new StubHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(30), ct);
            return OkImage();
        });
        var fetcher = CreateFetcher(handler, options: BuildOptions(timeoutSeconds: 30));
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

        var act = async () => await fetcher.FetchAsync(PublicHostUrl, cancellation.Token);

        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    // =======================================================================================
    // 9. Enforce the size cap while streaming
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WithAnOverCapBody_IsRefusedAndReadsAtMostTheCapPlusOne()
    {
        var stream = new CountingImageStream(MediaContentTypes.MaxFileBytes * 2);
        var fetcher = CreateFetcher(StreamingHandler(stream));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.TooLarge);
        stream.BytesRead.Should().Be(MediaContentTypes.MaxFileBytes + 1);
    }

    [Fact]
    public async Task FetchAsync_WithABodyOfExactlyTheCap_IsAccepted()
    {
        var stream = new CountingImageStream(MediaContentTypes.MaxFileBytes);
        var fetcher = CreateFetcher(StreamingHandler(stream));

        var result = await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        stream.BytesRead.Should().Be(MediaContentTypes.MaxFileBytes);
        result.Bytes.LongLength.Should().Be(MediaContentTypes.MaxFileBytes);
    }

    [Fact]
    public async Task FetchAsync_WithADeclaredOverCapLength_IsRefusedBeforeReadingTheBody()
    {
        var stream = new CountingImageStream(64);
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Headers.ContentLength = MediaContentTypes.MaxFileBytes + 1;
        var fetcher = CreateFetcher(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        }));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.TooLarge);
        stream.BytesRead.Should().Be(0);
    }

    // =======================================================================================
    // 10. Require an allow-listed content type
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WithANonImageContentType_IsRefused()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage(HtmlBytes, "text/html")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.ContentTypeNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithNoContentType_IsRefused()
    {
        var content = new ByteArrayContent(JpegBytes);
        content.Headers.ContentType = null;
        var fetcher = CreateFetcher(new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = content,
        }));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.ContentTypeNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithAPdfContentType_IsRefused()
    {
        // The fetcher is the image path; a PDF is storable but is not a pasted image. This case
        // covers the *declared* header only; the sniff arm is the next test.
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage("%PDF-1.7\n"u8.ToArray(), "application/pdf")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.ContentTypeNotAllowed);
    }

    [Fact]
    public async Task FetchAsync_WithPdfBytesBehindAnImageContentType_IsRefusedByTheSniff()
    {
        // F4a: the sniff guard that a PDF body is refused on the *fetch* path. The declared
        // header says `image/jpeg`, so the declared-type gate admits the body; the sniff sees the
        // `%PDF-` bytes and returns `application/pdf`, which `IsImage` then refuses. The reason is
        // `ContentSignatureMismatch`, not `ContentTypeNotAllowed`, which is what distinguishes this
        // arm from the declared-`application/pdf` refusal above.
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage("%PDF-1.7\n"u8.ToArray(), "image/jpeg")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.ContentSignatureMismatch);
    }

    // =======================================================================================
    // 11. Sniff the magic bytes
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WithAnHtmlBodyBehindAnImageContentType_IsRefused()
    {
        // The payload-smuggling case: arbitrary bytes behind an image content type.
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage(HtmlBytes, "image/jpeg")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.ContentSignatureMismatch);
    }

    [Fact]
    public async Task FetchAsync_WithAScriptBodyBehindAnImageContentType_IsRefused()
    {
        var script = Encoding.ASCII.GetBytes("<script>alert(1)</script>");
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage(script, "image/png")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.ContentSignatureMismatch);
    }

    [Fact]
    public async Task FetchAsync_WithARealImage_ReturnsTheSniffedContentType()
    {
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage(PngBytes, "image/png")));

        var result = await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        result.ContentType.Should().Be("image/png");
        result.Bytes.Should().Equal(PngBytes);
    }

    [Fact]
    public async Task FetchAsync_WithPngBytesBehindAJpegContentType_ReturnsTheSniffedType()
    {
        // The bytes are the truth; the declared header only has to be on the allow-list.
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage(PngBytes, "image/jpeg")));

        var result = await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        result.ContentType.Should().Be("image/png");
    }

    // =======================================================================================
    // F5: the fetched result itself carries the "this is an image" invariant
    // =======================================================================================

    [Fact]
    public void FetchedImage_RefusesANonImageContentType()
    {
        // F5 hardening: the guard used to be a separate statement in the fetcher, so a future
        // caller could hold a FetchedImage whose type was a non-image and store it by forgetting
        // its own `IsImage` check. The type now refuses construction outright.
        foreach (var contentType in new[] { "application/pdf", "text/html", "application/octet-stream" })
        {
            Assert.Throws<ArgumentException>(() => new FetchedImage(PngBytes, contentType));
        }

        Assert.Throws<ArgumentNullException>(() => new FetchedImage(PngBytes, null!));
        Assert.Throws<ArgumentException>(() => new FetchedImage(PngBytes, "  "));

        // The real image path is unaffected, and the sniffed type is preserved verbatim.
        var image = new FetchedImage(PngBytes, "image/png");
        image.ContentType.Should().Be("image/png");
        image.Bytes.Should().Equal(PngBytes);
    }

    // =======================================================================================
    // 12. No cookies, no ambient credentials
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_SendsNoAuthorizationOrCookieHeader()
    {
        var handler = new StubHandler(_ => OkImage());
        var fetcher = CreateFetcher(handler);

        await fetcher.FetchAsync(PublicHostUrl, CancellationToken.None);

        var request = handler.Requests.Single();
        request.Headers.Authorization.Should().BeNull();
        request.Headers.Contains("Cookie").Should().BeFalse();
        request.Headers.Contains("Proxy-Authorization").Should().BeFalse();
    }

    // =======================================================================================
    // 13. Never log the full URL
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_NeverLogsTheFullUrl_OnTheSuccessPath()
    {
        const string secret = "SUPERSECRETVALUE";
        var url = $"https://example.com/private/path/photo.jpg?token={secret}";
        var logger = new CapturingLogger<ImageUrlFetcher>();
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()), logger: logger);

        await fetcher.FetchAsync(url, CancellationToken.None);

        AssertNoUrlLeak(logger, url, secret);
        logger.Messages.Should().Contain(message => message.Contains("example.com"));
        logger.Messages.Should().Contain(message => message.Contains("urlHash="));
    }

    [Fact]
    public async Task FetchAsync_NeverLogsTheFullUrl_OnTheFailurePath()
    {
        const string secret = "SUPERSECRETVALUE";
        var url = $"https://example.com/private/path/photo.jpg?token={secret}";
        var logger = new CapturingLogger<ImageUrlFetcher>();
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(IPAddress.Parse("10.0.0.1")),
            logger: logger);

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(url, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.NonPublicAddress);
        AssertNoUrlLeak(logger, url, secret);
        logger.Levels.Should().Contain(LogLevel.Warning);
        logger.Messages.Should().Contain(message => message.Contains("example.com"));
    }

    [Fact]
    public async Task FetchAsync_WhenTheTransportThrows_NeverLogsTheFullUrl()
    {
        const string secret = "SUPERSECRETVALUE";
        var url = $"https://example.com/private/path/photo.jpg?token={secret}";
        var logger = new CapturingLogger<ImageUrlFetcher>();
        var fetcher = CreateFetcher(
            new StubHandler((_, _) => throw new HttpRequestException($"boom {url}")),
            logger: logger);

        await RefusedAsync(() => fetcher.FetchAsync(url, CancellationToken.None));

        AssertNoUrlLeak(logger, url, secret);
    }

    [Fact]
    public async Task FetchAsync_OnTheFailurePath_TheExceptionMessageCarriesNoUrl()
    {
        const string secret = "SUPERSECRETVALUE";
        var url = $"https://example.com/private/path/photo.jpg?token={secret}";
        var fetcher = CreateFetcher(
            new StubHandler(_ => OkImage()),
            new FakeResolver(IPAddress.Parse("169.254.169.254")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(url, CancellationToken.None));

        refusal.Message.Should().NotContain(secret);
        refusal.Message.Should().NotContain("example.com");
        refusal.Message.Should().NotContain("photo.jpg");
        refusal.Message.Should().NotContain("token=");
    }

    // =======================================================================================
    // 14. Fail closed, and never fail the send
    // =======================================================================================

    [Fact]
    public async Task FetchAsync_WhenTheFeatureIsDisabled_FailsClosedWithoutResolvingOrConnecting()
    {
        var resolver = new FakeResolver();
        var handler = new StubHandler(_ => OkImage());
        var logger = new CapturingLogger<ImageUrlFetcher>();
        var fetcher = CreateFetcher(handler, resolver, BuildOptions(enabled: false), logger);

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.Disabled);
        resolver.ResolvedHosts.Should().BeEmpty();
        handler.Requests.Should().BeEmpty();
        logger.Levels.Should().Contain(LogLevel.Warning);
    }

    [Fact]
    public async Task FetchAsync_WhenTheTransportThrows_WrapsItAsATypedRefusal()
    {
        var fetcher = CreateFetcher(
            new StubHandler((_, _) => throw new HttpRequestException("connection reset")));

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.FetchFailed);
        refusal.Should().BeOfType<ImageUrlFetchException>();
    }

    [Fact]
    public async Task FetchAsync_WhenTheTransportThrowsUnexpectedly_StillFailsClosed()
    {
        var logger = new CapturingLogger<ImageUrlFetcher>();
        var fetcher = CreateFetcher(
            new StubHandler((_, _) => throw new InvalidOperationException("handler exploded")),
            logger: logger);

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(PublicHostUrl, CancellationToken.None));

        refusal.Reason.Should().Be(ImageUrlFetchReasons.FetchFailed);
        refusal.Message.Should().NotContain("handler exploded");
        logger.Levels.Should().Contain(LogLevel.Warning);
    }

    [Theory]
    [InlineData("ftp://example.com/photo.jpg")]
    [InlineData("https://user:pass@example.com/photo.jpg")]
    [InlineData("https://example.com:8443/photo.jpg")]
    public async Task FetchAsync_EveryRefusalIsATypedExceptionLoggedAtWarning(string url)
    {
        var logger = new CapturingLogger<ImageUrlFetcher>();
        var fetcher = CreateFetcher(new StubHandler(_ => OkImage()), logger: logger);

        var refusal = await RefusedAsync(() => fetcher.FetchAsync(url, CancellationToken.None));

        refusal.Should().BeOfType<ImageUrlFetchException>();
        refusal.Reason.Should().NotBeNullOrWhiteSpace();
        logger.Levels.Should().Contain(LogLevel.Warning);
    }

    // =======================================================================================
    // The shipped transport configuration, exercised over a real socket
    // =======================================================================================

    [Fact]
    public void TheShippedHandler_DisablesAutoRedirectAndCookies_AndBoundsTheConnectPhase()
    {
        using var handler = ImageUrlPinningHandler.Create(TimeSpan.FromSeconds(7));

        handler.AllowAutoRedirect.Should().BeFalse();
        handler.UseCookies.Should().BeFalse();
        handler.ConnectTimeout.Should().Be(TimeSpan.FromSeconds(7));
        handler.ConnectCallback.Should().NotBeNull();
    }

    [Fact]
    public async Task TheShippedHandler_ConnectsToThePinnedAddressAndKeepsTheOriginalHostHeader()
    {
        // The server only exists on loopback; the request URI names a host that does not
        // resolve. Reaching the server proves the connect used the pinned address, and the
        // echoed Host proves the original authority was preserved (salon §7.5 item 6).
        await using var server = new PinnedImageServer();
        await server.StartAsync();

        using var handler = ImageUrlPinningHandler.Create(TimeSpan.FromSeconds(5));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://pinned-image.test:{server.Port}/echo");
        request.Options.Set(ImageUrlPinningHandler.PinnedAddressKey, IPAddress.Loopback);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        server.LastHostHeader.Should().Be($"pinned-image.test:{server.Port}");
    }

    [Fact]
    public async Task TheShippedHandler_RefusesToConnectWhenNoAddressWasPinned()
    {
        using var handler = ImageUrlPinningHandler.Create(TimeSpan.FromSeconds(2));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, "http://127.0.0.1:9/photo.jpg");

        var act = async () => await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        var exception = await act.Should().ThrowAsync<HttpRequestException>();
        exception.WithInnerException<InvalidOperationException>();
    }

    [Fact]
    public async Task TheShippedHandler_DoesNotFollowARedirectAutomatically()
    {
        await using var server = new PinnedImageServer();
        await server.StartAsync();

        using var handler = ImageUrlPinningHandler.Create(TimeSpan.FromSeconds(5));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://pinned-image.test:{server.Port}/redirect");
        request.Options.Set(ImageUrlPinningHandler.PinnedAddressKey, IPAddress.Loopback);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);

        response.StatusCode.Should().Be(HttpStatusCode.Found);
        response.Headers.Location!.ToString().Should().Be("/image.jpg");
    }

    [Fact]
    public async Task TheShippedHandler_DoesNotStoreOrResendAResponseCookie()
    {
        await using var server = new PinnedImageServer();
        await server.StartAsync();

        using var handler = ImageUrlPinningHandler.Create(TimeSpan.FromSeconds(5));
        using var client = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };

        await SendPinnedAsync(client, server, "/set-cookie");
        await SendPinnedAsync(client, server, "/echo");

        server.LastSetCookieResponse.Should().Contain("session=abc");
        server.LastCookieHeader.Should().BeNullOrEmpty();
        server.LastAuthorizationHeader.Should().BeNullOrEmpty();
    }

    // =======================================================================================
    // Registration
    // =======================================================================================

    [Fact]
    public void AddImageUrlFetcher_RegistersTheFetcherWithNoAmbientAuthorization()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMediaOptions(configuration);
        services.AddImageUrlFetcher();

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IImageUrlFetcher>().Should().BeOfType<ImageUrlFetcher>();

        var client = provider.GetRequiredService<IHttpClientFactory>()
            .CreateClient(typeof(IImageUrlFetcher).FullName!);
        client.DefaultRequestHeaders.Authorization.Should().BeNull();
        client.DefaultRequestHeaders.Contains("Cookie").Should().BeFalse();
    }

    // =======================================================================================
    // The production address policy, unit-tested directly
    // =======================================================================================

    [Theory]
    [InlineData("10.0.0.1", false)]
    [InlineData("169.254.169.254", false)]
    [InlineData("::1", false)]
    [InlineData("::ffff:169.254.169.254", false)]
    [InlineData("93.184.216.34", true)]
    [InlineData("2606:4700:4700::1111", true)]
    public void ImageUrlAddressPolicy_ClassifiesAddressesAsExpected(string address, bool isPublic)
    {
        ImageUrlAddressPolicy.IsPublic(IPAddress.Parse(address)).Should().Be(isPublic);
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private static ImageUrlFetcher CreateFetcher(
        HttpMessageHandler handler,
        IImageUrlHostResolver? resolver = null,
        MediaOptions? options = null,
        CapturingLogger<ImageUrlFetcher>? logger = null)
        => new(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            Microsoft.Extensions.Options.Options.Create(options ?? BuildOptions()),
            resolver ?? new FakeResolver(),
            (ILogger<ImageUrlFetcher>?)logger ?? NullLogger<ImageUrlFetcher>.Instance);

    private static ImageUrlFetcher CreateFetcher(
        Func<HttpRequestMessage, HttpResponseMessage> respond,
        IImageUrlHostResolver? resolver = null,
        MediaOptions? options = null,
        CapturingLogger<ImageUrlFetcher>? logger = null)
        => CreateFetcher(new StubHandler(respond), resolver, options, logger);

    private static MediaOptions BuildOptions(
        bool enabled = true,
        bool insecure = true,
        string allowlist = "",
        int maxRedirects = 2,
        int timeoutSeconds = 5)
        => new()
        {
            ImageUrlUploadEnabled = enabled,
            AllowInsecureImageFetch = insecure,
            ImageUrlAllowlist = allowlist,
            ImageUrlMaxRedirects = maxRedirects,
            ImageUrlFetchTimeoutSeconds = timeoutSeconds,
        };

    private static async Task<ImageUrlFetchException> RefusedAsync(Func<Task> act)
        => await Assert.ThrowsAsync<ImageUrlFetchException>(act);

    private static HttpResponseMessage OkImage(byte[]? body = null, string? contentType = "image/jpeg")
    {
        var content = new ByteArrayContent(body ?? JpegBytes);
        if (contentType is not null)
        {
            content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        }

        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    private static HttpResponseMessage RedirectTo(HttpRequestMessage request, string location)
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri(request.RequestUri!, location);
        return response;
    }

    private static StubHandler Redirecting(params (string From, string To)[] hops)
    {
        var map = hops.ToDictionary(hop => hop.From, hop => hop.To, StringComparer.Ordinal);
        return new StubHandler(request =>
        {
            var path = request.RequestUri!.AbsolutePath;
            return map.TryGetValue(path, out var to) ? RedirectTo(request, to) : OkImage();
        });
    }

    private static StubHandler StreamingHandler(Stream stream)
    {
        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
    }

    private static void AssertNoUrlLeak(CapturingLogger<ImageUrlFetcher> logger, string url, string secret)
    {
        var joined = string.Join('\n', logger.Messages);
        joined.Should().NotContain(url);
        joined.Should().NotContain(secret);
        joined.Should().NotContain("/private/path");
        joined.Should().NotContain("token=");
    }

    private static async Task SendPinnedAsync(HttpClient client, PinnedImageServer server, string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"http://pinned-image.test:{server.Port}{path}");
        request.Options.Set(ImageUrlPinningHandler.PinnedAddressKey, IPAddress.Loopback);
        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        await response.Content.ReadAsByteArrayAsync();
    }

    /// <summary>Deterministic resolver double. It is the one seam that replaces real DNS.</summary>
    private sealed class FakeResolver : IImageUrlHostResolver
    {
        private readonly Func<string, IReadOnlyList<IPAddress>> _map;

        /// <summary>
        /// The default double resolves an IP literal to itself (as real DNS does) and every
        /// other host to a public address.
        /// </summary>
        public FakeResolver()
            : this(host => IPAddress.TryParse(host, out var literal) ? [literal] : [PublicAddress]) { }

        public FakeResolver(params IPAddress[] addresses)
            : this(_ => addresses) { }

        public FakeResolver(Func<string, IReadOnlyList<IPAddress>> map) => _map = map;

        public List<string> ResolvedHosts { get; } = [];

        public Task<IReadOnlyList<IPAddress>> ResolveAsync(string host, CancellationToken cancellationToken)
        {
            ResolvedHosts.Add(host);
            return Task.FromResult(_map(host));
        }
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _respond;

        public StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            : this((request, _) => Task.FromResult(respond(request))) { }

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
            => _respond = respond;

        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request);
            return await _respond(request, cancellationToken);
        }
    }

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public List<LogLevel> Levels { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            Levels.Add(logLevel);
            var message = formatter(state, exception);
            Messages.Add(exception is null ? message : $"{message} | {exception}");
        }
    }

    /// <summary>A never-seekable byte source that counts how much of it was actually read.</summary>
    private sealed class CountingImageStream : Stream
    {
        private readonly long _length;
        private long _position;

        public CountingImageStream(long length) => _length = length;

        public long BytesRead { get; private set; }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => _position;
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
            => ReadCore(buffer.AsSpan(offset, count));

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => ValueTask.FromResult(ReadCore(buffer.Span));

        public override Task<int> ReadAsync(
            byte[] buffer, int offset, int count, CancellationToken cancellationToken)
            => Task.FromResult(ReadCore(buffer.AsSpan(offset, count)));

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

        private int ReadCore(Span<byte> destination)
        {
            if (_position >= _length || destination.Length == 0)
            {
                return 0;
            }

            var toReturn = (int)Math.Min(destination.Length, _length - _position);
            for (var index = 0; index < toReturn; index++)
            {
                var absolute = _position + index;
                destination[index] = absolute switch
                {
                    0 => 0xFF,
                    1 => 0xD8,
                    2 => 0xFF,
                    _ => 0x41,
                };
            }

            _position += toReturn;
            BytesRead += toReturn;
            return toReturn;
        }
    }

    /// <summary>Sends the JPEG header, then stalls the body: the slow-loris case.</summary>
    private sealed class StallingImageContent : HttpContent
    {
        public StallingImageContent() => Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context)
            => throw new NotSupportedException();

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task<Stream> CreateContentReadStreamAsync()
            => Task.FromResult<Stream>(new StallingStream());

        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken cancellationToken)
            => Task.FromResult<Stream>(new StallingStream());
    }

    private sealed class StallingStream : Stream
    {
        private int _reads;

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => 0;
            set => throw new NotSupportedException();
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_reads++ == 0)
            {
                JpegBytes.AsSpan().CopyTo(buffer.Span);
                return JpegBytes.Length;
            }

            await Task.Delay(TimeSpan.FromSeconds(30), cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_reads++ == 0)
            {
                JpegBytes.CopyTo(buffer, offset);
                return JpegBytes.Length;
            }

            return 0;
        }

        public override void Flush() { }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>A real Kestrel server on loopback, so the pinned transport is proven on a socket.</summary>
    private sealed class PinnedImageServer : IAsyncDisposable
    {
        private readonly WebApplication _app;

        public PinnedImageServer()
        {
            Port = FreePort();
            var builder = WebApplication.CreateBuilder();
            builder.Logging.ClearProviders();
            builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, Port));
            _app = builder.Build();

            _app.MapGet("/image.jpg", (HttpContext context) =>
            {
                context.Response.ContentType = "image/jpeg";
                return context.Response.Body.WriteAsync(JpegBytes).AsTask();
            });

            _app.MapGet("/redirect", (HttpContext context) =>
            {
                context.Response.StatusCode = StatusCodes.Status302Found;
                context.Response.Headers.Location = "/image.jpg";
                return Task.CompletedTask;
            });

            _app.MapGet("/set-cookie", (HttpContext context) =>
            {
                context.Response.Headers.Append("Set-Cookie", "session=abc; Path=/");
                LastSetCookieResponse = context.Response.Headers.SetCookie.ToString();
                context.Response.ContentType = "image/jpeg";
                return context.Response.Body.WriteAsync(JpegBytes).AsTask();
            });

            _app.MapGet("/echo", (HttpContext context) =>
            {
                LastHostHeader = context.Request.Host.Value;
                LastCookieHeader = context.Request.Headers.Cookie.ToString();
                LastAuthorizationHeader = context.Request.Headers.Authorization.ToString();
                context.Response.ContentType = "image/jpeg";
                return context.Response.Body.WriteAsync(JpegBytes).AsTask();
            });
        }

        public int Port { get; }

        public string? LastHostHeader { get; private set; }

        public string? LastCookieHeader { get; private set; }

        public string? LastAuthorizationHeader { get; private set; }

        public string? LastSetCookieResponse { get; private set; }

        public Task StartAsync() => _app.StartAsync();

        public async ValueTask DisposeAsync()
        {
            await _app.StopAsync();
            await _app.DisposeAsync();
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }
    }
}
