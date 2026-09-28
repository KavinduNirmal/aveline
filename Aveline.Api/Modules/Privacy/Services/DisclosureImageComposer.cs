using System.Reflection;
using SkiaSharp;

namespace Aveline.Api.Modules.Privacy.Services;

/// <summary>
/// Draws the first-contact disclosure image: the Aveline mark and wordmark, a cross, and the
/// boutique's own name, knocked out to white on the app's aurora.
/// </summary>
/// <remarks>
/// <para>
/// The composition is a co-branding lockup - <c>mark + Aveline × boutique</c> - which is the shape
/// the owner approved from a reference. The boutique's name is the hero and the Aveline side
/// supports it; that ordering is why the Aveline wordmark is deliberately smaller than the name it
/// sits beside, and why it must not creep up.
/// </para>
/// <para>
/// <b>The geometry below is frozen.</b> It was arrived at by rendering variants and looking at them,
/// so the numbers are not arbitrary and should not be "tidied". Changing any of them changes the
/// image customers see, which is why the layout carries a version and the provider keys its cache
/// and public id on it.
/// </para>
/// <para>
/// Fonts are embedded resources rather than files: the runtime image is chiseled with no font
/// packages, so a system font would simply be absent and the image would render without its text.
/// </para>
/// </remarks>
public sealed class DisclosureImageComposer : IDisclosureImageComposer
{
    /// <summary>
    /// Bumped whenever the frozen geometry or the copy of the drawing changes. The provider folds
    /// it into the Cloudinary public id, so a new version is a new URL rather than a stale cached
    /// image.
    /// </summary>
    public const string CurrentLayoutVersion = "v1";

    private const int CanvasWidth = 1200;
    private const int CanvasHeight = 675;
    private const int FitPercent = 86;
    private const int JpegQuality = 85;

    // --- frozen lockup geometry, in canvas pixels at 1200x675 --------------------------------
    private const float MarkSize = 165f;
    private const float MarkToWordmark = 32f;
    private const float AvelineTextSize = 70f;
    private const float WordmarkToCross = 104f;
    private const float CrossTextSize = 66f;
    private const float CrossToName = 104f;
    private const float NameTextSize = 92f;
    private const byte CrossAlpha = 204;          // 80% - present, without competing
    private const byte MarkAlpha = 235;

    // --- the aurora, from FlowerAuroraBackground.tsx ------------------------------------------
    private const byte OrbAlpha = 71;             // 0.28, as the component draws them
    private const float OrbBlurSigma = 90f;       // the component's blur(110px), scaled to this canvas
    private const byte GrainAlpha = 13;           // 0.05

    private const string BaseColour = "#140c0e";

    private static readonly (float Cx, float Cy, float Radius, string Colour)[] Orbs =
    [
        (324f, 238f, 204f, "#7a303f"),
        (912f, 522f, 252f, "#b0566b"),
        (660f, 827f, 300f, "#5d1a29"),
        (1188f, 517f, 348f, "#c9a227"),
    ];

    private readonly SKTypeface _displaySerif;
    private readonly SKTypeface _sansSemiBold;
    private readonly SKTypeface _sansRegular;

    public DisclosureImageComposer()
    {
        _displaySerif = LoadFont("Aveline.Api.Fonts.PlayfairDisplay-SemiBold.ttf");
        _sansSemiBold = LoadFont("Aveline.Api.Fonts.DMSans-SemiBold.ttf");
        _sansRegular = LoadFont("Aveline.Api.Fonts.DMSans-Regular.ttf");
    }

    /// <inheritdoc />
    public string LayoutVersion => CurrentLayoutVersion;

    /// <inheritdoc />
    public byte[] Compose(string boutiqueDisplayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(boutiqueDisplayName);
        var name = boutiqueDisplayName.Trim();

        using var surface = SKSurface.Create(new SKImageInfo(CanvasWidth, CanvasHeight));
        var canvas = surface.Canvas;

        canvas.Clear(SKColor.Parse(BaseColour));
        DrawAurora(canvas);
        DrawGrain(canvas);
        DrawLockup(canvas, name);

        using var snapshot = surface.Snapshot();
        using var encoded = snapshot.Encode(SKEncodedImageFormat.Jpeg, JpegQuality);
        return encoded.ToArray();
    }

    /// <summary>
    /// Four heavily blurred orbs over a warm dark base. A blur is linear, so applying the component's
    /// 0.28 opacity before blurring gives what blurring a 0.28-opacity circle would: the same field.
    /// </summary>
    private static void DrawAurora(SKCanvas canvas)
    {
        foreach (var (cx, cy, radius, colour) in Orbs)
        {
            using var paint = new SKPaint
            {
                Color = SKColor.Parse(colour).WithAlpha(OrbAlpha),
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateBlur(OrbBlurSigma, OrbBlurSigma),
            };
            canvas.DrawCircle(cx, cy, radius, paint);
        }
    }

    /// <summary>
    /// The component's faint film grain, at 5%. Blended as an overlay rather than filtered: a
    /// blend-mode <c>SKColorFilter</c> here is a *source* filter, so it blended the noise against
    /// transparent black and turned the whole canvas white. The grain is worth 5% of the look and
    /// not worth that failure mode.
    /// </summary>
    private static void DrawGrain(SKCanvas canvas)
    {
        using var shader = SKShader.CreatePerlinNoiseFractalNoise(0.9f, 0.9f, 2, 0f);
        using var paint = new SKPaint
        {
            Shader = shader,
            Color = SKColors.White.WithAlpha(GrainAlpha),
            BlendMode = SKBlendMode.Overlay,
        };
        canvas.DrawRect(0, 0, CanvasWidth, CanvasHeight, paint);
    }

