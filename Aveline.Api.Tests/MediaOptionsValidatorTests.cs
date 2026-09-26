using Aveline.Api.Modules.Media;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Moq;

namespace Aveline.Api.Tests;

/// <summary>
/// S0 / U0.1 — the frozen media configuration surface (strategy §3.4).
/// <para>
/// Every refusal condition in that table's <em>Fail-fast rule at startup</em> column is
/// asserted here, together with the two positives the deployment depends on: the safe default
/// (<c>Media:Provider=database</c>) boots anywhere, and the explicitly approved Production
/// override (<c>Media:AllowDatabaseProviderInProduction=true</c>) boots once and logs at
/// <see cref="LogLevel.Warning"/>.
/// </para>
/// </summary>
public class MediaOptionsValidatorTests
{
    private static readonly string ValidSigningKey = Convert.ToBase64String(new byte[32]);

    // ── The safe default and the approved override ────────────────────────────────────────

    [Fact]
    public void DatabaseProvider_StartsCleanly()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(("Media:Provider", "database")),
            HostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    [Fact]
    public void DatabaseProvider_OnANonProductionHost_StartsCleanly()
    {
        // The refusal is Production-specific; Staging must not need the escape hatch.
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(("Media:Provider", "database")),
            HostEnvironment("Staging"));

