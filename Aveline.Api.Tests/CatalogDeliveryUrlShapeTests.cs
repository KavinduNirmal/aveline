using System.Text.RegularExpressions;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U1.2 (lane L2) — the catalog delivery contract (strategy §3.7; plan §4 Wave 1; §7 check 11):
/// every catalog delivery URL carries <b>exactly one</b> width, plus <c>f_auto</c> and
/// <c>q_auto</c>; no URL carries a second <c>w_</c>, and none carries a fixed <c>q_&lt;n&gt;</c>
/// or <c>f_&lt;fmt&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// This is the cheapest high-value test in the plan. Adding "just one more width" multiplies
/// <c>f_auto</c>'s derivations — three widths times two formats is six derivations per asset,
/// which is 30 credits at only 5,000 assets — and can lose the Free plan faster than the
/// bandwidth it saves (§3.7, §6 item 12). The single width is therefore a gate, not a guideline.
/// </para>
/// <para>
/// <c>f_webp</c> is called out separately because it is the plausible mistake: Cloudinary's
/// <c>q_auto</c> deliberately refuses WebP when chroma subsampling would hurt colour fidelity,
/// which is exactly the boutique-colour case the vision pipeline samples a hex from (§3.7
/// point 2), so forcing the format trades that fidelity for bytes.
/// </para>
/// </remarks>
public class CatalogDeliveryUrlShapeTests
{
    /// <summary>Every shape a real Cloudinary secure URL can take for a catalog upload.</summary>
    public static TheoryData<string> ProviderUrls => new()
    {
        // With a version and a format, the shape an image upload returns.
        "https://res.cloudinary.com/a-cloud/image/upload/v1712345678/aveline/org/catalog/id.jpg",
        // No version.
        "https://res.cloudinary.com/a-cloud/image/upload/aveline/org/catalog/id.jpg",
        // No format extension.
        "https://res.cloudinary.com/a-cloud/image/upload/v1/aveline/org/catalog/id",
    };

    private const string StorageKey = "image/upload:aveline/org/catalog/id";

    [Fact]
    public void TheConfiguredDisplayWidth_IsTheOneWidthAndDefaultsTo800()
    {
        new MediaOptions().CatalogDisplayWidth.Should().Be(800);
    }

    [Theory]
    [MemberData(nameof(ProviderUrls))]
    public void Build_CarriesExactlyOneWidthAndAutomaticFormatAndQuality(string providerUrl)
    {
        var url = CatalogDeliveryUrl.Build(providerUrl, StorageKey, 800);

        url.Should().Contain("w_800");
        url.Should().Contain("f_auto");
        url.Should().Contain("q_auto");
    }

    [Theory]
    [MemberData(nameof(ProviderUrls))]
    public void Build_NeverEmitsASecondWidth(string providerUrl)
    {
        var url = CatalogDeliveryUrl.Build(providerUrl, StorageKey, 800);

        // A second `w_` is the single change that loses the plan fastest (§3.7, §6 item 12).
        Regex.Matches(url, "w_").Should().HaveCount(1);
    }

    [Theory]
    [MemberData(nameof(ProviderUrls))]
    public void Build_NeverEmitsAFixedQualityOrFormat(string providerUrl)
    {
        var url = CatalogDeliveryUrl.Build(providerUrl, StorageKey, 800);

        Regex.IsMatch(url, @"q_\d").Should().BeFalse("no fixed q_<n>; q_auto is the contract");
        Regex.IsMatch(url, @"f_(?!auto\b)[a-z0-9]+")
            .Should().BeFalse("no fixed f_<fmt>, and specifically not f_webp; f_auto is the contract");
    }

    [Theory]
    [MemberData(nameof(ProviderUrls))]
    public void Build_EmitsTheContractAsOneSegmentInOrder(string providerUrl)
    {
        var url = CatalogDeliveryUrl.Build(providerUrl, StorageKey, 800);

        // The transformation is one comma-joined segment, so a second width inside it would
        // change this exact string rather than merely adding to the URL.
        TransformationSegment(url).Should().Be("w_800,f_auto,q_auto");
    }

    [Fact]
    public void Build_UsesTheWidthItWasGiven()
    {
        var url = CatalogDeliveryUrl.Build(
            "https://res.cloudinary.com/a-cloud/image/upload/v1/aveline/org/catalog/id.jpg",
            StorageKey,
            640);

        TransformationSegment(url).Should().Be("w_640,f_auto,q_auto");
        Regex.Matches(url, "w_").Should().HaveCount(1);
    }

    [Fact]
    public void Build_LeavesThePublicIdAndOriginIntact()
    {
        const string providerUrl =
            "https://res.cloudinary.com/a-cloud/image/upload/v1712345678/aveline/org/catalog/id.jpg";

        var url = CatalogDeliveryUrl.Build(providerUrl, StorageKey, 800);

        url.Should().StartWith("https://res.cloudinary.com/a-cloud/image/upload/w_800,f_auto,q_auto/");
        url.Should().EndWith("aveline/org/catalog/id.jpg");
    }

    [Fact]
    public void Build_WithANonPositiveWidth_IsRefused()
    {
        var act = () => CatalogDeliveryUrl.Build(
            "https://res.cloudinary.com/a-cloud/image/upload/v1/aveline/org/catalog/id.jpg",
            StorageKey,
            0);

        // A variant of zero width is a bug, not a default (strategy §3.4).
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    /// <summary>The one slash-delimited segment the delivery contract occupies.</summary>
    private static string TransformationSegment(string url)
    {
        const string marker = "/image/upload/";
        var start = url.IndexOf(marker, StringComparison.Ordinal) + marker.Length;
        var end = url.IndexOf('/', start);
        return url[start..end];
    }
}
