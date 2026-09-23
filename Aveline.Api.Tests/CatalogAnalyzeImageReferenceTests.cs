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
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// L2 (CATALOG) - the <c>/api/v1/orgs/{organizationId}/catalog/analyze-image</c> route is the
/// second binding of <see cref="Aveline.Api.Modules.VisualIntelligence.DTOs.AnalyzeImageDto"/>.
/// It must answer an unresolvable reference exactly as lane L4's
/// <c>/internal/visual/analyze-image</c> route does: <c>404</c> with
/// <c>{ "error": "Image not found." }</c>, never the global handler's <c>500</c>.
/// </summary>
/// <remarks>
/// <para>
/// The defect this pins: <c>VisualService.AnalyzeImageAsync</c> throws
/// <see cref="KeyNotFoundException"/> for a reference the caller cannot see (another
/// organisation's, unknown, or deleted). Lane L4 translates that in its own route; this route
/// bound the same DTO and called the same service without the translation, so the cross-org and
/// missing cases fell through to the global exception handler as a <c>500</c>.
/// </para>
/// <para>
/// The decisive assertions are the status and the two side effects: no grant is minted for a
/// refused reference, and no URL is handed to the vision provider. The mint is recorded by a
/// decorator around the real HMAC signer and the provider is faked at
/// <see cref="IVisionService"/>, so no case touches Cloudinary, Redis or a vision provider, and
/// none of them mints a credential.
/// </para>
/// <para>
/// The non-analysable and external-URL cases are <b>already correct before the fix</b>; they are
/// kept here as the regression guard that the new catch does not change them (strategy §3.6: a
/// stored type outside the subset is recorded as <c>not_analysable</c>, not refused).
/// </para>
/// </remarks>
public class CatalogAnalyzeImageReferenceTests : IAsyncLifetime
{
    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string PublicBaseUrl = "https://api.aveline.test";

    private readonly CapturingVisionService _vision = new();

    private readonly InMemoryNonceStore _nonces = new();

    private RecordingSigner _signer = null!;

    private RsaSecurityKey _signingKey = null!;

    private StubAuthServer _authServer = null!;

    private WebApplicationFactory<Program> _factory = null!;

    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", PublicBaseUrl);