        act.Should().NotThrow();
    }

    [Fact]
    public void ProductionDatabaseProvider_WithoutTheOverride_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(("Media:Provider", "database")),
            HostEnvironment(Environments.Production));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:AllowDatabaseProviderInProduction*");
    }

    [Fact]
    public void ProductionDatabaseProvider_WithTheOverride_StartsAndLogsAtWarning()
    {
        var logger = new CapturingLogger();

        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(
                ("Media:Provider", "database"),
                ("Media:AllowDatabaseProviderInProduction", "true")),
            HostEnvironment(Environments.Production),
            logger);

        act.Should().NotThrow();
        logger.Entries.Should().ContainSingle(entry =>
            entry.Level == LogLevel.Warning
            && entry.Message.Contains("AllowDatabaseProviderInProduction", StringComparison.Ordinal));
    }

    // ── Credentials: the primary URL, and the discrete fallback ───────────────────────────

    [Fact]
    public void CloudinaryProvider_WithoutAnyCredential_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(("CLOUDINARY_URL", null)),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CLOUDINARY_URL*");
    }

    [Fact]
    public void CloudinaryProvider_WithOnlyTheApiKey_IsRefused()
    {
        // "Both or neither": a key without its secret is a half-configured provider.
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(
                ("CLOUDINARY_URL", null),
                ("CLOUDINARY_API_KEY", "an-api-key"),
                ("CLOUDINARY_API_SECRET", null)),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CLOUDINARY_API_KEY*");
    }

    [Fact]
    public void CloudinaryProvider_WithAMalformedUrl_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(("CLOUDINARY_URL", "https://not-cloudinary.example")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CLOUDINARY_URL*");
    }

    [Fact]
    public void CloudinaryProvider_WithTheDiscreteKeys_StartsCleanly()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(
                ("CLOUDINARY_URL", null),
                ("CLOUDINARY_API_KEY", "an-api-key"),
                ("CLOUDINARY_API_SECRET", "an-api-secret"),
                ("CLOUDINARY_CLOUD_NAME", "a-cloud-name")),
            HostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    [Fact]
    public void CloudinaryProvider_WithTheDiscreteKeysButNoCloudName_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(
                ("CLOUDINARY_URL", null),
                ("CLOUDINARY_API_KEY", "an-api-key"),
                ("CLOUDINARY_API_SECRET", "an-api-secret"),
                ("CLOUDINARY_CLOUD_NAME", null)),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*CloudName*");
    }

    // ── Signing key ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void CloudinaryProvider_WithoutASigningKey_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(("Media:SigningKey", "")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:SigningKey*");
    }

    [Fact]
    public void CloudinaryProvider_WithASigningKeyThatIsNot32Bytes_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(
                ("Media:SigningKey", Convert.ToBase64String(new byte[16]))),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:SigningKey*");
    }

    [Fact]
    public void CloudinaryProvider_WithANonBase64SigningKey_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(("Media:SigningKey", "not-base64!!")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:SigningKey*");
    }

    [Fact]
    public void DatabaseProvider_DoesNotRequireASigningKey()
    {
        // The signing key is only needed once tokens can be minted.
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(("Media:Provider", "database"), ("Media:SigningKey", "")),
            HostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    // ── Public base URL ───────────────────────────────────────────────────────────────────

    [Fact]
    public void CloudinaryProvider_WithABlankPublicBaseUrl_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(("Media:PublicBaseUrl", "")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:PublicBaseUrl*");
    }

    [Fact]
    public void CloudinaryProvider_WithARelativePublicBaseUrl_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(("Media:PublicBaseUrl", "/api")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:PublicBaseUrl*");
    }

    [Fact]
    public void CloudinaryProvider_WithACompletelyConfiguredSurface_StartsCleanly()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            CompleteCloudinaryConfig(),
            HostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    // ── The provider-neutral fail-fast rules (checked regardless of provider) ──────────────

    [Fact]
    public void AMalformedImageUrlAllowlistEntry_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(
                ("Media:Provider", "database"),
                ("Media:ImageUrlAllowlist", "example.com, https://malformed.example")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:ImageUrlAllowlist*");
    }

    [Fact]
    public void AWellFormedImageUrlAllowlist_StartsCleanly()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(
                ("Media:Provider", "database"),
                ("Media:ImageUrlAllowlist", "images.example.com, cdn.example.net")),
            HostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    [Fact]
    public void AZeroCatalogDisplayWidth_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(
                ("Media:Provider", "database"),
                ("Media:CatalogDisplayWidth", "0")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Media:CatalogDisplayWidth*");
    }

    [Fact]
    public void ANonPositiveAttachmentRetentionWindow_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(
                ("Media:Provider", "database"),
                ("Conversations:AttachmentRetentionDays", "0")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Conversations:AttachmentRetentionDays*");
    }

    [Fact]
    public void ANegativeAttachmentRetentionWindow_IsRefused()
    {
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(
                ("Media:Provider", "database"),
                ("Conversations:AttachmentRetentionDays", "-1")),
            HostEnvironment(Environments.Development));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Conversations:AttachmentRetentionDays*");
    }

    [Fact]
    public void AnAbsentAttachmentRetentionWindow_StartsCleanly()
    {
        // Absent means the documented default (7), not a refusal.
        var act = () => MediaOptionsValidator.ValidateOrThrow(
            Configuration(("Media:Provider", "database")),
            HostEnvironment(Environments.Development));

        act.Should().NotThrow();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────

    private static (string Key, string? Value)[] CompleteCloudinary()
    {
        return
        [
            ("Media:Provider", "cloudinary"),
            ("CLOUDINARY_URL", "cloudinary://an-api-key:an-api-secret@a-cloud-name"),
            ("Media:SigningKey", ValidSigningKey),
            ("Media:PublicBaseUrl", "https://api.aveline.example"),
        ];
    }

    private static IConfiguration CompleteCloudinaryConfig(
        params (string Key, string? Value)[] overrides)
    {
        var settings = CompleteCloudinary()
            .Where(setting => overrides.All(o => o.Key != setting.Key))
            .Concat(overrides);

        return Configuration(settings.ToArray());
    }

    private static IConfiguration Configuration(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();

    private static IHostEnvironment HostEnvironment(string name)
    {
        var environment = new Mock<IHostEnvironment>();
        environment.SetupGet(e => e.EnvironmentName).Returns(name);
        return environment.Object;
    }

    private sealed class CapturingLogger : ILogger
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
            => Entries.Add((logLevel, formatter(state, exception)));
    }
}

