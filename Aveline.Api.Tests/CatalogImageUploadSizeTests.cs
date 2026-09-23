using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Media;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Unit U0.6 (lane L2) — the catalog tier's per-file cap, applied to <b>both</b> catalog write
/// paths with one behaviour: exactly <c>Media:CatalogMaxFileBytes</c> is accepted, one byte
/// over is refused, and a refused upload writes nothing.
/// </summary>
/// <remarks>
/// <para>
/// The two paths under test are the ones the plan names:
/// <list type="number">
/// <item><c>POST …/catalog/images/upload</c> — the handler in <c>CatalogEndpoints.cs</c>, which
/// has a multipart branch and a base64-JSON branch.</item>
/// <item><c>POST …/catalog/items</c> with a <c>data:</c> image URL — which reaches
/// <c>InventoryService.ProcessImageUrlAsync</c>.</item>
/// </list>
/// </para>
/// <para>
/// The host is configured with a deliberately small 1 KB cap. That proves the behaviour reads
/// the configured key rather than a literal, and keeps the boundary cheap to construct. The
/// repository is a spy: "nothing was written" is asserted directly on it, and a silently
/// swallowed refusal cannot pass the test by writing a row through a real adapter.
/// </para>
/// </remarks>
public class CatalogImageUploadSizeTests : IAsyncLifetime
{
    /// <summary>The test's <c>Media:CatalogMaxFileBytes</c>; small on purpose.</summary>
    private const int CapBytes = 1024;

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;
    private Mock<IInventoryRepository> _repository = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _repository = new Mock<IInventoryRepository>();
        _repository
            .Setup(r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        _repository
            .Setup(r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Media:CatalogMaxFileBytes", CapBytes.ToString());

                // A spy instead of the database adapter, so an over-cap refusal is proven by the
                // store never being called and not merely by a resulting row count.
                builder.ConfigureTestServices(services =>
                {
                    services.RemoveAll<IInventoryRepository>();
                    services.AddSingleton(_repository.Object);
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

    // ---------------------------------------------------------------------------------------
    // Path 1 — the upload handler (CatalogEndpoints.cs), multipart branch
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UploadMultipart_ExactlyAtCap_IsAcceptedAndStored()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_multipart_at");
        var token = CreateToken(user.ClerkId);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, UploadUrl(org.Id), token, Multipart(Bytes(CapBytes))));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // The successful-upload response shape is unchanged: id / url / fileName / size / contentType.
        var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        body.Should().ContainKeys("id", "url", "fileName", "size", "contentType");

        _repository.Verify(
            r => r.AddImageAsync(
                It.Is<InventoryImage>(i => i.ImageData != null && i.ImageData.LongLength == CapBytes),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadMultipart_OneByteOverCap_Returns400AndWritesNothing()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_multipart_over");
        var token = CreateToken(user.ClerkId);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, UploadUrl(org.Id), token, Multipart(Bytes(CapBytes + 1))));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        body.Should().ContainKey("error");

        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await AssertNoImageRowAsync(org.Id);
    }

    // ---------------------------------------------------------------------------------------
    // Path 1 — the upload handler (CatalogEndpoints.cs), base64-JSON branch
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UploadBase64Json_ExactlyAtCap_IsAcceptedAndStored()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_json_at");
        var token = CreateToken(user.ClerkId);

        var payload = new UploadImagePayloadDto
        {
            ImageData = DataUrl(Bytes(CapBytes)),
            FileName = "at-cap.jpg",
            ContentType = "image/jpeg"
        };

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, UploadUrl(org.Id), token, JsonContent.Create(payload)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        body.Should().ContainKeys("id", "url", "fileName", "size", "contentType");

        _repository.Verify(
            r => r.AddImageAsync(
                It.Is<InventoryImage>(i => i.ImageData != null && i.ImageData.LongLength == CapBytes),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task UploadBase64Json_OneByteOverCap_Returns400AndWritesNothing()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_json_over");
        var token = CreateToken(user.ClerkId);

        var payload = new UploadImagePayloadDto
        {
            ImageData = DataUrl(Bytes(CapBytes + 1)),
            FileName = "over-cap.jpg",
            ContentType = "image/jpeg"
        };

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, UploadUrl(org.Id), token, JsonContent.Create(payload)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        body.Should().ContainKey("error");

        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await AssertNoImageRowAsync(org.Id);
    }

    // ---------------------------------------------------------------------------------------
    // Path 2 — InventoryService.ProcessImageUrlAsync, via POST /catalog/items
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task CreateItemWithBase64UrlImage_ExactlyAtCap_StoresImageAndItem()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_items_at");
        var token = CreateToken(user.ClerkId);

        var dto = CreateItemDto(DataUrl(Bytes(CapBytes)));

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, ItemsUrl(org.Id), token, JsonContent.Create(dto)));

        response.StatusCode.Should().Be(HttpStatusCode.Created);

