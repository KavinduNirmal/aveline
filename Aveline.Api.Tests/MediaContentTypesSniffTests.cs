using Aveline.Api.Common.Media;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit U0.2: the magic-byte sniff and the vision-analysable subset (strategy §3.6).
/// </summary>
/// <remarks>
/// Named <c>MediaContentTypesSniffTests</c> rather than <c>MediaContentTypesTests</c> because a
/// class of that name already lives in <c>ConversationAttachmentTests.cs</c>, which lane L3 owns.
/// The existing class is not edited, moved or renamed; these cases are additive.
/// </remarks>
public class MediaContentTypesSniffTests
{
    // ---------------------------------------------------------------------------------------
    // Byte fixtures. Each one is the smallest leading run that carries the format's signature.
    // ---------------------------------------------------------------------------------------

    private static readonly byte[] JpegBytes =
    [
        0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01,
    ];

    private static readonly byte[] PngBytes =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
    ];

    private static readonly byte[] Gif87aBytes = "GIF87a"u8.ToArray();

    private static readonly byte[] Gif89aBytes = "GIF89a"u8.ToArray();

    private static readonly byte[] WebpBytes =
    [
        .. "RIFF"u8.ToArray(),
        0x1A, 0x00, 0x00, 0x00,
        .. "WEBPVP8 "u8.ToArray(),
    ];

    private static readonly byte[] BmpBytes =
    [
        0x42, 0x4D, 0x36, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00,
    ];

    private static readonly byte[] TiffLittleEndianBytes =
    [
        0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00,
    ];

    private static readonly byte[] TiffBigEndianBytes =
    [
        0x4D, 0x4D, 0x00, 0x2A, 0x00, 0x00, 0x00, 0x08,
    ];

    /// <summary><c>ftyp</c> box with an <c>avif</c> major brand.</summary>
    private static readonly byte[] AvifBytes =
    [
        0x00, 0x00, 0x00, 0x20,
        .. "ftyp"u8.ToArray(),
        .. "avif"u8.ToArray(),
        0x00, 0x00, 0x00, 0x00,
        .. "avif"u8.ToArray(),
        .. "mif1"u8.ToArray(),
    ];

    /// <summary><c>ftyp</c> box with a <c>heic</c> major brand.</summary>
    private static readonly byte[] HeicBytes =
    [
        0x00, 0x00, 0x00, 0x18,
        .. "ftyp"u8.ToArray(),
        .. "heic"u8.ToArray(),
        0x00, 0x00, 0x00, 0x00,
        .. "heic"u8.ToArray(),
        .. "mif1"u8.ToArray(),
    ];

    /// <summary><c>ftyp</c> box with a <c>heix</c> brand and only a compatible <c>heic</c>.</summary>
    private static readonly byte[] HeixBytes =
    [
        0x00, 0x00, 0x00, 0x18,
        .. "ftyp"u8.ToArray(),
        .. "heix"u8.ToArray(),
        0x00, 0x00, 0x00, 0x00,
        .. "heic"u8.ToArray(),
    ];

    private static readonly byte[] HtmlBytes =
        "<!DOCTYPE html><html><body><h1>not an image</h1></body></html>"u8.ToArray();

    private static readonly byte[] PdfBytes = "%PDF-1.7\n1 0 obj\n"u8.ToArray();

    // ---------------------------------------------------------------------------------------
    // The live gap the sniff closes: a JPEG-claimed file whose bytes are HTML.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sniff_RefusesAnHtmlBodyClaimedAsJpeg()
    {
        // The gap, demonstrated rather than asserted in prose: `Resolve` still rescues the
        // declared `image/jpeg`, because it never looks at the bytes. That behaviour is left
        // alone for callers that do not opt in - it is the sniff that closes the gap.
        Assert.Equal("image/jpeg", MediaContentTypes.Resolve("image/jpeg", "photo.jpg"));

        Assert.Null(MediaContentTypes.Sniff(HtmlBytes));
        Assert.False(MediaContentTypes.IsRecognisableImage(HtmlBytes));
    }

    [Fact]
    public void Sniff_RefusesABodyThatMistakesItsOwnExtension()
    {
        // A `.png` claimed over a non-image body: the extension-based rescue passes it today,
        // and the sniff rejects it.
        Assert.Equal("image/png", MediaContentTypes.Resolve("application/octet-stream", "photo.png"));
        Assert.Null(MediaContentTypes.Sniff(HtmlBytes));
    }

    // ---------------------------------------------------------------------------------------
    // The four provider formats pass the sniff, and are analysable.
    // ---------------------------------------------------------------------------------------

    public static TheoryData<string, byte[]> ProviderFormats => new()
    {
        { "image/jpeg", JpegBytes },
        { "image/png", PngBytes },
        { "image/gif", Gif89aBytes },
        { "image/webp", WebpBytes },
    };

    [Theory]
    [MemberData(nameof(ProviderFormats))]
    public void Sniff_RecognisesTheProviderFormats(string expected, byte[] bytes)
    {
        Assert.Equal(expected, MediaContentTypes.Sniff(bytes));
        Assert.True(MediaContentTypes.IsRecognisableImage(bytes));
    }

    [Theory]
    [MemberData(nameof(ProviderFormats))]
    public void TheProviderFormatsAreAnalysable(string contentType, byte[] bytes)
    {
        var sniffed = MediaContentTypes.Sniff(bytes);

        Assert.Equal(contentType, sniffed);
        Assert.True(VisionContentTypes.IsAnalysable(sniffed));
    }

    [Fact]
    public void Sniff_RecognisesBothGifSignatures()
    {
        Assert.Equal("image/gif", MediaContentTypes.Sniff(Gif87aBytes));
        Assert.Equal("image/gif", MediaContentTypes.Sniff(Gif89aBytes));
    }

    // ---------------------------------------------------------------------------------------
    // Storable, but not analysable: the provider cannot read these.
    // ---------------------------------------------------------------------------------------

    public static TheoryData<string, byte[]> UnanalysableImageFormats => new()
    {
        { "image/avif", AvifBytes },
        { "image/bmp", BmpBytes },
        { "image/tiff", TiffLittleEndianBytes },
        { "image/heic", HeicBytes },
    };

    [Theory]
    [MemberData(nameof(UnanalysableImageFormats))]
    public void UnanalysableFormats_AreSniffedStorableAndNotAnalysable(string expected, byte[] bytes)
    {
        var sniffed = MediaContentTypes.Sniff(bytes);

        Assert.Equal(expected, sniffed);
        Assert.True(MediaContentTypes.IsImage(sniffed));
        Assert.True(MediaContentTypes.IsAllowed(sniffed));
        Assert.False(VisionContentTypes.IsAnalysable(sniffed));
    }

    [Fact]
    public void Sniff_RecognisesBothTiffByteOrders()
    {
        Assert.Equal("image/tiff", MediaContentTypes.Sniff(TiffLittleEndianBytes));
        Assert.Equal("image/tiff", MediaContentTypes.Sniff(TiffBigEndianBytes));
    }

    [Theory]
    [InlineData("heic")]
    [InlineData("heix")]
    public void Sniff_RecognisesTheHeifFamilyBrands(string brand)
    {
        var bytes = new byte[24];
        bytes[3] = 0x18;
        "ftyp"u8.CopyTo(bytes.AsSpan(4));
        System.Text.Encoding.ASCII.GetBytes(brand).CopyTo(bytes.AsSpan(8));

        Assert.Equal("image/heic", MediaContentTypes.Sniff(bytes));
    }

    [Fact]
    public void Sniff_RecognisesHeifAsStorableAndNotAnalysable()
    {
        // heif is the ninth storable image type and is subject to the same subset rule.
        Assert.True(MediaContentTypes.IsImage("image/heif"));
        Assert.False(VisionContentTypes.IsAnalysable("image/heif"));
    }

    // ---------------------------------------------------------------------------------------
    // Non-images, and inputs that are not images at all.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sniff_RefusesPdfAndNonImageBodies()
    {
        Assert.Null(MediaContentTypes.Sniff(PdfBytes));
        Assert.False(MediaContentTypes.IsRecognisableImage(PdfBytes));
        Assert.False(VisionContentTypes.IsAnalysable("application/pdf"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("G")]
    [InlineData("not an image at all")]
    public void Sniff_RefusesShortOrUnknownBodies(string body)
    {
        var bytes = System.Text.Encoding.ASCII.GetBytes(body);

        Assert.Null(MediaContentTypes.Sniff(bytes));
        Assert.False(MediaContentTypes.IsRecognisableImage(bytes));
    }

    [Fact]
    public void Sniff_RefusesNullAndEmptyInput()
    {
        Assert.Null(MediaContentTypes.Sniff(null));
        Assert.False(MediaContentTypes.IsRecognisableImage(null));
        Assert.Null(MediaContentTypes.Sniff([]));
        Assert.False(MediaContentTypes.IsRecognisableImage([]));
    }

    [Fact]
    public void Sniff_RefusesARiffThatIsNotWebp()
    {
        // A WAV file leads with RIFF too; only the WEBP fourcc makes it an image.
        var wav = new List<byte>();
        wav.AddRange("RIFF"u8.ToArray());
        wav.AddRange(new byte[] { 0x1A, 0x00, 0x00, 0x00 });
        wav.AddRange("WAVEfmt "u8.ToArray());

        Assert.Null(MediaContentTypes.Sniff([.. wav]));
    }

    [Fact]
    public void Sniff_RefusesAnFtypThatIsNotAnImage()
    {
        // A video `ftyp` (mp42) is not an image, even though the box parses.
        var mp4 = new List<byte> { 0x00, 0x00, 0x00, 0x18 };
        mp4.AddRange("ftyp"u8.ToArray());
        mp4.AddRange("mp42"u8.ToArray());
        mp4.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x00 });

        Assert.Null(MediaContentTypes.Sniff([.. mp4]));
    }

    [Fact]
    public void Sniff_RefusesATruncatedFtypBox()
    {
        // `ftyp` with no brand bytes: a valid marker prefix is not enough.
        var truncated = new List<byte> { 0x00, 0x00, 0x00, 0x18 };
        truncated.AddRange("ftyp"u8.ToArray());

        Assert.Null(MediaContentTypes.Sniff([.. truncated]));
    }

    [Fact]
    public void Sniff_TreatsTheIsoBrandAsCaseSensitive()
    {
        // ISO base-media-file brands are case-sensitive four-character codes, so an uppercase
        // `AVIF` is a brand this sniffer does not know - and it must not guess. Refusing an
        // unknown brand is the safe answer; it is not the same as a false negative for the
        // lowercase `avif` the format actually defines.
        var bytes = new byte[24];
        bytes[3] = 0x18;
        "ftyp"u8.CopyTo(bytes.AsSpan(4));
        "AVIF"u8.CopyTo(bytes.AsSpan(8));

        Assert.Null(MediaContentTypes.Sniff(bytes));
    }

    // ---------------------------------------------------------------------------------------
    // Purity: the sniff reads only the bytes it is given.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sniff_IsPureAndIgnoresTheDeclaredType()
    {
        // The same bytes always sniff to the same answer, regardless of any header - the provider
        // detects format from the bytes, so we do too.
        Assert.Equal(MediaContentTypes.Sniff(PngBytes), MediaContentTypes.Sniff(PngBytes));
        Assert.Equal("image/png", MediaContentTypes.Sniff(PngBytes));

        // A body claimed as `image/heic` but carrying JPEG bytes sniffs as JPEG.
        Assert.Equal("image/jpeg", MediaContentTypes.Sniff(JpegBytes));
    }

    // ---------------------------------------------------------------------------------------
    // The subset is a STRICT subset of MediaContentTypes.Images - asserted as a set relation
    // against the real collection, never against a copied list literal.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void EveryAnalysableTypeIsAStorableImageType()
    {
        var imageTypes = MediaContentTypes.ImageTypes;

        Assert.NotEmpty(imageTypes);
        foreach (var contentType in VisionContentTypes.AnalysableTypes)
        {
            Assert.Contains(contentType, imageTypes);
            Assert.True(MediaContentTypes.IsImage(contentType));
        }
    }

    [Fact]
    public void TheStorableImageSetIsTheNineDocumentedTypes()
    {
        // A guard, not the subset assertion: it pins the storage allow-list the strategy §3.6
        // cites, so widening or narrowing it is a deliberate act that shows up as a failure
        // here rather than silently changing what the subset relation means.
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "image/jpeg", "image/png", "image/webp", "image/gif", "image/avif",
            "image/bmp", "image/tiff", "image/heic", "image/heif",
        };

        Assert.Equal(9, MediaContentTypes.ImageTypes.Count);
        Assert.True(expected.SetEquals(MediaContentTypes.ImageTypes));
    }

    [Fact]
    public void TheAnalysableSetIsAStrictSubsetOfTheStorableImageSet()
    {
        var imageTypes = MediaContentTypes.ImageTypes;
        var analysable = VisionContentTypes.AnalysableTypes;

        // Every analysable type is an image...
        Assert.True(analysable.IsSubsetOf(imageTypes), "every analysable type must be storable");

        // ...and the subset is strict: the image allow-list holds at least one type the vision
        // provider cannot read. Asserted as a relation, so adding a storable format does not
        // silently make the two sets equal.
        Assert.NotEmpty(imageTypes.Except(analysable));

        // Neither collection may be empty, which would make the relation vacuously true.
        Assert.NotEmpty(analysable);
    }

    [Fact]
    public void TheAnalysableSetIsExactlyTheFourProviderFormats()
    {
        // The other direction of the strict-subset relation: the provider's four formats are all
        // present, so the subset cannot shrink silently either.
        Assert.Equal(4, VisionContentTypes.AnalysableTypes.Count);
        Assert.Contains("image/jpeg", VisionContentTypes.AnalysableTypes);
        Assert.Contains("image/png", VisionContentTypes.AnalysableTypes);
        Assert.Contains("image/gif", VisionContentTypes.AnalysableTypes);
        Assert.Contains("image/webp", VisionContentTypes.AnalysableTypes);
    }

    [Theory]
    [InlineData("image/avif")]
    [InlineData("image/bmp")]
    [InlineData("image/tiff")]
    [InlineData("image/heic")]
    [InlineData("image/heif")]
    public void UnanalysableTypesAreStorableButNotAnalysable(string contentType)
    {
        Assert.True(MediaContentTypes.IsAllowed(contentType));
        Assert.True(MediaContentTypes.IsImage(contentType));
        Assert.False(VisionContentTypes.IsAnalysable(contentType));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("application/octet-stream")]
    [InlineData("application/pdf")]
    [InlineData("video/mp4")]
    [InlineData("text/html")]
    [InlineData("not/a-type")]
    public void IsAnalysable_RefusesAnythingOutsideTheSubset(string? contentType)
    {
        Assert.False(VisionContentTypes.IsAnalysable(contentType));
    }

    [Fact]
    public void IsAnalysable_ToleratesParametersAndCasing()
    {
        // The rest of MediaContentTypes normalises parameters and casing; the subset does too.
        Assert.True(VisionContentTypes.IsAnalysable("IMAGE/PNG"));
        Assert.True(VisionContentTypes.IsAnalysable("image/jpeg; charset=binary"));

        // A parameter-only match for a type outside the subset still refuses.
        Assert.False(VisionContentTypes.IsAnalysable("image/heic; charset=binary"));
    }

    [Fact]
    public void EverySniffedTypeIsAStorableImageType()
    {
        // The sniff and the allow-list cannot drift: anything the sniff names is storable.
        var samples = new[]
        {
            JpegBytes, PngBytes, Gif89aBytes, WebpBytes,
            BmpBytes, TiffLittleEndianBytes, AvifBytes, HeicBytes,
        };

        foreach (var bytes in samples)
        {
            var sniffed = MediaContentTypes.Sniff(bytes);
            Assert.NotNull(sniffed);
            Assert.True(MediaContentTypes.IsImage(sniffed), $"{sniffed} must be an allow-listed image");
        }
    }

    [Fact]
    public void Sniff_DoesNotMutateTheInput()
    {
        var bytes = (byte[])JpegBytes.Clone();
        var before = (byte[])bytes.Clone();

        _ = MediaContentTypes.Sniff(bytes);

        Assert.Equal(before, bytes);
    }
}