/// <summary>
/// S0 / U0.1 — <c>CLOUDINARY_URL</c> is the SDK convention
/// <c>cloudinary://&lt;key&gt;:&lt;secret&gt;@&lt;cloud_name&gt;</c>, parsed explicitly so the value
/// is a testable input rather than the SDK's ambient global read (strategy §3.4).
/// </summary>
public class CloudinaryUrlParserTests
{
    [Fact]
    public void ParsesTheSdkConventionUrl()
    {
        var parsed = CloudinaryUrlParser.TryParse(
            "cloudinary://123456789012345:the-secret@a-cloud-name", out var credentials, out var error);

        parsed.Should().BeTrue();
        error.Should().BeNull();
        credentials.CloudName.Should().Be("a-cloud-name");
        credentials.ApiKey.Should().Be("123456789012345");
        credentials.ApiSecret.Should().Be("the-secret");
    }

    [Fact]
    public void KeepsASecretContainingTheAtSign()
    {
        // Splitting on the last '@' keeps a secret that contains one intact.
        var parsed = CloudinaryUrlParser.TryParse(
            "cloudinary://key:sec@ret@cloud", out var credentials, out _);

        parsed.Should().BeTrue();
        credentials.ApiSecret.Should().Be("sec@ret");
        credentials.CloudName.Should().Be("cloud");
    }

    [Fact]
    public void IsCaseInsensitiveOnTheScheme()
    {
        var parsed = CloudinaryUrlParser.TryParse(
            "CLOUDINARY://key:secret@cloud", out var credentials, out _);

        parsed.Should().BeTrue();
        credentials.CloudName.Should().Be("cloud");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://key:secret@cloud")]
    [InlineData("cloudinary:/key:secret@cloud")]
    [InlineData("cloudinary://key:secret")]
    [InlineData("cloudinary://key@cloud")]
    [InlineData("cloudinary://key:@cloud")]
    [InlineData("cloudinary://:secret@cloud")]
    [InlineData("cloudinary://key:secret@")]
    public void RefusesAnythingThatIsNotTheConvention(string? value)
    {
        var parsed = CloudinaryUrlParser.TryParse(value, out _, out var error);

        parsed.Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void ResolvePrefersTheUrlOverTheDiscreteKeys()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLOUDINARY_URL"] = "cloudinary://url-key:url-secret@url-cloud",
                ["CLOUDINARY_API_KEY"] = "discrete-key",
                ["CLOUDINARY_API_SECRET"] = "discrete-secret",
                ["CLOUDINARY_CLOUD_NAME"] = "discrete-cloud",
            })
            .Build();

        var resolved = CloudinaryUrlParser.TryResolve(configuration, out var credentials, out _);

        resolved.Should().BeTrue();
        credentials.CloudName.Should().Be("url-cloud");
        credentials.ApiKey.Should().Be("url-key");
        credentials.ApiSecret.Should().Be("url-secret");
    }

    [Fact]
    public void ResolveUsesTheDiscreteFallbackWhenTheUrlIsAbsent()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLOUDINARY_API_KEY"] = "discrete-key",
                ["CLOUDINARY_API_SECRET"] = "discrete-secret",
                ["CLOUDINARY_CLOUD_NAME"] = "discrete-cloud",
            })
            .Build();

        var resolved = CloudinaryUrlParser.TryResolve(configuration, out var credentials, out _);

        resolved.Should().BeTrue();
        credentials.CloudName.Should().Be("discrete-cloud");
        credentials.ApiKey.Should().Be("discrete-key");
        credentials.ApiSecret.Should().Be("discrete-secret");
    }

    [Fact]
    public void ResolveReadsNothingWhenNoCredentialExists()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection([]).Build();

        var resolved = CloudinaryUrlParser.TryResolve(configuration, out _, out _);

        resolved.Should().BeFalse();
    }

    [Fact]
    public void ResolveDoesNotFallBackWhenTheUrlIsPresentButMalformed()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CLOUDINARY_URL"] = "https://not-cloudinary.example",
                ["CLOUDINARY_API_KEY"] = "discrete-key",
                ["CLOUDINARY_API_SECRET"] = "discrete-secret",
            })
            .Build();

        var resolved = CloudinaryUrlParser.TryResolve(configuration, out _, out var error);

        resolved.Should().BeFalse();
        error.Should().Contain("CLOUDINARY_URL");
    }
}

