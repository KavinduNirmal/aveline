using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.2 (lane L4) - the reference arm of <c>POST /internal/visual/analyze-image</c> and the
/// <c>primary_color</c> wire shape.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why these tests and not the two existing ones.</b> The two analyze-image cases in
/// <see cref="VisualEndpointsIntegrationTests"/> run their host with no vision key, so
/// <c>VisionService</c> takes the deterministic fallback and their assertions
/// (<c>Category</c>/<c>PrimaryColor</c> non-blank, <c>ConfidenceScore &gt; 0</c>) pass whether or not
/// the reference resolves - they cannot detect this unit's defect (strategy §4 C26). The decisive
/// assertion here is therefore <b>the argument handed to <see cref="IVisionService"/></b> and the
/// mint that produced it, never the analysis outcome.
/// </para>
/// <para>
/// The provider is faked at <see cref="IVisionService"/> (capture) and the grant at
/// <see cref="IMediaUrlSigner"/> (a recording decorator around the real HMAC signer), so no case
/// touches a vision provider, Cloudinary or Redis.
/// </para>
/// </remarks>
public class AnalyzeImageReferenceTests : IAsyncLifetime
{
    private const string InternalKey = "test-internal-analyze-key";

    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string PublicBaseUrl = "https://api.aveline.test";

    private readonly CapturingVisionService _vision = new();

    private readonly InMemoryNonceStore _nonces = new();

    private RecordingSigner _signer = null!;