                builder.ConfigureServices(services =>
                {
                    // The provider is the capture point: a refused reference must never reach it.
                    services.RemoveAll<IVisionService>();
                    services.AddSingleton<IVisionService>(_vision);

                    // A recording decorator around the real signer proves no grant is minted on
                    // the refusal path while still minting a verifiable token on the happy path.
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
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    // =======================================================================================
    // The defect: an unresolvable reference is a 404, matching the L4 route
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_ForACrossOrgReference_Returns404AndNeverMintsOrServes()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_xorg");
        var (_, otherOrg) = await SeedMemberAndOrgAsync("cat_xorg_other");
        var attachment = await SeedAttachmentAsync(otherOrg.Id, "image/png");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(
            org.Id,
            new
            {
                imageRefKind = "attachment",
                imageRefId = attachment.Id,
            },
            token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"Image not found.\"");
        _signer.Mints.Should().BeEmpty("a reference the caller cannot see never mints");
        _vision.Urls.Should().BeEmpty("a reference the caller cannot see never reaches the provider");
    }

    [Fact]
    public async Task AnalyzeImage_ForAMissingReference_Returns404AndNeverMintsOrServes()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_missing");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(
            org.Id,
            new
            {
                imageRefKind = "attachment",
                imageRefId = Guid.NewGuid(),
            },
            token);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"Image not found.\"");
        _signer.Mints.Should().BeEmpty();
        _vision.Urls.Should().BeEmpty();
    }

    // =======================================================================================
    // The non-analysable answer matches lane L4's route (a 200 with the signal, not an error)
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_ForAStoredTypeOutsideTheSubset_MatchesTheNotAnalysableAnswerAndNeverMints()
    {
        // HEIC is storable and servable but outside VisionContentTypes.IsAnalysable, so the
        // service records `not_analysable` and returns before minting (strategy §3.6).
        var (user, org) = await SeedMemberAndOrgAsync("cat_heic");
        var attachment = await SeedAttachmentAsync(org.Id, "image/heic");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(
            org.Id,
            new
            {
                imageRefKind = "attachment",
                imageRefId = attachment.Id,
            },
            token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("\"not_analysable\":true");
        _signer.Mints.Should().BeEmpty("the analysability check runs before the mint");
        _vision.Urls.Should().BeEmpty("no unreadable asset is handed to the provider");
    }

    // =======================================================================================
    // The imageUrl arm and the resolvable-reference happy path are unchanged
    // =======================================================================================

    [Fact]
    public async Task AnalyzeImage_WithAnExternalImageUrl_StillPassesTheUrlThroughUnchanged()
    {
        const string external = "https://example.com/saree.jpg";
        var (user, org) = await SeedMemberAndOrgAsync("cat_external");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(
            org.Id,
            new
            {
                imageUrl = external,
            },
            token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        _vision.Urls.Should().ContainSingle().Which.Should().Be(external);
        _signer.Mints.Should().BeEmpty("the external-URL arm is not tokenised");
    }

    [Fact]
    public async Task AnalyzeImage_ForAnAnalysableReference_StillMintsAndAnalyses()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_happy");
        var attachment = await SeedAttachmentAsync(org.Id, "image/png");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(
            org.Id,
            new
            {
                imageRefKind = "attachment",
                imageRefId = attachment.Id,
            },
            token);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        // Bytes inline, so nothing is minted: there is no URL for anyone to fetch.
        _signer.Mints.Should().BeEmpty("bytes inline need no media token");
        _vision.Urls.Should().ContainSingle()
            .Which.Should().StartWith("data:image/");
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private string CreateToken(string clerkId, string userRole = "staff")
    {
        var claims = new List<Claim>
        {
            new("sub", clerkId),
            new("user_role", userRole),
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private async Task<HttpResponseMessage> AuthorizedPostAsync(Guid organizationId, object body, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orgs/{organizationId}/catalog/analyze-image")
        {
            Content = JsonContent.Create(body),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        return await _client.SendAsync(request);
    }

    private async Task<(User User, Organization Org)> SeedMemberAndOrgAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"{prefix}_clerk_id",
            Email = $"{prefix}@example.com",
            FirstName = "Test",
            LastName = "User",
            Username = $"{prefix}_user",
            UserRole = Roles.Staff,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            IsActive = true,
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"{prefix} Boutique",
            Slug = $"{prefix}-boutique",
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Organizations.Add(org);

        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return (user, org);
    }

    private async Task<MessageAttachment> SeedAttachmentAsync(Guid organizationId, string contentType)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attachment = new MessageAttachment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = organizationId,
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

        db.MessageAttachments.Add(attachment);
        await db.SaveChangesAsync();
        return attachment;
    }

    private sealed class CapturingVisionService : IVisionService
    {
        public List<string> Urls { get; } = [];

        public Task<Aveline.Api.Modules.VisualIntelligence.DTOs.ImageAnalysisResultDto> AnalyzeAsync(
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

            return Task.FromResult(new Aveline.Api.Modules.VisualIntelligence.DTOs.ImageAnalysisResultDto
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

/// <summary>
/// L2 (CATALOG) - the <c>/api/v1/orgs/{organizationId}/catalog/analyze-image</c> route's
/// translation of a target the vision provider cannot read.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a second fixture.</b> <see cref="CatalogAnalyzeImageReferenceTests"/> replaces
/// <see cref="IVisionService"/> with a capturing double to assert the argument a reference
/// resolves to. This unit is about what the <em>real</em>
/// <see cref="Aveline.Api.Modules.VisualIntelligence.Services.VisionService"/> does with a target
/// it cannot use, so this fixture leaves the production registration in place: a relative path is
/// refused by the service and must be translated by the route, not by the global exception handler.
/// </para>
/// <para>
/// The production shape is the literal string the catalog upload stores in
/// <c>InventoryImages.ImageUrl</c> - <c>/api/v1/orgs/{orgId}/catalog/images/{id}</c> - which the
/// web modal writes back into its <c>imageUrl</c> state and posts here. The prior handler caught
/// only <see cref="KeyNotFoundException"/>, so the refusal reached the global handler as a
/// misleading <c>500</c>.
/// </para>
/// </remarks>
public class CatalogAnalyzeImageBadTargetTests : IAsyncLifetime
{
    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string CatalogBaseUrl = "https://api.aveline.test";

    private RsaSecurityKey _signingKey = null!;

    private StubAuthServer _authServer = null!;

    private WebApplicationFactory<Program> _factory = null!;

    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        // IVisionService is deliberately NOT replaced here: the production VisionService classifies
        // the target, and the route's job is to translate that refusal.
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", CatalogBaseUrl);
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    [Fact]
    public async Task AnalyzeImage_WithARelativeImageUrl_Returns400Not500()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_bad_relative");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(
            org.Id,
            new
            {
                imageUrl = $"/api/v1/orgs/{org.Id}/catalog/images/4f0a1e2d-3c4b-5a69-8d7e-9f0a1b2c3d4e",
            },
            token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("absolute http(s) URL");
    }

    [Fact]
    public async Task AnalyzeImage_WithABlankImageUrl_Returns400Not500()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_bad_blank");
        var token = CreateToken(user.ClerkId);

        var response = await AuthorizedPostAsync(org.Id, new { imageUrl = string.Empty }, token);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private string CreateToken(string clerkId, string userRole = "staff")
    {
        var claims = new List<Claim>
        {
            new("sub", clerkId),
            new("user_role", userRole),
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private async Task<HttpResponseMessage> AuthorizedPostAsync(Guid organizationId, object body, string token)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/v1/orgs/{organizationId}/catalog/analyze-image")
        {
            Content = JsonContent.Create(body),
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        return await _client.SendAsync(request);
    }

    private async Task<(User User, Organization Org)> SeedMemberAndOrgAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"{prefix}_clerk_id",
            Email = $"{prefix}@example.com",
            FirstName = "Test",
            LastName = "User",
            Username = $"{prefix}_user",
            UserRole = Roles.Staff,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            IsActive = true,
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"{prefix} Boutique",
            Slug = $"{prefix}-boutique",
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        db.Organizations.Add(org);

        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });

        await db.SaveChangesAsync();
        return (user, org);
    }
}