/// <summary>
/// S0 / U0.1 — the documented defaults from strategy §3.4, which the shipped
/// <c>appsettings.json</c> must match. The load-bearing ones are called out by name.
/// </summary>
public class MediaOptionsDefaultsTests
{
    [Fact]
    public void MediaOptionsCarryTheFrozenDefaults()
    {
        var options = new MediaOptions();

        options.Provider.Should().Be(MediaProvider.Database);
        options.ReadFromCloudinary.Should().BeFalse();
        options.DualWrite.Should().BeFalse();
        options.SigningKey.Should().BeEmpty();
        options.PublicBaseUrl.Should().BeEmpty();
        options.AllowDatabaseProviderInProduction.Should().BeFalse();
        options.VisionTokenTtlSeconds.Should().Be(600);
        options.AttachmentTokenTtlSeconds.Should().Be(900);
        options.ClockSkewToleranceSeconds.Should().Be(30);
        options.VisionUsePrivateDownload.Should().BeFalse();
        options.ImageUrlUploadEnabled.Should().BeFalse();
        options.ImageUrlAllowlist.Should().BeEmpty();
        options.ImageUrlMaxRedirects.Should().Be(2);
        options.ImageUrlFetchTimeoutSeconds.Should().Be(10);
        options.AllowInsecureImageFetch.Should().BeFalse();
        options.CatalogMaxFileBytes.Should().Be(2 * 1024 * 1024);
        options.CatalogDisplayWidth.Should().Be(800);
    }

    [Fact]
    public void CloudinaryOptionsCarryTheFrozenDefaults()
    {
        var options = new CloudinaryOptions();

        options.FolderRoot.Should().Be("aveline");
        options.CatalogDeliveryType.Should().Be("upload");
        options.ProtectedDeliveryType.Should().Be("authenticated");
        options.UploadTimeoutSeconds.Should().Be(30);
        options.MaxConcurrentUploads.Should().Be(10);
        options.UploadRetryAttempts.Should().Be(3);
    }

    [Fact]
    public void MediaTierHasNoThirdValue()
    {
        // A PDF is Protected with kind:pdf; the tier is about who may read (strategy §3.2).
        Enum.GetValues<MediaTier>().Should().BeEquivalentTo([MediaTier.Public, MediaTier.Protected]);
    }

    [Fact]
    public void MediaScopeNamesTheThreeDocumentedScopes()
    {
        Enum.GetValues<MediaScope>().Should().BeEquivalentTo(
            [MediaScope.AssetPublic, MediaScope.AttachmentView, MediaScope.VisionAnalyze]);
    }

    [Fact]
    public void MediaPutRequestMetadataIsOptional()
    {
        // A caller with nothing to say must not be forced to invent labels (strategy §3.2).
        var request = new MediaPutRequest(
            "aveline/org/catalog/image", [1, 2, 3], "image/jpeg", "piece.jpg", MediaTier.Public);

        request.Metadata.Should().BeNull();
    }

    [Fact]
    public void MediaMetadataUsesProviderNeutralFieldNames()
    {
        // Nothing about the record names Cloudinary (strategy §3.2).
        var metadata = typeof(MediaMetadata);

        metadata.GetProperty("Labels").Should().NotBeNull();
        metadata.GetProperty("Attributes").Should().NotBeNull();
        metadata.GetProperties().Should().OnlyContain(property => property.Name != "Tags");
        metadata.GetProperties().Should().OnlyContain(property => property.Name != "Context");
    }
}