        _repository.Verify(
            r => r.AddImageAsync(
                It.Is<InventoryImage>(i => i.ImageData != null && i.ImageData.LongLength == CapBytes),
                It.IsAny<CancellationToken>()),
            Times.Once);
        _repository.Verify(
            r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CreateItemWithBase64UrlImage_OneByteOverCap_RefusesVisiblyAndWritesNothing()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_items_over");
        var token = CreateToken(user.ClerkId);

        var dto = CreateItemDto(DataUrl(Bytes(CapBytes + 1)));

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post, ItemsUrl(org.Id), token, JsonContent.Create(dto)));

        // The catalog item route maps the service's refusal to the same 400 { error } shape
        // the upload route uses; it must never report the item as created.
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        body.Should().ContainKey("error");

        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await AssertNoImageRowAsync(org.Id);
    }

    // ---------------------------------------------------------------------------------------
    // Path 2 — InventoryService.ProcessImageUrlAsync, via PUT /catalog/items/{id}
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async Task UpdateItemWithBase64UrlImage_OneByteOverCap_Returns400AndWritesNothing()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cap_update_over");
        var token = CreateToken(user.ClerkId);

        _repository
            .Setup(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new InventoryItem { Id = Guid.NewGuid(), OrgId = org.Id, ItemName = "Existing piece" });
        _repository
            .Setup(r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var dto = new UpdateInventoryItemDto { ImageUrl = DataUrl(Bytes(CapBytes + 1)) };

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Put, $"{ItemsUrl(org.Id)}/{Guid.NewGuid()}", token, JsonContent.Create(dto)));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = (await response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        body.Should().ContainKey("error");

        _repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        _repository.Verify(
            r => r.UpdateAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
        await AssertNoImageRowAsync(org.Id);
    }

    // ---------------------------------------------------------------------------------------
    // Helpers
    // ---------------------------------------------------------------------------------------

    private static string UploadUrl(Guid orgId) => $"/api/v1/orgs/{orgId}/catalog/images/upload";

    private static string ItemsUrl(Guid orgId) => $"/api/v1/orgs/{orgId}/catalog/items";

    private static CreateInventoryItemDto CreateItemDto(string imageUrl) => new()
    {
        ItemName = "Cap boundary piece",
        Category = "Sarees",
        Color = "Emerald",
        ImageUrl = imageUrl
    };

    private static byte[] Bytes(int length)
    {
        var bytes = new byte[length];
        for (var i = 0; i < length; i++)
        {
            bytes[i] = (byte)(i % 251);
        }

        return bytes;
    }

    private static string DataUrl(byte[] bytes)
        => "data:image/jpeg;base64," + Convert.ToBase64String(bytes);

    private static MultipartFormDataContent Multipart(byte[] bytes)
    {
        var content = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        content.Add(file, "file", "boundary.jpg");
        return content;
    }

    private async Task AssertNoImageRowAsync(Guid orgId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Belt and braces with the spy: no row exists either.
        (await db.InventoryImages.CountAsync(i => i.OrgId == orgId)).Should().Be(0);
    }

    private string CreateToken(string clerkId, string userRole = "staff")
    {
        var claims = new List<Claim>
        {
            new("sub", clerkId),
            new("user_role", userRole)
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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        if (content != null)
        {
            request.Content = content;
        }

        return request;
    }

    private async Task<(User User, Organization Org)> SeedMemberAndOrgAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = $"{prefix}_clerk_id",
            Email = $"{prefix}@example.com",
            FirstName = "Test",
            LastName = "User",
            Username = $"{prefix}_user",
            UserRole = Roles.Staff,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            IsActive = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = $"{prefix} Boutique",
            Slug = $"{prefix}-boutique",
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Organizations.Add(org);

        db.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        });

        await db.SaveChangesAsync();
        return (user, org);
    }
}

/// <summary>
/// The <see cref="InventoryService"/> arm of the same U0.6 behaviour, at the unit level. The cap
/// is read from the injected <see cref="MediaOptions"/>, and an over-cap data URL is refused
/// rather than swallowed by the method's best-effort <c>catch</c> and reported as a successful
/// item write.
/// </summary>
public class CatalogImageUploadSizeInventoryServiceTests
{
    private const int CapBytes = 1024;

    private static readonly Guid OrgId = Guid.NewGuid();

    [Fact]
    public async Task ProcessImageUrl_ExactlyAtConfiguredCap_StoresTheImageAndTheItem()
    {
        var repository = RepositorySpy();
        var service = ServiceWithCap(repository, CapBytes);

        var created = await service.CreateItemAsync(new CreateInventoryItemDto
        {
            OrganizationId = OrgId,
            ItemName = "At the cap",
            ImageUrl = DataUrl(CapBytes)
        });

        created.ImageUrl.Should().StartWith($"/api/v1/orgs/{OrgId}/catalog/images/");
        repository.Verify(
            r => r.AddImageAsync(
                It.Is<InventoryImage>(i => i.ImageData != null && i.ImageData.LongLength == CapBytes),
                It.IsAny<CancellationToken>()),
            Times.Once);
        repository.Verify(
            r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task ProcessImageUrl_OneByteOverConfiguredCap_ThrowsAndWritesNothing()
    {
        var repository = RepositorySpy();
        var service = ServiceWithCap(repository, CapBytes);

        var act = async () => await service.CreateItemAsync(new CreateInventoryItemDto
        {
            OrganizationId = OrgId,
            ItemName = "Over the cap",
            ImageUrl = DataUrl(CapBytes + 1)
        });

        (await act.Should().ThrowAsync<CatalogImageTooLargeException>())
            .WithMessage($"*{CapBytes} bytes*");

        repository.Verify(
            r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()),
            Times.Never);
        repository.Verify(
            r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    private static Mock<IInventoryRepository> RepositorySpy()
    {
        var repository = new Mock<IInventoryRepository>();
        repository
            .Setup(r => r.AddImageAsync(It.IsAny<InventoryImage>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repository
            .Setup(r => r.AddAsync(It.IsAny<InventoryItem>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        return repository;
    }

    private static InventoryService ServiceWithCap(Mock<IInventoryRepository> repository, int capBytes)
        => new(
            repository.Object,
            Options.Create(new MediaOptions { CatalogMaxFileBytes = capBytes }));

    private static string DataUrl(int length)
    {
        var bytes = new byte[length];
        return "data:image/jpeg;base64," + Convert.ToBase64String(bytes);
    }
}
