using System.Reflection;
using Aveline.Api.Modules.Media;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit U0.3 — the frozen tag/context contract from the strategy's §3.2 table. <see cref="MediaTagger"/>
/// is I/O-free and takes the UTC instant as an argument, so none of these assertions touch the clock,
/// a network, or a media provider.
/// </summary>
public class MediaTaggerTests
{
    private static readonly Guid OrgId = Guid.Parse("a1b2c3d4-5e6f-4a7b-8c9d-0e1f2a3b4c5d");
    private static readonly Guid ConversationId = Guid.Parse("b2c3d4e5-6f7a-4b8c-9d0e-1f2a3b4c5d6e");
    private static readonly Guid UserId = Guid.Parse("c3d4e5f6-7a8b-4c9d-8e0f-2a3b4c5d6e7f");
    private static readonly Guid CustomerId = Guid.Parse("d4e5f6a7-8b9c-4d0e-9f1a-3b4c5d6e7f80");
    private static readonly DateTimeOffset Instant = new(2026, 9, 21, 10, 30, 0, TimeSpan.Zero);
    private const string UtcDate = "2026-09-21";
    private const string Jpeg = "image/jpeg";
    private const string Pdf = "application/pdf";

    // ---- Catalog write: exactly four labels and three context pairs (strategy §3.2) ----

    [Fact]
    public void CatalogEntryPoint_ProducesTheFourLabelsFromTheTable()
    {
        var metadata = MediaTagger.ForCatalogImage(OrgId, Instant);

        Assert.Equal(
            new[]
            {
                "catalog-image",
                "kind:image",
                $"organizationId:{OrgId}",
                $"date:{UtcDate}",
            },
            metadata.Labels);
    }

    [Fact]
    public void CatalogEntryPoint_ProducesTheThreeContextPairsFromTheTable()
    {
        var metadata = MediaTagger.ForCatalogImage(OrgId, Instant);

        Assert.Equal(3, metadata.Attributes.Count);
        Assert.Equal(new[] { "d", "o", "s" }, metadata.Attributes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(OrgId.ToString(), metadata.Attributes["o"]);
        Assert.Equal(UtcDate, metadata.Attributes["d"]);
        Assert.Equal("catalog", metadata.Attributes["s"]);
    }

    // ---- Salon attachment write: exactly seven labels and six context pairs ----

    [Fact]
    public void SalonEntryPoint_ProducesTheSevenLabelsFromTheTable()
    {
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.Web, OrgId, ConversationId, UserId, CustomerId, Jpeg, Instant);

        Assert.Equal(
            new[]
            {
                "salon-image",
                "source:web",
                "kind:image",
                $"conversationId:{ConversationId}",
                $"organizationId:{OrgId}",
                $"userId:{UserId}",
                $"date:{UtcDate}",
            },
            metadata.Labels);
    }

    [Fact]
    public void SalonEntryPoint_ProducesTheSixContextPairsFromTheTable()
    {
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.Web, OrgId, ConversationId, UserId, CustomerId, Jpeg, Instant);

