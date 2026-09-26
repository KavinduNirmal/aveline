using Aveline.Api.Modules.Privacy.Services;
using SkiaSharp;

namespace Aveline.Api.Tests;

/// <summary>
/// The disclosure image is drawn in code, so these catch the failures that would otherwise only be
/// visible on a customer's phone: an embedded font that did not load, a canvas of the wrong size, a
/// JPEG that is really something else.
/// </summary>
public class DisclosureImageComposerTests
{
    private static readonly DisclosureImageComposer Composer = new();

    [Fact]
    public void Compose_ProducesAJpegOfTheApprovedSize()
    {
        var bytes = Composer.Compose("Aurora Boutique");

        Assert.True(bytes.Length > 5_000, $"the image is suspiciously small: {bytes.Length} bytes");
        Assert.Equal(0xFF, bytes[0]);
        Assert.Equal(0xD8, bytes[1]);
        Assert.Equal(0xFF, bytes[2]);

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(1200, decoded.Width);
        Assert.Equal(675, decoded.Height);
    }

    [Fact]
    public void Compose_DrawsTheWhiteLockupThroughTheMiddleOfTheCanvas()
    {
        var bytes = Composer.Compose("Ceylon Handloom Collective");

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);

        // The mark, both wordmarks and the cross are white and sit on the canvas's centre line, so a
        // band across the middle must contain bright pixels. A font that failed to load would leave
        // only the aurora there, which is dark everywhere, and the image would ship silently.
        var bright = 0;
        for (var y = 300; y < 375; y++)
        {
            for (var x = 0; x < decoded.Width; x++)
            {
                var pixel = decoded.GetPixel(x, y);
                if (pixel.Red > 200 && pixel.Green > 200 && pixel.Blue > 200)
                {
                    bright++;
                }
            }
        }

        Assert.True(bright > 1_000, $"expected the white lockup in the centre band, found {bright} bright pixels");

        // Writes the real renderer's output for inspection when asked. Gated on an environment
        // variable so an ordinary run leaves no artefacts behind.
        var previewPath = Environment.GetEnvironmentVariable("AVELINE_PREVIEW_PATH");
        if (!string.IsNullOrWhiteSpace(previewPath))
        {
            File.WriteAllBytes(previewPath, bytes);
        }
    }

    [Fact]
    public void Compose_IsDeterministicForTheSameName()
    {
        // Byte-identical for the same name, which is what lets the provider key its cache and its
        // Cloudinary public id on the name instead of uploading on every message.
        Assert.Equal(
            Composer.Compose("Ceylon Handloom Collective"),
            Composer.Compose("Ceylon Handloom Collective"));
    }

    [Fact]
    public void Compose_DiffersForADifferentName()
    {
        Assert.NotEqual(Composer.Compose("Aurora Boutique"), Composer.Compose("Gravora"));
    }

    [Fact]
    public void Compose_StillFitsAVeryLongNameOnTheCanvas()
    {
        var bytes = Composer.Compose("Ceylon Handloom Collective And Textiles Atelier Limited");

        using var decoded = SKBitmap.Decode(bytes);
        Assert.NotNull(decoded);
        Assert.Equal(1200, decoded.Width);
        Assert.Equal(675, decoded.Height);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Compose_RejectsABlankName(string name) =>
        Assert.Throws<ArgumentException>(() => Composer.Compose(name));
}
