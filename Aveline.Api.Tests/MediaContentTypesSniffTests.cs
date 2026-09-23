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
    public void Sniff_ConfirmsPdfButStillDoesNotCallItAnImage()
    {
        // Superseded expectation, changed deliberately: U0.2 shipped a sniff with no PDF arm, and
        // this case asserted a PDF was refused. The PDF arm now exists (U1.3a), so the sniff
        // *confirms* a PDF - while `IsRecognisableImage` keeps its narrower image meaning.
        Assert.Equal("application/pdf", MediaContentTypes.Sniff(PdfBytes));
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

    // ---------------------------------------------------------------------------------------
    // PDF: the `%PDF-` signature.
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Sniff_RecognisesARealPdfHeader()
    {
        var sniffed = MediaContentTypes.Sniff(PdfBytes);

        Assert.Equal("application/pdf", sniffed);

        // PDF is admitted by the storage allow-list, but it is not an image and no vision
        // provider reads it, so it is storable and not analysable.
        Assert.True(MediaContentTypes.IsAllowed(sniffed));
        Assert.False(MediaContentTypes.IsImage(sniffed));
        Assert.False(VisionContentTypes.IsAnalysable(sniffed));

        // `IsRecognisableImage` keeps its name and its narrower meaning: a PDF is recognisable
        // to the sniff but is still not an image.
        Assert.False(MediaContentTypes.IsRecognisableImage(PdfBytes));
    }

    [Fact]
    public void Sniff_ToleratesASmallLeadingOffsetBeforeThePdfHeader()
    {
        // Malformed-but-real PDFs can carry a small run of bytes before `%PDF-`. The sniff
        // accepts a complete header (marker + major.minor + end-of-line) inside a small leading
        // window rather than at byte 0 only.
        var offsetPdf = new byte[64];
        "garbage!"u8.CopyTo(offsetPdf);
        "%PDF-1.4\n"u8.CopyTo(offsetPdf.AsSpan(8));

        Assert.Equal("application/pdf", MediaContentTypes.Sniff(offsetPdf));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    [InlineData(32)]
    public void Sniff_AcceptsACompletePdfHeaderInsideTheSmallLeadingWindow(int offset)
    {
        var bytes = new byte[64];
        "%PDF-1.7\n"u8.CopyTo(bytes.AsSpan(offset));

        Assert.Equal("application/pdf", MediaContentTypes.Sniff(bytes));
    }

    [Fact]
    public void Sniff_RefusesAPdfHeaderJustPastTheSmallLeadingWindow()
    {
        // The window is 32 bytes inclusive, so a marker at byte 33 is not a header. That is the
        // trade-off the narrow window buys: a malformed PDF with a longer leading run is not a
        // PDF here, and every path that consults the sniff then fails closed (upload refused,
        // fetch refused) rather than storing or trusting bytes the sniff cannot confirm.
        var bytes = new byte[64];
        "%PDF-1.7\n"u8.CopyTo(bytes.AsSpan(33));

        Assert.Null(MediaContentTypes.Sniff(bytes));
    }

    public static TheoryData<string, byte[]> IncompletePdfHeaders => new()
    {
        { "bare-marker", "%PDF-"u8.ToArray() },
        { "major-only", "%PDF-1"u8.ToArray() },
        { "no-minor", "%PDF-1."u8.ToArray() },
        { "version-with-no-terminator", "%PDF-1.7"u8.ToArray() },
        { "version-then-a-non-eol-byte", "%PDF-1.7x"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(IncompletePdfHeaders))]
    public void Sniff_RefusesAnIncompletePdfHeader(string label, byte[] bytes)
    {
        // `%PDF-` is a prefix of the signature, not the signature: a header carries a major.minor
        // version and the end-of-line that terminates the header line. Anything shorter is a
        // prefix and must not be confirmed as a document.
        Assert.Null(MediaContentTypes.Sniff(bytes));
        _ = label;
    }

    [Fact]
    public void Sniff_RefusesAPdfHeaderThatSitsBeyondTheLeadingWindow()
    {
        // The window is bounded on purpose. A `%PDF-` buried in the payload is not a PDF
        // header, and a full-payload substring search would let an arbitrary body be blessed
        // by a marker anywhere inside it.
        var buried = new byte[4096];
        Array.Fill(buried, (byte)'x');
        "%PDF-1.7\n"u8.CopyTo(buried.AsSpan(3000));

        Assert.Null(MediaContentTypes.Sniff(buried));
    }

    /// <summary>A JPEG whose payload carries a `%PDF-` header deep inside it, at byte 200.</summary>
    private static byte[] JpegCarryingABuriedPdfMarker()
    {
        var bytes = new byte[240];
        JpegBytes.CopyTo(bytes, 0);
        "%PDF-1.7\n"u8.CopyTo(bytes.AsSpan(200));
        return bytes;
    }

    /// <summary>A PNG whose payload carries a `%PDF-` header deep inside it, at byte 200.</summary>
    private static byte[] PngCarryingABuriedPdfMarker()
    {
        var bytes = new byte[240];
        PngBytes.CopyTo(bytes, 0);
        "%PDF-1.7\n"u8.CopyTo(bytes.AsSpan(200));
        return bytes;
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/png")]
    public void Sniff_DoesNotClassifyAnImageCarryingABuriedPdfMarkerAsPdf(string imageType)
    {
        // F9 defect: SniffPdf used to search the first 1024 bytes for `%PDF-`, so a JPEG or PNG
        // whose first kilobyte happened to carry that marker was classified `application/pdf`.
        // On the upload/sniff path that stored image bytes under a PDF content type; on the
        // fetch path it was a false refusal. The marker at byte 200 sits inside the old window
        // and outside the PDF header window, so the bytes must decide the image's real type.
        var bytes = imageType == "image/jpeg"
            ? JpegCarryingABuriedPdfMarker()
            : PngCarryingABuriedPdfMarker();

        Assert.Equal(imageType, MediaContentTypes.Sniff(bytes));
        Assert.True(MediaContentTypes.IsRecognisableImage(bytes));
    }

    public static TheoryData<string, byte[]> PdfClaimedNonPdfBodies => new()
    {
        { "xml", "<?xml version=\"1.0\"?><root><evil/></root>"u8.ToArray() },
        { "html", "<!DOCTYPE html><html><body>not a pdf</body></html>"u8.ToArray() },
        { "plain-text", "not a pdf at all"u8.ToArray() },
        { "leading-whitespace-then-xml", "   \n\t<?xml version=\"1.0\"?><r/>"u8.ToArray() },
    };

    [Theory]
    [MemberData(nameof(PdfClaimedNonPdfBodies))]
    public void Sniff_ReturnsNothingForANonPdfBodyClaimingToBePdf(string label, byte[] bytes)
    {
        // The extension and the declared type both say `application/pdf`; only the bytes decide.
        // This is the arm with the most hostile content (a PDF can carry JavaScript), so an XML
        // or HTML body must not be confirmed as a PDF on the strength of a `.pdf` name.
        Assert.Equal("application/pdf", MediaContentTypes.Resolve("application/pdf", "report.pdf"));
        Assert.Equal("application/pdf", MediaContentTypes.Resolve("application/octet-stream", "report.pdf"));

        // The sniff returns nothing, so a policy that requires the sniff to confirm the PDF arm
        // refuses these bodies.
        Assert.Null(MediaContentTypes.Sniff(bytes));
        _ = label;
    }

    [Theory]
    [MemberData(nameof(PdfClaimedNonPdfBodies))]
    public void APdfDeclaredBodyWithNoRecognisableSignatureIsRefusedByASniffRequiringPolicy(
        string label, byte[] bytes)
    {
        // The exact rule the owner of AttachmentContentPolicy can now apply: a resolved PDF is
        // kept only when the bytes confirm it.
        var resolved = MediaContentTypes.Resolve("application/pdf", "report.pdf");
        var sniffed = MediaContentTypes.Sniff(bytes);

        var stored = resolved == MediaContentTypes.Pdf && sniffed is null ? null : sniffed;

        Assert.Null(stored);
        _ = label;
    }

    [Fact]
    public void Sniff_RefusesALowercasePdfHeader()
    {
        // The PDF header is the literal `%PDF-`. A lowercase variant is not a PDF header, and
        // the sniff must not treat it as one.
        Assert.Null(MediaContentTypes.Sniff("%pdf-1.7\n"u8.ToArray()));
    }

    [Fact]
    public void Sniff_RefusesAPdfMarkerWithNoVersion()
    {
        // `%PDF-` is a prefix of the signature, not the signature: a real header carries version
        // digits, and a file that stops at the marker is not a PDF.
        Assert.Null(MediaContentTypes.Sniff("%PDF-"u8.ToArray()));
    }

    [Theory]
    [InlineData("image/png")]
    [InlineData("image/jpeg")]
    public void ARealImageBodyDeclaredAsPdfKeepsItsImageType(string expected)
    {
        // The mirror of the refusal: a genuine image body mislabelled `.pdf` is not a PDF, and
        // it is not refused either - the bytes win and the stored type is the image's.
        var bytes = expected == "image/png" ? PngBytes : JpegBytes;

        Assert.Equal(expected, MediaContentTypes.Sniff(bytes));
        Assert.NotEqual(MediaContentTypes.Pdf, MediaContentTypes.Sniff(bytes));

        // `Resolve` still returns the declared PDF; the sniff is what corrects it, which is the
        // whole reason the policy consults the sniff before keeping a PDF.
        Assert.Equal("application/pdf", MediaContentTypes.Resolve("application/pdf", "report.pdf"));
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
