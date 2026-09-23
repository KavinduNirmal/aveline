using Aveline.Api.Modules.Media;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.1 (lane L1) — <see cref="MediaAccessService"/>: the server-side resolution of a verified
/// token. Two properties carry this file: the per-row dispatch of strategy §3.3 (a Cloudinary row
/// is read from the provider <em>only</em> while <c>Media:ReadFromCloudinary</c> is on, and the
/// rollback branch reads the row's bytes without touching the provider), and the fixed status
/// mapping of migration plan §7.7.
/// </summary>
public class MediaAccessServiceTests
{
    private static readonly Guid Org = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly DateTimeOffset Now = DateTimeOffset.FromUnixTimeSeconds(1_700_000_000);

    private const string SigningKey =
        "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string CloudinaryKey =
        "image/authenticated:aveline/11111111-1111-1111-1111-111111111111/conversations/22222222-2222-2222-2222-222222222222";

    // ---------------------------------------------------------------------------------------
    // The happy path and the per-row dispatch (strategy §3.3)
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_ForADatabaseRow_ServesTheRowsBytes()
    {
        var bytes = new byte[] { 1, 2, 3, 4 };
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add("row-id", new MediaAccessAsset("row-id", "row-id", "database", null, bytes, "image/png"));
        var token = harness.Mint(Org, "row-id", MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.IsSuccess.Should().BeTrue();
        result.Grant!.ContentType.Should().Be("image/png");
        (await ReadAsync(result.Grant.Content)).Should().Equal(bytes);
        harness.Storage.Opens.Should().BeEmpty("a database row never reaches the provider");
    }

    [Fact]
    public async Task OpenAsync_ForACloudinaryRowWithReadFromCloudinaryOn_StreamsFromTheProvider()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, null, "image/jpeg"));
        harness.Storage.OnOpen = _ => new MemoryStream([9, 8, 7], writable: false);
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.IsSuccess.Should().BeTrue();
        (await ReadAsync(result.Grant!.Content)).Should().Equal(9, 8, 7);
        harness.Storage.Opens.Should().ContainSingle()
            .Which.StorageKey.Should().Be(CloudinaryKey);
    }

    [Fact]
    public async Task OpenAsync_WithReadFromCloudinaryOff_RestoresTheDatabaseReadForARowThatStillHasBytes()
    {
        // The documented rollback (strategy §5.2 stage 2; migration plan §6.3): flipping the flag
        // reads the row's dual-write copy and does not touch the provider.
        var harness = CreateHarness(readFromCloudinary: false);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, [4, 5, 6], "image/jpeg"));
        harness.Storage.OnOpen = _ => new MemoryStream([9, 8, 7], writable: false);
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.IsSuccess.Should().BeTrue();
        (await ReadAsync(result.Grant!.Content)).Should().Equal(4, 5, 6);
        harness.Storage.Opens.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_ForARowWithNeitherBytesNorAKey_IsNotFoundAndNeverAProviderError()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add("orphan", new MediaAccessAsset("orphan", "orphan", "database", null, null, "image/jpeg"));
        var token = harness.Mint(Org, "orphan", MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.NotFound);
        harness.Storage.Opens.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // 404 before any provider call
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_ForAMissingRow_IsNotFoundAndNeverCallsTheProvider()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.NotFound);
        harness.Storage.Opens.Should().BeEmpty();
    }

    [Fact]
    public async Task OpenAsync_WhenTheTokenTenantDoesNotMatchTheRowTenant_IsNotFound()
    {
        // Tenant binding: a token minted for another organisation cannot name this row.
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, null, "image/jpeg"));
        harness.Locator.OrganizationId = Guid.NewGuid();
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.NotFound);
        harness.Storage.Opens.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------
    // Scope, tamper, expiry: the token checks
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_WhenTheRouteRequiresVisionAndTheTokenIsAnAttachmentToken_IsScopeMismatch()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        var token = harness.Mint(Org, "row-id", MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.VisionAnalyze]);

        result.Failure.Should().Be(MediaAccessFailure.ScopeMismatch);
    }

    [Fact]
    public async Task OpenAsync_WhenTheRouteRequiresAttachmentViewAndTheTokenIsAVisionToken_IsScopeMismatch()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        var token = harness.Mint(Org, "row-id", MediaScope.VisionAnalyze);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.ScopeMismatch);
    }

    [Fact]
    public async Task OpenAsync_OnARouteThatServesBothScopes_AcceptsEither()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add("row-id", new MediaAccessAsset("row-id", "row-id", "database", null, [1], "image/png"));

        var attachment = await harness.Service.OpenAsync(
            harness.Mint(Org, "row-id", MediaScope.AttachmentView),
            [MediaScope.AttachmentView, MediaScope.VisionAnalyze]);

        attachment.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task OpenAsync_WithATamperedToken_IsBadSignature()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        var token = harness.Mint(Org, "row-id", MediaScope.AttachmentView);
        var parts = token.Split('.');
        var tampered = $"{parts[0]}.{(parts[1][0] == 'A' ? 'B' : 'A')}{parts[1][1..]}";

        var result = await harness.Service.OpenAsync(tampered, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.BadSignature);
    }

    [Fact]
    public async Task OpenAsync_WithAnExpiredToken_IsExpired()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        var token = harness.Mint(Org, "row-id", MediaScope.AttachmentView);
        harness.Clock.Now = Now.AddSeconds(900 + 30 + 1);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.Expired);
    }

    // ---------------------------------------------------------------------------------------
    // The single-use nonce, and the fail-closed direction
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OpenAsync_ForAReplayedVisionToken_IsReplayedAndNeverCallsTheProvider()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, null, "image/jpeg"));
        harness.Storage.OnOpen = _ => new MemoryStream([7], writable: false);
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.VisionAnalyze);

        var first = await harness.Service.OpenAsync(token, [MediaScope.VisionAnalyze]);
        var second = await harness.Service.OpenAsync(token, [MediaScope.VisionAnalyze]);

        first.IsSuccess.Should().BeTrue();
        second.Failure.Should().Be(MediaAccessFailure.Replayed);
        harness.Storage.Opens.Should().ContainSingle("the replay is refused before the fetch");
    }

    [Fact]
    public async Task OpenAsync_WhenTheNonceStoreIsUnavailable_FailsClosedAndNeverCallsTheProvider()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, null, "image/jpeg"));
        harness.Storage.OnOpen = _ => new MemoryStream([7], writable: false);
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.VisionAnalyze);
        harness.Nonces.Unavailable = true;

        var result = await harness.Service.OpenAsync(token, [MediaScope.VisionAnalyze]);

        result.Failure.Should().Be(MediaAccessFailure.NonceStoreUnavailable);
        harness.Storage.Opens.Should().BeEmpty("no claim means no fetch (strategy §3.8)");
    }

    // ---------------------------------------------------------------------------------------
    // The provider failure mapping (migration plan §7.7)
    // ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(401, MediaAccessFailure.ProviderDenied)]
    [InlineData(403, MediaAccessFailure.ProviderDenied)]
    [InlineData(420, MediaAccessFailure.ProviderRateLimited)]
    [InlineData(500, MediaAccessFailure.ProviderError)]
    public async Task OpenAsync_WhenTheProviderFails_MapsTheStatus(int statusCode, MediaAccessFailure expected)
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, null, "image/jpeg"));
        harness.Storage.OpenException = new MediaStorageException(statusCode, "provider said no");
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(expected);
        result.ProviderDetail.Should().Be("provider said no");
    }

    [Fact]
    public async Task OpenAsync_WhenTheProviderTimesOut_IsProviderTimeout()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, null, "image/jpeg"));
        harness.Storage.OpenException = new MediaStorageException(
            0, "the provider did not answer", new TaskCanceledException("the provider did not answer"));
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.ProviderTimeout);
    }

    [Fact]
    public async Task OpenAsync_WhenTheProviderDenies_DoesNotSubstituteAFallbackImage()
    {
        var harness = CreateHarness(readFromCloudinary: true);
        harness.Locator.Add(CloudinaryKey, new MediaAccessAsset(
            CloudinaryKey, "aveline/org/conversations/attachment", "cloudinary", CloudinaryKey, [1, 2, 3], "image/jpeg"));
        harness.Storage.OpenException = new MediaStorageException(403, "denied");
        var token = harness.Mint(Org, CloudinaryKey, MediaScope.AttachmentView);

        var result = await harness.Service.OpenAsync(token, [MediaScope.AttachmentView]);

        result.Failure.Should().Be(MediaAccessFailure.ProviderDenied);
        result.Grant.Should().BeNull("a denied asset is never silently replaced by the row's copy");
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static Harness CreateHarness(bool readFromCloudinary) => new(readFromCloudinary);

    private static async Task<byte[]> ReadAsync(Stream stream)
    {
        await using (stream)
        {
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            return buffer.ToArray();
        }
    }

    private sealed class Harness
    {
        public Harness(bool readFromCloudinary)
        {
            Clock = new MutableClock(Now);
            Nonces = new RecordingNonceStore();
            Storage = new ScriptedStorage();
            Locator = new FakeAssetLocator { OrganizationId = Org };
            var options = new MediaOptions
            {
                SigningKey = SigningKey,
                PublicBaseUrl = "https://api.aveline.lk",
                ReadFromCloudinary = readFromCloudinary,
                VisionTokenTtlSeconds = 600,
                AttachmentTokenTtlSeconds = 900,
                ClockSkewToleranceSeconds = 30,
            };
            Signer = new HmacMediaUrlSigner(
                Microsoft.Extensions.Options.Options.Create(options), Clock, Nonces,
                () => Guid.NewGuid().ToString("N"));
            Service = new MediaAccessService(
                Signer, Locator, Storage,
                Microsoft.Extensions.Options.Options.Create(options),
                NullLogger<MediaAccessService>.Instance);
        }

        public MutableClock Clock { get; }

        public RecordingNonceStore Nonces { get; }

        public ScriptedStorage Storage { get; }

        public FakeAssetLocator Locator { get; }

        public HmacMediaUrlSigner Signer { get; }

        public MediaAccessService Service { get; }

        public string Mint(Guid organizationId, string assetKey, MediaScope scope)
        {
            var ttl = scope == MediaScope.VisionAnalyze
                ? TimeSpan.FromSeconds(600)
                : TimeSpan.FromSeconds(900);
            return Signer.Mint(new MediaTokenRequest(organizationId, assetKey, scope, ttl, scope == MediaScope.VisionAnalyze), out _);
        }
    }

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

    private sealed class FakeAssetLocator : IMediaAssetLocator
    {
        private readonly Dictionary<string, MediaAccessAsset> _byKey = new(StringComparer.Ordinal);

        public Guid OrganizationId { get; set; }

        public void Add(string assetKey, MediaAccessAsset asset) => _byKey[assetKey] = asset;

        public Task<MediaAccessAsset?> FindByAssetKeyAsync(
            Guid organizationId, string assetKey, CancellationToken ct = default)
            => Task.FromResult(
                organizationId == OrganizationId && _byKey.TryGetValue(assetKey, out var asset) ? asset : null);

        public Task<MediaAccessAsset?> FindByReferenceAsync(
            Guid organizationId, string imageRefKind, Guid imageRefId, CancellationToken ct = default)
            => Task.FromResult<MediaAccessAsset?>(null);
    }

    private sealed class ScriptedStorage : IMediaStorage
    {
        public Func<StoredMediaRef, Stream?>? OnOpen { get; set; }

        public Exception? OpenException { get; set; }

        public List<StoredMediaRef> Opens { get; } = [];

        public string Provider => "cloudinary";

        public Task<StoredMedia> PutAsync(MediaPutRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

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
}
