using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.1 (lane L1) — the HTTP surface: <c>GET /api/v1/media/{token}</c> (a streaming proxy, never a
/// redirect), <c>POST /internal/visual/media-token</c>, and
/// <c>POST …/conversations/{id}/attachments/{id}/media-token</c>.
/// </summary>
/// <remarks>
/// <para>
/// The decisive case is <see cref="ServeToken_HoldATokenThenForceExpiry_TheSecondRequestFails"/>:
/// hold a token, force expiry, assert the <b>second</b> request fails. A <c>302</c> to a permanent
/// signed URL could not pass it, because the caller would keep the redirect target past
/// <c>exp</c>; the proxy is the only place <c>exp</c> can be re-checked (migration plan §7.3,
/// strategy §7 check 4).
/// </para>
/// <para>
/// The provider is faked at <see cref="IMediaStorage"/> and the nonce store at
/// <see cref="IMediaTokenNonceStore"/>, so no case touches Cloudinary or Redis. The rows are
/// seeded directly, so the cases do not depend on the configured provider.
/// </para>
/// </remarks>
public class MediaTokenEndpointTests : IAsyncLifetime
{
    private const string InternalKey = "test-internal-media-key";

    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string PublicBaseUrl = "https://api.aveline.test";

    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private static readonly byte[] TinyPng =
    [
        0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
    ];

    private readonly MutableClock _clock = new(Now);

    private readonly ScriptedMediaStorage _storage = new();

    private readonly RecordingNonceStore _nonces = new();

    private readonly LogCollector _logs = new();

    private RsaSecurityKey _rsaKey = null!;

    private StubAuthServer _authServer = null!;

    private WebApplicationFactory<Program> _factory = null!;

    private HttpClient _client = null!;

    /// <summary>The host used by every case that does not mint for a conversation.</summary>
    private WebApplicationFactory<Program> _unsignedFactory = null!;

    private HttpClient _unsignedClient = null!;

    public async Task InitializeAsync()
    {
        _rsaKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_rsaKey);
        await _authServer.StartAsync();

        // The Clerk-authenticated host: needed only by the conversation mint route.
        _factory = BuildHost(readFromCloudinary: true, withAuth: true);
        _client = _factory.CreateClient();

        // A second host that never resolves the Clerk authority (no stub), for the anonymous and
        // internal routes. Kept separate so a conversation-auth failure cannot mask a token result.
        _unsignedFactory = BuildHost(readFromCloudinary: true, withAuth: false);
        _unsignedClient = _unsignedFactory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _unsignedClient.Dispose();
        await _unsignedFactory.DisposeAsync();
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    // =======================================================================================
    // GET /api/v1/media/{token} — streaming, private, no-store
    // =======================================================================================

    [Fact]
    public async Task ServeToken_WithAValidAttachmentToken_StreamsTheAssetWithPrivateNoStore()
    {
        var attachment = await SeedAttachmentAsync(bytes: [1, 2, 3, 4], provider: "database", storageKey: null);
        var token = Mint(attachment.OrganizationId, AssetKeyFor(attachment), MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Headers.CacheControl.Should().NotBeNull();
        response.Headers.CacheControl!.Private.Should().BeTrue();
        response.Headers.CacheControl.NoStore.Should().BeTrue();
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        response.Content.Headers.ContentDisposition!.DispositionType.Should().Be("inline");
        response.Content.Headers.ContentType!.MediaType.Should().Be("image/png");
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(1, 2, 3, 4);
    }

    [Fact]
    public async Task ServeToken_ForACloudinaryRow_StreamsTheProvidersBytes()
    {
        var stored = new byte[] { 9, 8, 7 };
        _storage.OnOpen = _ => new MemoryStream(stored, writable: false);
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsByteArrayAsync()).Should().Equal(stored);
        _storage.Opens.Should().ContainSingle()
            .Which.StorageKey.Should().Be(attachment.StorageKey);
    }

    // =======================================================================================
    // The decisive test a redirect could not pass
    // =======================================================================================

    [Fact]
    public async Task ServeToken_HoldATokenThenForceExpiry_TheSecondRequestFails()
    {
        var attachment = await SeedAttachmentAsync(bytes: [5, 5, 5], provider: "database", storageKey: null);
        var token = Mint(attachment.OrganizationId, AssetKeyFor(attachment), MediaScope.AttachmentView);
        var route = $"/api/v1/media/{token}";

        var first = await _unsignedClient.GetAsync(route);
        first.StatusCode.Should().Be(HttpStatusCode.OK);

        // Past `exp` plus the 30 s clock-skew tolerance.
        _clock.Now = Now.AddSeconds(900 + 30 + 1);

        var second = await _unsignedClient.GetAsync(route);

        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await second.Content.ReadAsStringAsync()).Should().NotContain(token);
    }

    // =======================================================================================
    // The status matrix
    // =======================================================================================

    [Fact]
    public async Task ServeToken_WithATamperedToken_Returns401WithoutTheToken()
    {
        var attachment = await SeedAttachmentAsync(bytes: [1], provider: "database", storageKey: null);
        var token = Mint(attachment.OrganizationId, AssetKeyFor(attachment), MediaScope.AttachmentView);
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{tampered}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadAsStringAsync()).Should().NotContain(tampered);
        _storage.Opens.Should().BeEmpty();
    }

    [Fact]
    public async Task ServeToken_ForAReplayedVisionToken_Returns401()
    {
        _storage.OnOpen = _ => new MemoryStream([7], writable: false);
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.VisionAnalyze);
        var route = $"/api/v1/media/{token}";

        var first = await _unsignedClient.GetAsync(route);
        var second = await _unsignedClient.GetAsync(route);

        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        _storage.Opens.Should().ContainSingle("the replay is refused before the fetch");
    }

    [Fact]
    public async Task ServeToken_WhenTheNonceStoreIsUnavailable_FailsClosedAndServesNothing()
    {
        _storage.OnOpen = _ => new MemoryStream([7], writable: false);
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.VisionAnalyze);
        _nonces.Unavailable = true;

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
        (await response.Content.ReadAsByteArrayAsync()).Should().NotEqual(new byte[] { 7 });
        _storage.Opens.Should().BeEmpty("no claim means no fetch (strategy §3.8)");
    }

    [Fact]
    public async Task ServeToken_ForADeletedRow_Returns404BeforeAnyProviderCall()
    {
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.AttachmentView);

        await DeleteAttachmentAsync(attachment.Id);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _storage.Opens.Should().BeEmpty();
    }

    [Fact]
    public async Task ServeToken_ForATokenFromAnotherTenant_Returns404()
    {
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        // Minted for a different organisation than the row's: tenant binding, not scope, is what
        // refuses it (migration plan §7.5).
        var token = Mint(Guid.NewGuid(), attachment.StorageKey!, MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _storage.Opens.Should().BeEmpty();
    }

    [Fact]
    public async Task ServeToken_ForARowWithNeitherBytesNorAKey_Returns404Never500()
    {
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "database", storageKey: null);
        var token = Mint(attachment.OrganizationId, attachment.Id.ToString(), MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        _storage.Opens.Should().BeEmpty();
    }

    [Fact]
    public async Task ServeToken_WhenTheProviderDenies_Returns502WithACorrelationIdAndLogsTheProviderDetail()
    {
        _storage.OpenException = new MediaStorageException(403, "X-Cld-Error: signature denied");
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.BadGateway);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("correlationId");
        body.Should().NotContain(token);
        body.Should().NotContain("signature denied", "the provider's text is logged, not echoed");

        _logs.Messages.Should().Contain(message => message.Contains("signature denied", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ServeToken_WhenTheProviderRateLimitPersists_Returns503()
    {
        _storage.OpenException = new MediaStorageException(420, "rate limited");
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable);
    }

    [Fact]
    public async Task ServeToken_WhenTheProviderTimesOut_Returns504()
    {
        _storage.OpenException = new MediaStorageException(
            0, "the provider did not answer", new TaskCanceledException("the provided did not answer"));
        var attachment = await SeedAttachmentAsync(bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));
        var token = Mint(attachment.OrganizationId, attachment.StorageKey!, MediaScope.AttachmentView);

        var response = await _unsignedClient.GetAsync($"/api/v1/media/{token}");

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout);
    }

    [Fact]
    public async Task ServeToken_WithAGarbageToken_Returns401()
    {
        var response = await _unsignedClient.GetAsync("/api/v1/media/not-a-token-at-all");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // =======================================================================================
    // The token never reaches a log line
    // =======================================================================================

    [Fact]
    public async Task ServeToken_OnEveryOutcome_TheTokenNeverAppearsInALogLine()
    {
        var attachment = await SeedAttachmentAsync(bytes: [1], provider: "database", storageKey: null);
        var token = Mint(attachment.OrganizationId, AssetKeyFor(attachment), MediaScope.AttachmentView);
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        await _unsignedClient.GetAsync($"/api/v1/media/{token}");
        await _unsignedClient.GetAsync($"/api/v1/media/{tampered}");

        _logs.Messages.Should().NotContain(message =>
            message.Contains(token, StringComparison.Ordinal)
            || message.Contains(tampered, StringComparison.Ordinal));
    }

    // =======================================================================================
    // POST /internal/visual/media-token — the agent's mint
    // =======================================================================================

    [Fact]
    public async Task MintForReference_WithAValidInternalToken_ReturnsTokenUrlPublicIdAndExpiry()
    {
        var attachment = await SeedAttachmentAsync(bytes: [1, 2], provider: "database", storageKey: null);

        var response = await MintAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
            scope = "vision.analyze",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var minted = await response.Content.ReadFromJsonAsync<MintResponse>();
        minted.Should().NotBeNull();
        minted!.Token.Should().NotBeNullOrWhiteSpace();
        minted.Url.Should().Be($"{PublicBaseUrl}/api/v1/media/{minted.Token}");
        Uri.TryCreate(minted.Url, UriKind.Absolute, out var uri).Should().BeTrue();
        uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
        minted.Url.Length.Should().BeLessThan(8192);
        minted.PublicId.Should().Be(attachment.Id.ToString());
        minted.ExpiresAtUtc.Should().Be(Now.AddSeconds(600));
    }

    [Fact]
    public async Task MintForReference_WithoutTheInternalToken_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/visual/media-token")
        {
            Content = JsonContent.Create(new
            {
                organizationId = Guid.NewGuid(),
                imageRefKind = "attachment",
                imageRefId = Guid.NewGuid(),
                scope = "vision.analyze",
            }),
        };

        var response = await _unsignedClient.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MintForReference_ForACrossOrgReference_Returns404AndNoToken()
    {
        var attachment = await SeedAttachmentAsync(bytes: [1], provider: "database", storageKey: null);

        var response = await MintAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
            scope = "vision.analyze",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("token");
    }

    [Fact]
    public async Task MintForReference_WithAnUnpermittedScope_Returns403()
    {
        var attachment = await SeedAttachmentAsync(bytes: [1], provider: "database", storageKey: null);

        var response = await MintAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
            scope = "attachment.view",
        });

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MintForReference_WithAnUnknownReferenceKind_Returns400()
    {
        var response = await MintAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageRefKind = "externalUrl",
            imageRefId = Guid.NewGuid(),
            scope = "vision.analyze",
        });

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task MintForReference_ForAMissingRow_Returns404()
    {
        var response = await MintAsync(new
        {
            organizationId = Guid.NewGuid(),
            imageRefKind = "attachment",
            imageRefId = Guid.NewGuid(),
            scope = "vision.analyze",
        });

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MintForReference_ThenFetchingTheToken_ServesTheAsset()
    {
        _storage.OnOpen = _ => new MemoryStream([3, 3], writable: false);
        var attachment = await SeedAttachmentAsync(
            bytes: null, provider: "cloudinary", storageKey: CloudinaryKey(Guid.NewGuid()));

        var response = await MintAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
            scope = "vision.analyze",
        });

        var minted = await response.Content.ReadFromJsonAsync<MintResponse>();
        var fetch = await _unsignedClient.GetAsync(new Uri(minted!.Url).PathAndQuery);

        fetch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetch.Content.ReadAsByteArrayAsync()).Should().Equal(3, 3);
    }

    [Fact]
    public async Task MintForReference_ForAnInventoryImage_ThenFetchingTheToken_ServesTheAsset()
    {
        // The second reference kind, and the second row type the locator resolves (the catalog
        // tier's row seam).
        _storage.OnOpen = _ => new MemoryStream([4, 4], writable: false);
        var image = await SeedInventoryImageAsync(
            provider: "cloudinary", storageKey: $"image/upload:aveline/{Guid.NewGuid()}/catalog/{Guid.NewGuid()}");

        var response = await MintAsync(new
        {
            organizationId = image.OrgId,
            imageRefKind = "inventoryImage",
            imageRefId = image.Id,
            scope = "vision.analyze",
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var minted = await response.Content.ReadFromJsonAsync<MintResponse>();
        minted!.PublicId.Should().Be(image.StorageKey!["image/upload:".Length..]);

        var fetch = await _unsignedClient.GetAsync(new Uri(minted.Url).PathAndQuery);
        fetch.StatusCode.Should().Be(HttpStatusCode.OK);
        (await fetch.Content.ReadAsByteArrayAsync()).Should().Equal(4, 4);
    }

    // =======================================================================================
    // POST …/conversations/{id}/attachments/{id}/media-token — the member's mint
    // =======================================================================================

    [Fact]
    public async Task MintForConversationAttachment_WithAMemberAtThePolicy_ReturnsATokenThatServes()
    {
        var (owner, org) = await SeedActiveOwnerAsync("media_owner_a", "media-owner-a");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var conversation = await CreateConversationAsync(org.Id, token);
        var attachmentId = await UploadAttachmentAsync(org.Id, conversation, token);

        var mint = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation}/attachments/{attachmentId}/media-token",
            token, new { }));
        var minted = await mint.Content.ReadFromJsonAsync<MintResponse>();

        mint.StatusCode.Should().Be(HttpStatusCode.OK);
        minted!.Token.Should().NotBeNullOrWhiteSpace();
        minted.PublicId.Should().Be(attachmentId.ToString());

        // The attachment's own stored route is still not anonymous: the minted token is the one
        // additional way in.
        var direct = await _unsignedClient.GetAsync(
            $"/api/v1/orgs/{org.Id}/conversations/{conversation}/attachments/{attachmentId}");
        direct.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // A private attachment is readable only through a minted token.
        var anonymous = await _unsignedClient.GetAsync(new Uri(minted.Url).PathAndQuery);
        anonymous.StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.Content.ReadAsByteArrayAsync()).Should().Equal(TinyPng);
    }

    [Fact]
    public async Task MintForConversationAttachment_WithoutABearerToken_Returns401()
    {
        var response = await _unsignedClient.PostAsJsonAsync(
            $"/api/v1/orgs/{Guid.NewGuid()}/conversations/{Guid.NewGuid()}/attachments/{Guid.NewGuid()}/media-token",
            new { });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task MintForConversationAttachment_WithAVisionScope_Returns403()
    {
        var (owner, org) = await SeedActiveOwnerAsync("media_owner_b", "media-owner-b");
        var token = CreateToken(owner.ClerkId, owner.Email);
        var conversation = await CreateConversationAsync(org.Id, token);
        var attachmentId = await UploadAttachmentAsync(org.Id, conversation, token);

        var mint = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{org.Id}/conversations/{conversation}/attachments/{attachmentId}/media-token",
            token, new { scope = "vision.analyze" }));

        mint.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task MintForConversationAttachment_ForAnotherOrgsAttachment_Returns404()
    {
        var (ownerA, orgA) = await SeedActiveOwnerAsync("media_owner_c", "media-owner-c");
        var (ownerB, orgB) = await SeedActiveOwnerAsync("media_owner_d", "media-owner-d");
        var tokenA = CreateToken(ownerA.ClerkId, ownerA.Email);
        var tokenB = CreateToken(ownerB.ClerkId, ownerB.Email);
        var conversation = await CreateConversationAsync(orgA.Id, tokenA);
        var attachmentId = await UploadAttachmentAsync(orgA.Id, conversation, tokenA);

        // A member of org B presenting org A's conversation and attachment under org B's route.
        var mint = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{orgB.Id}/conversations/{conversation}/attachments/{attachmentId}/media-token",
            tokenB, new { }));

        mint.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // =======================================================================================
    // Seeding and helpers
    // =======================================================================================

    private WebApplicationFactory<Program> BuildHost(bool readFromCloudinary, bool withAuth)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                if (withAuth)
                {
                    builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                }
                else
                {
                    builder.UseSetting("Clerk:Authority", "http://localhost:0");
                }

                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalKey);
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", PublicBaseUrl);
                builder.UseSetting("Media:ReadFromCloudinary", readFromCloudinary ? "true" : "false");
                builder.ConfigureLogging(logging => logging.AddProvider(_logs));
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IMediaStorage>();
                    services.AddSingleton<IMediaStorage>(_storage);
                    services.RemoveAll<IMediaTokenNonceStore>();
                    services.AddSingleton<IMediaTokenNonceStore>(_nonces);
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(_clock);
                });
            });
    }

    private string Mint(Guid organizationId, string assetKey, MediaScope scope)
    {
        using var scope_ = _unsignedFactory.Services.CreateScope();
        var minter = scope_.ServiceProvider.GetRequiredService<MediaTokenMintService>();
        return minter.Mint(organizationId, assetKey, scope).Token!;
    }

    private async Task<HttpResponseMessage> MintAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/visual/media-token")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Internal-Token", InternalKey);
        return await _unsignedClient.SendAsync(request);
    }

    private async Task<MessageAttachment> SeedAttachmentAsync(byte[]? bytes, string provider, string? storageKey)
    {
        using var scope = _unsignedFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attachment = new MessageAttachment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            StorageProvider = provider,
            StorageKey = storageKey,
            ImageData = bytes,
            ContentType = "image/png",
            FileName = "seeded.png",
            SizeBytes = bytes?.LongLength ?? 0,
            Url = "/api/v1/orgs/x/conversations/y/attachments/z",
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.MessageAttachments.Add(attachment);
        await context.SaveChangesAsync();
        return attachment;
    }

    private async Task<InventoryImage> SeedInventoryImageAsync(string provider, string storageKey)
    {
        using var scope = _unsignedFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var image = new InventoryImage
        {
            Id = Guid.CreateVersion7(),
            OrgId = Guid.NewGuid(),
            StorageProvider = provider,
            StorageKey = storageKey,
            ImageData = null,
            ContentType = "image/jpeg",
            FileName = "seeded.jpg",
            FileSizeBytes = 4,
            ImageUrl = "https://res.cloudinary.com/a-cloud/image/upload/w_800,f_auto,q_auto/v1/x",
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.InventoryImages.Add(image);
        await context.SaveChangesAsync();
        return image;
    }

    private async Task DeleteAttachmentAsync(Guid attachmentId)
    {
        using var scope = _unsignedFactory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await context.MessageAttachments.SingleAsync(a => a.Id == attachmentId);
        context.MessageAttachments.Remove(row);
        await context.SaveChangesAsync();
    }

    private static string AssetKeyFor(MessageAttachment attachment)
        => string.IsNullOrWhiteSpace(attachment.StorageKey)
            ? attachment.Id.ToString()
            : attachment.StorageKey!;

    private static string CloudinaryKey(Guid organizationId)
        => $"image/authenticated:aveline/{organizationId}/conversations/{Guid.NewGuid()}";

    // --- The Clerk-authenticated conversation flow (mirrors the shipped conversation tests) ---

    private string CreateToken(string userId, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null)
        {
            claims.Add(new Claim("email", email));
        }

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_rsaKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }

    private async Task<(User owner, Organization org)> SeedActiveOwnerAsync(string clerkId, string slug)
    {
        await using var context = CreateContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Owner",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = slug.Replace('-', ' '),
            Slug = slug,
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return (owner, org);
    }

    private async Task<Guid> CreateConversationAsync(Guid organizationId, string token)
    {
        var create = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{organizationId}/conversations", token, new { customerId = (Guid?)null }));
        create.StatusCode.Should().Be(HttpStatusCode.OK);
        var conversation = await create.Content.ReadFromJsonAsync<ConversationResponse>();
        return conversation!.Id;
    }

    private async Task<Guid> UploadAttachmentAsync(Guid organizationId, Guid conversationId, string token)
    {
        var upload = await _client.SendAsync(AuthorizedJson(HttpMethod.Post,
            $"/api/v1/orgs/{organizationId}/conversations/{conversationId}/attachments", token,
            new { imageData = Convert.ToBase64String(TinyPng), fileName = "photo.png" }));
        upload.StatusCode.Should().Be(HttpStatusCode.OK);
        var attachment = await upload.Content.ReadFromJsonAsync<AttachmentResponse>();
        return attachment!.AttachmentId;
    }

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };

    private sealed record ConversationResponse(Guid Id, string Kind, Guid? CustomerId, string ThreadId);

    private sealed record AttachmentResponse(Guid AttachmentId, string Url, string ContentType, string FileName, long SizeBytes);

    private sealed record MintResponse(string Token, string Url, string PublicId, DateTimeOffset ExpiresAtUtc);

    private sealed class MutableClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class RecordingNonceStore : IMediaTokenNonceStore
    {
        private readonly HashSet<string> _claimed = new(StringComparer.Ordinal);

        public bool Unavailable { get; set; }

        public Task<MediaNonceClaim> TryClaimAsync(string nonce, TimeSpan ttl, CancellationToken ct = default)
        {
            if (Unavailable)
            {
                return Task.FromResult(MediaNonceClaim.Unavailable);
            }

            return Task.FromResult(_claimed.Add(nonce) ? MediaNonceClaim.Claimed : MediaNonceClaim.AlreadyClaimed);
        }
    }

    private sealed class ScriptedMediaStorage : IMediaStorage
    {
        public Func<StoredMediaRef, Stream?>? OnOpen { get; set; }

        public Exception? OpenException { get; set; }

        public List<StoredMediaRef> Opens { get; } = [];

        public string Provider => "cloudinary";

        public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
            => throw new NotSupportedException("the token route never writes bytes");

        public Task<Stream?> OpenReadAsync(StoredMediaRef reference, CancellationToken ct = default)
        {
            Opens.Add(reference);
            return OpenException is not null
                ? Task.FromException<Stream?>(OpenException)
                : Task.FromResult(OnOpen?.Invoke(reference));
        }

        public Task DeleteAsync(StoredMediaRef reference, CancellationToken ct = default) => Task.CompletedTask;

        public string PublicDeliveryUrl(StoredMediaRef reference) => string.Empty;

        public string SignedDeliveryUrl(StoredMediaRef reference, DateTimeOffset expiresAtUtc) => string.Empty;
    }

    private sealed class LogCollector : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Collector(this);

        public void Dispose()
        {
        }

        private sealed class Collector(LogCollector owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._messages)
                {
                    owner._messages.Add(formatter(state, exception));
                }
            }
        }
    }
}