        Assert.Equal(6, metadata.Attributes.Count);
        Assert.Equal(
            new[] { "c", "d", "o", "s", "u", "v" },
            metadata.Attributes.Keys.OrderBy(k => k, StringComparer.Ordinal));
        Assert.Equal(OrgId.ToString(), metadata.Attributes["o"]);
        Assert.Equal(ConversationId.ToString(), metadata.Attributes["v"]);
        Assert.Equal(UserId.ToString(), metadata.Attributes["u"]);
        Assert.Equal(CustomerId.ToString(), metadata.Attributes["c"]);
        Assert.Equal(UtcDate, metadata.Attributes["d"]);
        Assert.Equal("web", metadata.Attributes["s"]);
    }

    [Fact]
    public void InboundWhatsAppPath_CarriesUserIdNone()
    {
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.WhatsApp, OrgId, ConversationId, userId: null, CustomerId, Jpeg, Instant);

        Assert.Contains("source:whatsapp", metadata.Labels);
        Assert.Contains("userId:none", metadata.Labels);
        Assert.Equal("none", metadata.Attributes["u"]);
        Assert.Equal("whatsapp", metadata.Attributes["s"]);
    }

    [Fact]
    public void EveryMediaSource_RendersItsWireValueInTheSourceTag()
    {
        foreach (var source in MediaSource.All)
        {
            var metadata = MediaTagger.ForSalonAttachment(
                source, OrgId, ConversationId, UserId, CustomerId, Jpeg, Instant);

            Assert.Contains($"source:{source.Value}", metadata.Labels);
            Assert.Equal(source.Value, metadata.Attributes["s"]);
        }
    }

    [Fact]
    public void KindIsPdfForApplicationPdfAndIsStillSalonImage()
    {
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.Web, OrgId, ConversationId, UserId, CustomerId, Pdf, Instant);

        Assert.Contains("kind:pdf", metadata.Labels);
        Assert.DoesNotContain("kind:image", metadata.Labels);
        Assert.Contains("salon-image", metadata.Labels);
    }

    // ---- The 179-character worst case, recomputed rather than trusted ----

    [Fact]
    public void SalonEntryPoint_WorstCaseContextIsExactly179Characters()
    {
        // Every value at its documented maximum: four GUIDs, the longest source string ("whatsapp"),
        // and a ten-character UTC date (salon plan §6.3). The catalogue path cannot be longer.
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.WhatsApp, OrgId, ConversationId, UserId, CustomerId, Jpeg, Instant);

        var expected =
            $"o={OrgId}|v={ConversationId}|u={UserId}|c={CustomerId}|d={UtcDate}|s=whatsapp";
        var context = MediaTagger.RenderContext(metadata.Attributes);

        Assert.Equal(179, expected.Length);
        Assert.Equal(expected, context);
        Assert.Equal(179, context.Length);
        // The conservative reading of the two conflicting documented limits (salon C7 / research §2).
        Assert.True(context.Length <= 255, $"context was {context.Length} characters");
    }

    [Fact]
    public void SalonEntryPoint_OmitsTheCustomerPairWhenThereIsNoCustomer()
    {
        // Cloudinary rejects an empty context value ("Keys and values can't be empty", research §2),
        // so a missing customer is a missing pair, never a `c=` pair with an empty value.
        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.Web, OrgId, ConversationId, UserId, customerId: null, Jpeg, Instant);

        Assert.Equal(5, metadata.Attributes.Count);
        Assert.False(metadata.Attributes.ContainsKey("c"));
        Assert.DoesNotContain("c=", MediaTagger.RenderContext(metadata.Attributes));
    }

    [Fact]
    public void Date_IsRenderedInUtc()
    {
        // 02:00 on 2026-09-22 at UTC+05:00 is 21:00 on 2026-09-21 UTC.
        var instant = new DateTimeOffset(2026, 9, 22, 2, 0, 0, TimeSpan.FromHours(5));

        var metadata = MediaTagger.ForSalonAttachment(
            MediaSource.Web, OrgId, ConversationId, UserId, CustomerId, Jpeg, instant);

        Assert.Contains("date:2026-09-21", metadata.Labels);
        Assert.Equal("2026-09-21", metadata.Attributes["d"]);
    }

    // ---- The two rules that never enter context, asserted rather than stated ----

    [Fact]
    public void ContentType_NeverAppearsInContext()
    {
        foreach (var contentType in new[] { Jpeg, Pdf, "image/png" })
        {
            var metadata = MediaTagger.ForSalonAttachment(
                MediaSource.Web, OrgId, ConversationId, UserId, CustomerId, contentType, Instant);

            var context = MediaTagger.RenderContext(metadata.Attributes);

            Assert.DoesNotContain(contentType, context);
            Assert.DoesNotContain(contentType.Split('/')[1], context);
            Assert.DoesNotContain("image", context);
            Assert.DoesNotContain("pdf", context);
        }
    }

    [Fact]
    public void FileName_CannotReachContextBecauseNoEntryPointAcceptsOne()
    {
        // fileName is caller-controlled and may contain '=' or '|', the two characters that would
        // need escaping. The proof is structural: the tagger never receives a file name, so it
        // cannot emit one — a future parameter would fail this test.
        foreach (var method in EntryPoints())
        {
            Assert.DoesNotContain(
                method.GetParameters(),
                p => p.Name is not null && p.Name.Contains("file", StringComparison.OrdinalIgnoreCase));
        }
    }

    // ---- No forbidden character reaches a public id (strategy §3.3, salon C8) ----

    [Fact]
    public void PublicIds_UseTheFrozenEncodingAndCarryNoForbiddenCharacter()
    {
        var imageId = Guid.Parse("e5f6a7b8-9c0d-4e1f-8a2b-4c5d6e7f8091");
        var attachmentId = Guid.Parse("f6a7b8c9-0d1e-4f2a-9b3c-5d6e7f8091a2");

        var catalog = MediaTagger.BuildCatalogPublicId(OrgId, imageId);
        var conversation = MediaTagger.BuildConversationPublicId(OrgId, attachmentId);

        Assert.Equal($"aveline/{OrgId}/catalog/{imageId}", catalog);
        Assert.Equal($"aveline/{OrgId}/conversations/{attachmentId}", conversation);

        var forbidden = new[] { '?', '&', '#', '\\', '%', '<', '>', '+' };
        foreach (var publicId in new[] { catalog, conversation })
        {
            Assert.DoesNotContain(publicId, c => forbidden.Contains(c));
        }
    }

    [Fact]
    public void MediaSource_IsClosedToValuesOutsideTheThreeProvenances()
    {
        // A C# enum would accept (MediaSource)42; this type has no public constructor, so an
        // invalid source cannot be constructed at all.
        Assert.Equal(
            new[] { "url", "web", "whatsapp" },
            MediaSource.All.Select(s => s.Value).OrderBy(v => v, StringComparer.Ordinal));
        Assert.Empty(typeof(MediaSource).GetConstructors(BindingFlags.Public | BindingFlags.Instance));
    }

    private static IEnumerable<MethodInfo> EntryPoints()
    {
        yield return typeof(MediaTagger).GetMethod(nameof(MediaTagger.ForCatalogImage))!;
        yield return typeof(MediaTagger).GetMethod(nameof(MediaTagger.ForSalonAttachment))!;
    }
}