    /// <summary>
    /// The lockup, centred on the canvas and scaled down as a whole when the boutique's name makes it
    /// too wide. Scaling the whole thing rather than the name alone is what keeps the proportions the
    /// same for every boutique.
    /// </summary>
    private void DrawLockup(SKCanvas canvas, string name)
    {
        using var mark = new SKPaint { Color = SKColors.White.WithAlpha(MarkAlpha), IsAntialias = true };

        // SkiaSharp 4 keeps typeface and size on SKFont and colour on SKPaint, so each element is a
        // font plus an ink.
        using var avelineFont = new SKFont(_sansSemiBold, AvelineTextSize);
        using var avelineInk = Ink(SKColors.White);
        using var crossFont = new SKFont(_sansRegular, CrossTextSize);
        using var crossInk = Ink(SKColors.White.WithAlpha(CrossAlpha));
        using var nameFont = new SKFont(_displaySerif, NameTextSize);
        using var nameInk = Ink(SKColors.White);

        const string avelineText = "Aveline";
        const string crossText = "\u00d7";

        var avelineWidth = avelineFont.MeasureText(avelineText, avelineInk);
        var crossWidth = crossFont.MeasureText(crossText, crossInk);
        var nameWidth = nameFont.MeasureText(name, nameInk);

        var naturalWidth = MarkSize + MarkToWordmark + avelineWidth + WordmarkToCross
                           + crossWidth + CrossToName + nameWidth;

        var fitWidth = CanvasWidth * FitPercent / 100f;
        var scale = naturalWidth > fitWidth ? fitWidth / naturalWidth : 1f;

        var totalWidth = naturalWidth * scale;
        var left = (CanvasWidth - totalWidth) / 2f;
        var centreY = CanvasHeight / 2f;

        // The mark: logo.svg's own 24x24 space, scaled into MarkSize and centred on the line.
        var markSize = MarkSize * scale;
        canvas.Save();
        canvas.Translate(left + markSize / 2f, centreY);
        canvas.Scale(markSize / 24f);
        canvas.Translate(-12f, -12f);
        DrawBlossom(canvas, mark);
        canvas.Restore();

        var x = left + markSize + MarkToWordmark * scale;

        // "Aveline" keeps DM Sans; the boutique keeps Playfair. That split is the point of the
        // lockup, so a change of font here is a change of design, not a tweak.
        x = DrawText(canvas, avelineFont, avelineInk, avelineText, AvelineTextSize, scale, x, centreY,
            WordmarkToCross * scale);
        x = DrawText(canvas, crossFont, crossInk, crossText, CrossTextSize, scale, x, centreY,
            CrossToName * scale);
        DrawText(canvas, nameFont, nameInk, name, NameTextSize, scale, x, centreY, 0f);
    }

    /// <summary>Draws one text element and returns the x it ends at plus the following gap.</summary>
    private static float DrawText(
        SKCanvas canvas,
        SKFont font,
        SKPaint ink,
        string text,
        float baseSize,
        float scale,
        float x,
        float centreY,
        float gapAfter)
    {
        font.Size = baseSize * scale;
        var metrics = font.Metrics;
        // Centre the glyph box on the line rather than the baseline, so faces of different sizes
        // share one optical centre.
        var baseline = centreY - (metrics.Ascent + metrics.Descent) / 2f;
        canvas.DrawText(text, x, baseline, font, ink);
        return x + font.MeasureText(text, ink) + gapAfter;
    }

    /// <summary>
    /// The eight-petal blossom of <c>frontend/web/public/logo.svg</c>, at its own coordinates: two
    /// rings of four ellipses about (12,12) at the opacities the file uses, plus the centre.
    /// </summary>
    private static void DrawBlossom(SKCanvas canvas, SKPaint paint)
    {
        var baseAlpha = paint.Color.Alpha;

        using var innerRing = paint.Clone();
        innerRing.Color = paint.Color.WithAlpha((byte)(baseAlpha * 0.92f));
        for (var i = 0; i < 4; i++)
        {
            canvas.Save();
            canvas.RotateDegrees(i * 90f, 12f, 12f);
            canvas.DrawOval(new SKRect(12f - 3.1f, 5.4f - 5.3f, 12f + 3.1f, 5.4f + 5.3f), innerRing);
            canvas.Restore();
        }

        using var outerRing = paint.Clone();
        outerRing.Color = paint.Color.WithAlpha((byte)(baseAlpha * 0.98f));
        for (var i = 0; i < 4; i++)
        {
            canvas.Save();
            canvas.RotateDegrees(45f + (i * 90f), 12f, 12f);
            canvas.DrawOval(new SKRect(12f - 2.95f, 5.4f - 5.1f, 12f + 2.95f, 5.4f + 5.1f), outerRing);
            canvas.Restore();
        }

        canvas.DrawCircle(12f, 12f, 2.3f, outerRing);
    }

    private static SKPaint Ink(SKColor colour) =>
        new()
        {
            Color = colour,
            IsAntialias = true,
        };

    private static SKTypeface LoadFont(string logicalName)
    {
        using var stream = typeof(DisclosureImageComposer).Assembly
            .GetManifestResourceStream(logicalName)
            ?? throw new InvalidOperationException(
                $"The embedded font '{logicalName}' is missing from the assembly. "
                + $"Present: {string.Join(", ", Assembly.GetExecutingAssembly().GetManifestResourceNames())}");

        return SKTypeface.FromStream(stream)
            ?? throw new InvalidOperationException(
                $"The embedded font '{logicalName}' could not be read as a typeface.");
    }
}