    private WebApplicationFactory<Program> _factory = null!;

    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalKey);
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", PublicBaseUrl);

                builder.ConfigureServices(services =>
                {
                    // The capture point: the URL this unit is about is the argument, not the result.
                    services.RemoveAll<IVisionService>();
                    services.AddSingleton<IVisionService>(_vision);

                    // A recording decorator around the real signer, so a case can assert whether
                    // the analysability check ran before the mint as well as the scope it minted.
                    _signer = new RecordingSigner(new HmacMediaUrlSigner(
                        Options.Create(new MediaOptions
                        {
                            SigningKey = SigningKey,
                            PublicBaseUrl = PublicBaseUrl,
                        }),
                        TimeProvider.System,
                        _nonces));
                    services.RemoveAll<IMediaUrlSigner>();
                    services.AddSingleton<IMediaUrlSigner>(_signer);
                });
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // =======================================================================================
    // The reference arm: resolve, org-check, validate, mint
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_WithAnAnalysableReference_MintsAndHandsTheVisionProviderAnAbsoluteTokenisedUrl()
    {
        var attachment = await SeedAttachmentAsync("image/png");

        var response = await AnalyzeAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The argument to IVisionService is absolute, on Media:PublicBaseUrl, and tokenised.
        _vision.Urls.Should().ContainSingle();
        var url = _vision.Urls[0];
        Uri.TryCreate(url, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
        url.Should().StartWith($"{PublicBaseUrl}/api/v1/media/");
        url.Length.Should().BeLessThan(8192, "the provider's external-URL maximum is 8192 characters");

        // The grant was minted for this organisation with the vision scope, and it verifies.
        _signer.Mints.Should().ContainSingle();
        var mint = _signer.Mints[0];
        mint.OrganizationId.Should().Be(attachment.OrganizationId);
        mint.Scope.Should().Be(MediaScope.VisionAnalyze);
        mint.SingleUse.Should().BeTrue();

        var token = url[($"{PublicBaseUrl}/api/v1/media/").Length..];
        var verified = _signer.Verify(token, MediaScope.VisionAnalyze);
        verified.IsValid.Should().BeTrue();
        verified.Scope.Should().Be(MediaScope.VisionAnalyze);
        verified.OrganizationId.Should().Be(attachment.OrganizationId);
    }

    [Fact]
    public async Task AnalyzeImage_WithAnEmptyImageUrlAndAReference_TreatsEmptyAsAbsentAndUsesTheReference()
    {
        var attachment = await SeedAttachmentAsync("image/png");

        var response = await AnalyzeAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageUrl = string.Empty,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _vision.Urls.Should().ContainSingle()
            .Which.Should().StartWith($"{PublicBaseUrl}/api/v1/media/");
    }

    [Fact]
    public async Task AnalyzeImage_ForACrossOrgReference_Returns404AndNeverMints()
    {
        var attachment = await SeedAttachmentAsync("image/png");

        var response = await AnalyzeAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _signer.Mints.Should().BeEmpty("a reference the caller cannot see never mints");
        _vision.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeImage_ForAMissingReference_Returns404AndNeverMints()
    {
        var response = await AnalyzeAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageRefKind = "attachment",
            imageRefId = Guid.NewGuid(),
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _signer.Mints.Should().BeEmpty();
        _vision.Urls.Should().BeEmpty();
    }

    [Fact]
    public async Task AnalyzeImage_ForAnInventoryImageReference_MintsForTheCatalogRow()
    {
        var imageId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        await SeedInventoryImageAsync(imageId, orgId);

        var response = await AnalyzeAsync(new
        {
            organizationId = orgId,
            imageRefKind = "inventoryImage",
            imageRefId = imageId,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _signer.Mints.Should().ContainSingle()
            .Which.Scope.Should().Be(MediaScope.VisionAnalyze);
        _vision.Urls.Should().ContainSingle()
            .Which.Should().StartWith($"{PublicBaseUrl}/api/v1/media/");
    }

    // =======================================================================================
    // The analysable subset is checked before the mint
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_ForAStoredTypeOutsideTheAnalysableSubset_IsRecordedAsNotAnalysableAndNeverMints()
    {
        // HEIC is storable, servable and tagged normally, but the provider cannot read it
        // (strategy §3.6). It must be recorded as not analysable - not minted, not degraded.
        var attachment = await SeedAttachmentAsync("image/heic");

        var response = await AnalyzeAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _signer.Mints.Should().BeEmpty("the check runs before the mint");
        _vision.Urls.Should().BeEmpty("no unreadable asset is handed to the provider");

        var json = await response.Content.ReadAsStringAsync();
        json.Should().Contain("\"not_analysable\":true");
    }

    [Theory]
    [InlineData("image/avif")]
    [InlineData("image/bmp")]
    [InlineData("image/tiff")]
    [InlineData("application/pdf")]
    public async Task AnalyzeImage_ForEveryStoredTypeOutsideTheSubset_IsNotAnalysable(string contentType)
    {
        var attachment = await SeedAttachmentAsync(contentType);

        var response = await AnalyzeAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _signer.Mints.Should().BeEmpty();
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"not_analysable\":true");
    }

    // =======================================================================================
    // The compatibility arm is unchanged
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_WithAnExternalImageUrl_StillPassesTheUrlThroughUnchanged()
    {
        const string external = "https://example.com/saree.jpg";

        var response = await AnalyzeAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageUrl = external,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _vision.Urls.Should().ContainSingle().Which.Should().Be(external);
        _signer.Mints.Should().BeEmpty("the external-URL arm is not tokenised");
    }

    [Fact]
    public async Task AnalyzeImage_WithTheExternalUrlRefKind_FallsBackToTheImageUrlArm()
    {
        const string external = "https://example.com/saree.jpg";

        var response = await AnalyzeAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageRefKind = "externalUrl",
            imageUrl = external,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _vision.Urls.Should().ContainSingle().Which.Should().Be(external);
        _signer.Mints.Should().BeEmpty();
    }

    // =======================================================================================
    // The DTO keeps its live shape (strategy §4 C14)
    // =======================================================================================

    [Fact]
    public void AnalyzeImageDto_ImageUrl_IsStillANonNullableStringDefaultingToEmpty()
    {
        var property = typeof(AnalyzeImageDto).GetProperty(nameof(AnalyzeImageDto.ImageUrl));
        property.Should().NotBeNull();
        property!.PropertyType.Should().Be(typeof(string));

        var nullability = new NullabilityInfoContext().Create(property);
        nullability.ReadState.Should().Be(NullabilityState.NotNull);
        nullability.WriteState.Should().Be(NullabilityState.NotNull);

        new AnalyzeImageDto().ImageUrl.Should().BeEmpty();
    }

    [Fact]
    public void AnalyzeImageDto_WithoutAnImageUrlInThePayload_LeavesImageUrlEmpty()
    {
        var dto = JsonSerializer.Deserialize<AnalyzeImageDto>(
            $"{{\"organizationId\":\"{Guid.NewGuid()}\"}}",
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        dto.Should().NotBeNull();
        dto!.ImageUrl.Should().BeEmpty();
        dto.ImageUrl.Should().NotBeNull();
    }

    [Fact]
    public void AnalyzeImageDto_CarriesTheReferenceArmAdditively()
    {
        typeof(AnalyzeImageDto).GetProperty("ImageRefKind").Should().NotBeNull();
        typeof(AnalyzeImageDto).GetProperty("ImageRefId").Should().NotBeNull();
    }

    // =======================================================================================
    // The wire shape: `primary_color`
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_PrimaryColor_IsOnTheWireAsSnakeCase()
    {
        var response = await AnalyzeAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageUrl = "https://example.com/saree.jpg",
        });

        var json = await response.Content.ReadAsStringAsync();

        json.Should().Contain("\"primary_color\"", "image_tools.py reads `primary_color` first");
        json.Should().NotContain(
            "primaryColor",
            "the attribute replaces the camelCase name, so the wire has one spelling, not two");
    }

    [Fact]
    public void ImageAnalysisResultDto_PrimaryColor_SerializesAsPrimaryColorSnakeCase()
    {
        var json = JsonSerializer.Serialize(
            new ImageAnalysisResultDto { PrimaryColor = "emerald" },
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        json.Should().Contain("\"primary_color\":\"emerald\"");
        json.Should().NotContain("primaryColor");
    }

    // =======================================================================================
    // Seeding and helpers
    // =======================================================================================

    private async Task<HttpResponseMessage> AnalyzeAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/visual/analyze-image")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Internal-Token", InternalKey);
        return await _client.SendAsync(request);
    }

    private async Task<MessageAttachment> SeedAttachmentAsync(string contentType)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attachment = new MessageAttachment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            StorageProvider = "database",
            StorageKey = null,
            ImageData = [0x89, 0x50, 0x4E, 0x47],
            ContentType = contentType,
            FileName = "seeded",
            SizeBytes = 4,
            Url = "/api/v1/orgs/x/conversations/y/attachments/z",
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.MessageAttachments.Add(attachment);
        await context.SaveChangesAsync();
        return attachment;
    }

    private async Task SeedInventoryImageAsync(Guid imageId, Guid orgId)
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        context.InventoryImages.Add(new Aveline.Api.Modules.VisualIntelligence.Models.InventoryImage
        {
            Id = imageId,
            OrgId = orgId,
            StorageProvider = "database",
            StorageKey = null,
            ImageData = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/jpeg",
            FileName = "seeded.jpg",
            FileSizeBytes = 4,
            ImageUrl = "https://example.com/seeded.jpg",
            CreatedAtUtc = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();
    }

    private sealed class CapturingVisionService : IVisionService
    {
        public List<string> Urls { get; } = [];

        public Task<ImageAnalysisResultDto> AnalyzeAsync(
            string imageUrl,
            Guid organizationId,
            string? fileName = null,
            string? contextHint = null,
            CancellationToken cancellationToken = default)
        {
            lock (Urls)
            {
                Urls.Add(imageUrl);
            }

            return Task.FromResult(new ImageAnalysisResultDto
            {
                Category = "saree",
                PrimaryColor = "emerald",
                ConfidenceScore = 0.9,
            });
        }
    }

    private sealed class RecordingSigner(IMediaUrlSigner inner) : IMediaUrlSigner
    {
        public List<MediaTokenRequest> Mints { get; } = [];

        public string Mint(MediaTokenRequest request, out DateTimeOffset expiresAtUtc)
        {
            lock (Mints)
            {
                Mints.Add(request);
            }

            return inner.Mint(request, out expiresAtUtc);
        }

        public MediaTokenValidation Verify(string token, MediaScope requiredScope, string? ipAddress = null)
            => inner.Verify(token, requiredScope, ipAddress);
    }

    private sealed class InMemoryNonceStore : IMediaTokenNonceStore
    {
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default)
            => Task.FromResult(_claimed.Add(nonce) ? MediaNonceClaim.Claimed : MediaNonceClaim.AlreadyClaimed);
    }
}
