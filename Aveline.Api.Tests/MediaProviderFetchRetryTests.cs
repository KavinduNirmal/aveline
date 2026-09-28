using Aveline.Api.Modules.Media;
using CloudinaryDotNet.Actions;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.1 (lane L1) — the retry on the proxy <em>read</em>. Cloudinary's rate-limit code is
/// <c>420</c>, so a proxy fetch retries with the same bounded backoff an upload uses and only
/// surfaces <c>420</c> when it persists, which is what makes the route answer <c>503</c>
/// (migration plan §7.7).
/// </summary>
public class MediaProviderFetchRetryTests
{
    private const string Key =
        "image/authenticated:aveline/11111111-1111-1111-1111-111111111111/conversations/22222222-2222-2222-2222-222222222222";

    [Fact]
    public async Task OpenReadAsync_RetriesA420ThenSucceeds()
    {
        var gateway = new ScriptedGateway(
            FetchOutcome.Status(420),
            FetchOutcome.Status(420),
            FetchOutcome.Body([9, 9, 9]));
        var delay = new RecordingDelay();
        var storage = Storage(gateway, delay);

        var stream = await storage.OpenReadAsync(new StoredMediaRef("cloudinary", Key, "image/jpeg"));

        stream.Should().NotBeNull();
        gateway.FetchCount.Should().Be(3);
        delay.Delays.Should().HaveCount(2);
        delay.Delays.Should().OnlyContain(d => d > TimeSpan.Zero && d <= TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task OpenReadAsync_WhenA420Persists_ExhaustsTheBudgetAndSurfaces420()
    {
        var gateway = new ScriptedGateway(
            FetchOutcome.Status(420),
            FetchOutcome.Status(420),
            FetchOutcome.Status(420),
            FetchOutcome.Status(420));
        var delay = new RecordingDelay();
        var storage = Storage(gateway, delay);

        var fetch = async () => await storage.OpenReadAsync(new StoredMediaRef("cloudinary", Key, "image/jpeg"));

        var thrown = await fetch.Should().ThrowAsync<MediaStorageException>();
        thrown.Which.StatusCode.Should().Be(420);
        gateway.FetchCount.Should().Be(4, "one attempt plus the three configured retries");
    }

    [Fact]
    public async Task OpenReadAsync_OnAProviderDenial_DoesNotRetry()
    {
        var gateway = new ScriptedGateway(FetchOutcome.Status(403));
        var delay = new RecordingDelay();
        var storage = Storage(gateway, delay);

        var fetch = async () => await storage.OpenReadAsync(new StoredMediaRef("cloudinary", Key, "image/jpeg"));

        var thrown = await fetch.Should().ThrowAsync<MediaStorageException>();
        thrown.Which.StatusCode.Should().Be(403);
        gateway.FetchCount.Should().Be(1, "a denial is the provider's final answer");
        delay.Delays.Should().BeEmpty();
    }

    private static CloudinaryMediaStorage Storage(ICloudinaryGateway gateway, IMediaRetryDelay delay)
    {
        var options = new CloudinaryOptions
        {
            CloudName = "a-cloud",
            ApiKey = "test-key",
            ApiSecret = "test-secret",
            UploadRetryAttempts = 3,
        };

        return new CloudinaryMediaStorage(
            gateway,
            Microsoft.Extensions.Options.Options.Create(options),
            new CloudinaryRetryPolicy(options, NullLogger.Instance, delay),
            NullLogger<CloudinaryMediaStorage>.Instance);
    }

    /// <summary>One scripted fetch answer: a status to surface, or bytes to hand back.</summary>
    private sealed record FetchOutcome(int? StatusCode, byte[]? Bytes)
    {
        public static FetchOutcome Status(int statusCode) => new(statusCode, null);

        public static FetchOutcome Body(byte[] bytes) => new(null, bytes);
    }

    private sealed class ScriptedGateway(params FetchOutcome[] outcomes) : ICloudinaryGateway
    {
        private int _index;

        public int FetchCount { get; private set; }

        public bool Secure => true;

        public Task<CloudinaryAttemptResult> UploadImageAsync(ImageUploadParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryAttemptResult> UploadRawAsync(RawUploadParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryAttemptResult> DestroyAsync(DeletionParams parameters, CancellationToken ct)
            => throw new NotSupportedException();

        public Task<CloudinaryResourceInfo?> GetResourceAsync(
            string publicId, string resourceType, string deliveryType, CancellationToken ct)
            => throw new NotSupportedException();

        public string BuildDeliveryUrl(string publicId, string resourceType, string deliveryType, bool signed)
            => $"https://res.cloudinary.com/a-cloud/{resourceType}/{deliveryType}/{publicId}";

        public string BuildPrivateDownloadUrl(
            string publicId, string resourceType, string deliveryType, DateTimeOffset expiresAtUtc)
            => $"https://api.cloudinary.com/v1_1/a-cloud/{resourceType}/download?public_id={publicId}";

        public Task<Stream> FetchAsync(string url, CancellationToken ct)
        {
            FetchCount++;
            var outcome = outcomes[Math.Min(_index++, outcomes.Length - 1)];
            return outcome.StatusCode is { } status
                ? Task.FromException<Stream>(new MediaStorageException(status, $"provider returned {status}"))
                : Task.FromResult<Stream>(new MemoryStream(outcome.Bytes!, writable: false));
        }
    }

    private sealed class RecordingDelay : IMediaRetryDelay
    {
        public List<TimeSpan> Delays { get; } = [];

        public Task DelayAsync(TimeSpan delay, CancellationToken ct)
        {
            Delays.Add(delay);
            return Task.CompletedTask;
        }
    }
}
