using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

public class CatalogEndpointsIntegrationTests : IAsyncLifetime
{
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
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
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
            IsActive = true
        };
        db.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"{prefix} Boutique",
            Slug = $"{prefix}-boutique",
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Organizations.Add(org);

        var membership = new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.OrganizationMemberships.Add(membership);

        await db.SaveChangesAsync();
        return (user, org);
    }

    [Fact]
    public async Task GetItems_WithoutAuth_ReturnsUnauthorized()
    {
        var orgId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/orgs/{orgId}/catalog/items");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnalyzeImage_WithoutAuth_ReturnsUnauthorized()
    {
        // Regression guard: this route previously carried .AllowAnonymous(), which let an
        // unauthenticated caller invoke the paid Vision AI provider against any organization.
        var orgId = Guid.NewGuid();
        var response = await _client.PostAsJsonAsync(
            $"/api/v1/orgs/{orgId}/catalog/analyze-image",
            new AnalyzeImageDto { ImageUrl = "https://images.aveline.luxury/evening-dress.jpg" });

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetItems_WithActiveMember_ReturnsOk()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_items");
        var token = CreateToken(user.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemDto>>();
        items.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAndGetItem_Flow_Succeeds()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_create");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Silk Banarasi Saree",
            Category = "saree",
            Color = "crimson",
            Price = 450.00m,
            Quantity = 8,
            Sizes = new List<string> { "Free Size" },
            Description = "Pure silk handloom saree"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));

        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        created.Should().NotBeNull();
        created!.ItemName.Should().Be("Silk Banarasi Saree");
        created.OrganizationId.Should().Be(org.Id);

        // Get by ID
        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}", token));

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.ItemName.Should().Be("Silk Banarasi Saree");
    }

    [Fact]
    public async Task UpdateItem_AndStatus_Succeeds()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_update");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Linen Kurta",
            Category = "kurta",
            Color = "navy",
            Price = 120.00m,
            Quantity = 15
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        var created = (await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!;

        // Update item details
        var updateDto = new UpdateInventoryItemDto
        {
            ItemName = "Premium Linen Kurta",
            Price = 140.00m,
            Quantity = 12
        };
        var putResponse = await _client.SendAsync(
            Authorized(HttpMethod.Put, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}", token, JsonContent.Create(updateDto)));

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        updated!.ItemName.Should().Be("Premium Linen Kurta");
        updated.Price.Should().Be(140.00m);

        // Update status
        var statusDto = new UpdateInventoryStatusDto { Status = "reserved" };
        var patchResponse = await _client.SendAsync(
            Authorized(HttpMethod.Patch, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}/status", token, JsonContent.Create(statusDto)));

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await patchResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        patched!.Status.Should().Be("reserved");
    }

    [Fact]
    public async Task GetLowStock_ReturnsLowStockItems()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_lowstock");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Scarce Velvet Dupatta",
            Category = "dupatta",
            Color = "gold",
            Price = 85.00m,
            Quantity = 2
        };
        await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/low-stock?threshold=3", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemDto>>();
        items.Should().NotBeNull();
        items!.Should().Contain(x => x.ItemName == "Scarce Velvet Dupatta");
    }

    [Fact]
    public async Task Lookbooks_And_Sourcing_Endpoints_WorkCorrectly()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_lookbooks");
        var token = CreateToken(user.ClerkId);

        // Lookbooks query
        var lookbooksResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/lookbooks", token));
        lookbooksResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sourcing requests query
        var sourcingResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/sourcing", token));
        sourcingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create sourcing request
        var createSourcingDto = new CreateSourcingRequestDto
        {
            Category = "lehenga",
            Color = "rose gold",
            Description = "Rose Gold Embroidered Lehenga",
            TargetPrice = 750.00m,
            QuantityNeeded = 5,
            Urgency = "high"
        };
        var createSourcingResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/sourcing", token, JsonContent.Create(createSourcingDto)));
        createSourcingResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdSourcing = (await createSourcingResponse.Content.ReadFromJsonAsync<SourcingRequestDto>())!;

        // Patch sourcing status
        var updateStatusDto = new UpdateSourcingStatusDto("ordered");
        var patchSourcingResponse = await _client.SendAsync(
            Authorized(HttpMethod.Patch, $"/api/v1/orgs/{org.Id}/catalog/sourcing/{createdSourcing.Id}/status", token, JsonContent.Create(updateStatusDto)));
        patchSourcingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suppliers_Endpoints_ReturnOk()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_suppliers");
        var token = CreateToken(user.ClerkId);

        var suppliersResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/suppliers", token));
        suppliersResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var catalogResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/suppliers/{Guid.NewGuid()}/catalog", token));
        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Images_UploadAndGetBinary_PersistsAndStreamsSuccessfully()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_images");
        var token = CreateToken(user.ClerkId);

        // 1. Upload via multipart form
        var fakeImageBytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46 };
        var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(fakeImageBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        multipartContent.Add(fileContent, "file", "saree-photo.jpg");

        var uploadResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/images/upload", token, multipartContent));
        uploadResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var uploadResult = (await uploadResponse.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        uploadResult.Should().ContainKey("id");
        uploadResult.Should().ContainKey("url");
        var imageId = uploadResult["id"].ToString()!;
        var imageUrl = uploadResult["url"].ToString()!;
        imageUrl.Should().Contain(imageId);

        // 2. Stream image back from PostgreSQL
        var getResponse = await _client.GetAsync($"/api/v1/orgs/{org.Id}/catalog/images/{imageId}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/jpeg");
        getResponse.Headers.CacheControl?.Public.Should().BeTrue();

        var downloadedBytes = await getResponse.Content.ReadAsByteArrayAsync();
        downloadedBytes.Should().Equal(fakeImageBytes);

        // 3. Upload via Base64 JSON payload
        var base64UploadDto = new UploadImagePayloadDto
        {
            ImageData = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==",
            FileName = "test-icon.png",
            ContentType = "image/png"
        };
        var base64Response = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/images/upload", token, JsonContent.Create(base64UploadDto)));
        base64Response.StatusCode.Should().Be(HttpStatusCode.OK);

        var base64Result = (await base64Response.Content.ReadFromJsonAsync<Dictionary<string, object>>())!;
        var base64ImageId = base64Result["id"].ToString()!;

        var getPngResponse = await _client.GetAsync($"/api/v1/orgs/{org.Id}/catalog/images/{base64ImageId}");
        getPngResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getPngResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
    }

    [Fact]
    public void CatalogImageRoute_KeepsTheF7AnonymityCommentAndTheCloudinaryDirection()
    {
        // F-7/Q4: the catalog image route is the one deliberate anonymous route on the catalog
        // group, because catalog imagery is public. U1.2 extends the recorded reason — a
        // Cloudinary row now answers 302 to the CDN rather than streaming bytes — without
        // dropping it. The guard is keyed on the source text next to the route template, because
        // a runtime test can only see the behaviour, not the recorded reason.
        var source = File.ReadAllText(
            Path.Combine(RepositoryRoot, "Aveline.Api", "Endpoints", "CatalogEndpoints.cs"));

        var routeStart = source.IndexOf("\"/images/{imageId:guid}\"", StringComparison.Ordinal);
        routeStart.Should().BeGreaterThanOrEqualTo(0, "the catalog image route must still exist");

        var route = source[routeStart..Math.Min(source.Length, routeStart + 3000)];
        route.Should().Contain("F-7", "the reason for the anonymity stays recorded on the route");
        route.Should().Contain("Cloudinary", "the extended comment must name the direction it now redirects to");
        route.Should().Contain("302", "the redirect is the delivery contract for a Cloudinary row");
        route.Should().Contain(".AllowAnonymous()", "the route stays deliberately anonymous");
    }

    private static string RepositoryRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docker-compose.yml")))
            {
                directory = directory.Parent;
            }

            directory.Should().NotBeNull();
            return directory!.FullName;
        }
    }

    [Fact]
    public async Task CreateItem_WithBase64DataUrlImage_AutoOffloadsToInventoryImages_AndPersistsSuccessfully()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_base64_auto");
        var token = CreateToken(user.ClerkId);

        var base64DataUrl = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==";

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Handloom Silk Saree with AI Narrative",
            Category = "Sarees",
            Color = "Emerald Green / Antique Gold Zari",
            Fabric = "Pure Handloom Mulberry Kanjeevaram Silk",
            Style = "Traditional Heritage Temple Silhouette",
            Price = 1850.00m,
            Cost = 750.00m,
            Quantity = 5,
            Sizes = new List<string> { "Free Size", "Unstitched Blouse" },
            ImageUrl = base64DataUrl,
            Description = "Exquisite emerald green handloom silk saree woven from authentic mulberry silk, featuring an opulent gold zari brocade with a lustrous heirloom drape. Tailored with meticulous craftsmanship, making it a centerpiece for weddings, celebratory galas, and festive receptions. Styling: Accentuate with handcrafted polki or antique gold jewelry."
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));

        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        created.Should().NotBeNull();
        created!.ItemName.Should().Be("Handloom Silk Saree with AI Narrative");
        created.OrganizationId.Should().Be(org.Id);
        created.ImageUrl.Should().NotBeNullOrEmpty();
        created.ImageUrl.Should().StartWith($"/api/v1/orgs/{org.Id}/catalog/images/");

        // Verify the auto-offloaded image can be fetched from the database storage endpoint
        var getImageResponse = await _client.GetAsync(created.ImageUrl);
        getImageResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        getImageResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
    }

    [Fact]
    public async Task GetItemQr_AsPng_Returns200WithPngImage()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_qr_png");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Embroidered Velvet Cape",
            Category = "Capes",
            Color = "Midnight Blue",
            Price = 650.00m,
            Quantity = 4,
            Sku = "CPE-VLV-001"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Act: Get QR as PNG binary
        var qrResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created!.Id}/qr?format=png&size=300", token));

        // Assert
        qrResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        qrResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/png");
        var bytes = await qrResponse.Content.ReadAsByteArrayAsync();
        bytes.Length.Should().BeGreaterThan(50);
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50);
    }

    [Fact]
    public async Task GetItemQr_AsSvg_Returns200WithSvgImage()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_qr_svg");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Pleated Silk Maxi",
            Category = "Dresses",
            Color = "Rose Gold",
            Price = 420.00m,
            Quantity = 3,
            Sku = "DRS-MAX-002"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Act: Get QR as SVG
        var qrResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created!.Id}/qr?format=svg&size=200", token));

        // Assert
        qrResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        qrResponse.Content.Headers.ContentType?.MediaType.Should().Be("image/svg+xml");
        var svgText = await qrResponse.Content.ReadAsStringAsync();
        svgText.Should().Contain("<svg");
        svgText.Should().Contain("</svg>");
    }

    [Fact]
    public async Task GetItemQr_AsJson_Returns200WithBase64AndPayload()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_qr_json");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Cashmere Scarf",
            Category = "Accessories",
            Color = "Camel",
            Price = 160.00m,
            Quantity = 12,
            Sku = "ACC-SCF-003"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Act: Get QR as JSON
        var qrResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created!.Id}/qr?format=json&size=250", token));

        // Assert
        qrResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var jsonDto = await qrResponse.Content.ReadFromJsonAsync<QrCodeResponseDto>();
        jsonDto.Should().NotBeNull();
        jsonDto!.DataUrl.Should().StartWith("data:image/png;base64,");
        jsonDto.Payload.Should().Contain(created.Id.ToString());
        jsonDto.Payload.Should().Contain("ACC-SCF-003");
        // The code names the boutique, so the same piece id in another shop cannot be confused for
        // this one.
        jsonDto.Payload.Should().Contain($"org={org.Id}");
    }

    [Fact]
    public async Task PostQrGenerate_WithCustomPayload_ReturnsValidResponse()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_qr_gen");
        var token = CreateToken(user.ClerkId);

        var genDto = new GenerateQrDto
        {
            Payload = "https://aveline.app/fitting-room/123",
            Format = "json",
            Size = 200
        };

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/qr/generate", token, JsonContent.Create(genDto)));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto = await response.Content.ReadFromJsonAsync<QrCodeResponseDto>();
        dto.Should().NotBeNull();
        dto!.Payload.Should().Be("https://aveline.app/fitting-room/123");
        dto.DataUrl.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public async Task PostScanQr_WithJsonPayload_ResolvesItem()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_qr_scan_json");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Chiffon Evening Gown",
            Category = "Eveningwear",
            Color = "Champagne",
            Price = 980.00m,
            Quantity = 2,
            Sku = "GWN-CHF-777"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Get the generated QR payload
        var qrJsonResp = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created!.Id}/qr?format=json", token));
        var qrDto = await qrJsonResp.Content.ReadFromJsonAsync<QrCodeResponseDto>();

        // Act: Scan and resolve the QR code
        var scanDto = new ScanQrDto { Code = qrDto!.Payload };
        var scanResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items/scan-qr", token, JsonContent.Create(scanDto)));

        // Assert
        scanResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await scanResponse.Content.ReadFromJsonAsync<QrScanResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Found.Should().BeTrue();
        result.ScanType.Should().Be("InventoryItem");
        result.Item.Should().NotBeNull();
        result.Item!.Id.Should().Be(created.Id);
        result.Item.ItemName.Should().Be("Chiffon Evening Gown");
    }

    [Fact]
    public async Task PostScanQr_WithSku_ResolvesItem()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_qr_scan_sku");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Structured Tuxedo Jacket",
            Category = "Jackets",
            Color = "Ivory",
            Price = 720.00m,
            Quantity = 6,
            Sku = "JKT-TUX-555"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Act: Scan SKU directly
        var scanDto = new ScanQrDto { Code = "JKT-TUX-555" };
        var scanResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items/scan-qr", token, JsonContent.Create(scanDto)));

        // Assert
        scanResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await scanResponse.Content.ReadFromJsonAsync<QrScanResultDto>();
        result.Should().NotBeNull();
        result!.Success.Should().BeTrue();
        result.Found.Should().BeTrue();
        result.Item!.Id.Should().Be(created!.Id);
        result.Item.Sku.Should().Be("JKT-TUX-555");
    }

    [Fact]
    public async Task PostScanQr_WithCrossTenantItem_ReturnsFoundFalse()
    {
        var (user1, org1) = await SeedMemberAndOrgAsync("cat_qr_iso1");
        var (user2, org2) = await SeedMemberAndOrgAsync("cat_qr_iso2");
        var token1 = CreateToken(user1.ClerkId);
        var token2 = CreateToken(user2.ClerkId);

        // Org 1 creates an item
        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Org1 Exclusive Corset",
            Category = "Tops",
            Color = "Noir",
            Price = 350.00m,
            Quantity = 1,
            Sku = "TOP-COR-001"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org1.Id}/catalog/items", token1, JsonContent.Create(createDto)));
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Org 2 attempts to scan Org 1's Item ID
        var scanDto = new ScanQrDto { Code = created!.Id.ToString() };
        var scanResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org2.Id}/catalog/items/scan-qr", token2, JsonContent.Create(scanDto)));

        // Assert: Multi-tenant isolation prevents org 2 from finding org 1's item
        scanResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await scanResponse.Content.ReadFromJsonAsync<QrScanResultDto>();
        result.Should().NotBeNull();
        result!.Found.Should().BeFalse();
        result.Item.Should().BeNull();
    }

    [Fact]
    public async Task DeleteItem_WithValidId_Returns204NoContent_AndExcludesFromSubsequentQueries()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_del_ok");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Embroidered Organza Dupatta",
            Category = "Drapes & Shawls",
            Color = "Rose Gold",
            Price = 450.00m,
            Quantity = 2,
            Sku = "DRP-ORG-999"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        created.Should().NotBeNull();

        // Act: Delete item
        var deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/orgs/{org.Id}/catalog/items/{created!.Id}", token));

        // Assert: 204 No Content
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Subsequent GET by ID should return 404
        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}", token));
        getResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Subsequent search should not include the deleted item
        var searchResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items", token));
        searchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var searchItems = await searchResponse.Content.ReadFromJsonAsync<List<InventoryItemDto>>();
        searchItems.Should().NotContain(i => i.Id == created.Id);
    }

    [Fact]
    public async Task DeleteItem_WithNonExistentId_Returns404NotFound()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_del_404");
        var token = CreateToken(user.ClerkId);

        var deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/orgs/{org.Id}/catalog/items/{Guid.NewGuid()}", token));

        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task DeleteItem_WithCrossTenantOrgId_Returns404NotFound()
    {
        var (user1, org1) = await SeedMemberAndOrgAsync("cat_del_iso1");
        var (user2, org2) = await SeedMemberAndOrgAsync("cat_del_iso2");
        var token1 = CreateToken(user1.ClerkId);
        var token2 = CreateToken(user2.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Org 1 Royal Saree",
            Category = "Sarees",
            Color = "Emerald",
            Price = 1200.00m,
            Quantity = 1,
            Sku = "SAR-EMR-001"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org1.Id}/catalog/items", token1, JsonContent.Create(createDto)));
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();

        // Org 2 tries to delete Org 1's item
        var deleteResponse = await _client.SendAsync(
            Authorized(HttpMethod.Delete, $"/api/v1/orgs/{org2.Id}/catalog/items/{created!.Id}", token2));

        // Assert: 404 NotFound
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NotFound);

        // Org 1 can still fetch their item
        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org1.Id}/catalog/items/{created.Id}", token1));
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Lookbook_UpdateAndDelete_Flow_Succeeds()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_lookbook_crud");
        var token = CreateToken(user.ClerkId);

        var item = await CreateItemAsync(org.Id, token, "Embroidered Velvet Lehenga", 900m, 3);

        var composeResponse = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org.Id}/catalog/lookbooks/compose",
                token,
                JsonContent.Create(new ComposeOutfitDto { PrimaryItemId = item.Id, Occasion = "gala" })));
        composeResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var listResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/lookbooks", token));
        var lookbooks = (await listResponse.Content.ReadFromJsonAsync<List<OutfitCompositionDto>>())!;
        lookbooks.Should().HaveCount(1);
        var lookbookId = lookbooks[0].Id;

        // Rename and re-occasion it.
        var updateDto = new UpdateOutfitCompositionDto
        {
            Name = "Royal Gala Ensemble",
            Occasion = "Cocktail Reception & Gala",
            StyleNotes = "Pair with a statement choker."
        };
        var putResponse = await _client.SendAsync(
            Authorized(
                HttpMethod.Put,
                $"/api/v1/orgs/{org.Id}/catalog/lookbooks/{lookbookId}",
                token,
                JsonContent.Create(updateDto)));

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = (await putResponse.Content.ReadFromJsonAsync<OutfitCompositionDto>())!;
        updated.Name.Should().Be("Royal Gala Ensemble");
        updated.Occasion.Should().Be("Cocktail Reception & Gala");
        updated.StyleNotes.Should().Be("Pair with a statement choker.");

        // Delete it, and prove the list no longer carries it.
        var deleteResponse = await _client.SendAsync(
            Authorized(
                HttpMethod.Delete,
                $"/api/v1/orgs/{org.Id}/catalog/lookbooks/{lookbookId}",
                token));
        deleteResponse.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var afterDelete = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/lookbooks", token));
        var remaining = (await afterDelete.Content.ReadFromJsonAsync<List<OutfitCompositionDto>>())!;
        remaining.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateLookbook_WithUnknownOrCrossTenantId_Returns404()
    {
        var (user1, org1) = await SeedMemberAndOrgAsync("cat_lookbook_iso1");
        var (user2, org2) = await SeedMemberAndOrgAsync("cat_lookbook_iso2");
        var token1 = CreateToken(user1.ClerkId);
        var token2 = CreateToken(user2.ClerkId);

        var item = await CreateItemAsync(org1.Id, token1, "Org One Saree", 500m, 2);
        await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org1.Id}/catalog/lookbooks/compose",
                token1,
                JsonContent.Create(new ComposeOutfitDto { PrimaryItemId = item.Id, Occasion = "wedding" })));

        var listResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org1.Id}/catalog/lookbooks", token1));
        var lookbookId = (await listResponse.Content.ReadFromJsonAsync<List<OutfitCompositionDto>>())![0].Id;

        var body = JsonContent.Create(new UpdateOutfitCompositionDto { Name = "Stolen Look" });

        // Unknown id, and another tenant's id: both are 404, never a silent write.
        (await _client.SendAsync(Authorized(
            HttpMethod.Put, $"/api/v1/orgs/{org1.Id}/catalog/lookbooks/{Guid.NewGuid()}", token1, body)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.SendAsync(Authorized(
            HttpMethod.Put, $"/api/v1/orgs/{org2.Id}/catalog/lookbooks/{lookbookId}", token2, body)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        (await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{org2.Id}/catalog/lookbooks/{lookbookId}", token2)))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task RecordSale_DecrementsStockAndWritesTheTakingsJournal()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_sale_ok");
        var token = CreateToken(user.ClerkId);

        var item = await CreateItemAsync(org.Id, token, "Kanjeevaram Silk Saree", 450m, 5);

        var saleResponse = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org.Id}/catalog/items/{item.Id}/sales",
                token,
                JsonContent.Create(new RecordCatalogSaleDto { Quantity = 2 })));

        saleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = (await saleResponse.Content.ReadFromJsonAsync<CatalogSaleReceiptDto>())!;
        receipt.QuantitySold.Should().Be(2);
        receipt.UnitPrice.Should().Be(450m);
        receipt.TotalAmount.Should().Be(900m);
        receipt.RemainingStock.Should().Be(3);
        receipt.Status.Should().Be("available");

        // The catalog row is the one source of stock truth, so read it back rather than trusting
        // the receipt.
        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{item.Id}", token));
        var reloaded = (await getResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!;
        reloaded.Quantity.Should().Be(3);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ledgerEntry = db.BoutiqueSaleEntries
            .Single(entry => entry.OrganizationId == org.Id);
        ledgerEntry.Id.Should().Be(receipt.LedgerEntryId);
        ledgerEntry.Amount.Should().Be(900m);
        ledgerEntry.Kind.Should().Be(BoutiqueSaleEntryKind.Sale);
        ledgerEntry.ChargeBasis.Should().Be(BoutiqueSaleChargeBasis.Verified);
        ledgerEntry.Status.Should().Be(BoutiqueSaleEntryStatus.Recorded);
    }

    [Fact]
    public async Task RecordSale_ThatEmptiesTheShelf_LeavesThePieceReserved()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_sale_empty");
        var token = CreateToken(user.ClerkId);

        var item = await CreateItemAsync(org.Id, token, "Single Run Corset", 1200m, 1);

        var saleResponse = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org.Id}/catalog/items/{item.Id}/sales",
                token,
                JsonContent.Create(new RecordCatalogSaleDto { Quantity = 1 })));

        saleResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var receipt = (await saleResponse.Content.ReadFromJsonAsync<CatalogSaleReceiptDto>())!;
        receipt.RemainingStock.Should().Be(0);
        // The same derivation the Add/Edit drawer uses for a row at zero.
        receipt.Status.Should().Be("reserved");
    }

    [Fact]
    public async Task RecordSale_WithMorePiecesThanStock_Returns409AndSellsNothing()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_sale_oversell");
        var token = CreateToken(user.ClerkId);

        var item = await CreateItemAsync(org.Id, token, "Limited Organza Dupatta", 300m, 2);

        var saleResponse = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{org.Id}/catalog/items/{item.Id}/sales",
                token,
                JsonContent.Create(new RecordCatalogSaleDto { Quantity = 3 })));

        saleResponse.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{item.Id}", token));
        var reloaded = (await getResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!;
        reloaded.Quantity.Should().Be(2);

        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.BoutiqueSaleEntries.Count(entry => entry.OrganizationId == org.Id).Should().Be(0);
    }

    [Fact]
    public async Task RecordSale_WithZeroPriceOrForeignItem_IsRefused()
    {
        var (user1, org1) = await SeedMemberAndOrgAsync("cat_sale_guard1");
        var (user2, org2) = await SeedMemberAndOrgAsync("cat_sale_guard2");
        var token1 = CreateToken(user1.ClerkId);
        var token2 = CreateToken(user2.ClerkId);

        var item = await CreateItemAsync(org1.Id, token1, "Free Sample Stole", 0m, 4);

        // A zero-price sale is a gift, and the ledger refuses a non-positive amount.
        (await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{org1.Id}/catalog/items/{item.Id}/sales",
            token1,
            JsonContent.Create(new RecordCatalogSaleDto { Quantity = 1, UnitPrice = 0m }))))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Another tenant's piece is a 404, and its stock is untouched.
        (await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{org2.Id}/catalog/items/{item.Id}/sales",
            token2,
            JsonContent.Create(new RecordCatalogSaleDto { Quantity = 1 }))))
            .StatusCode.Should().Be(HttpStatusCode.NotFound);

        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org1.Id}/catalog/items/{item.Id}", token1));
        (await getResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!.Quantity.Should().Be(4);
    }

    /// <summary>Creates one piece through the API and returns it, so a test names real server state.</summary>
    private async Task<InventoryItemDto> CreateItemAsync(Guid orgId, string token, string name, decimal price, int quantity)
    {
        var response = await _client.SendAsync(
            Authorized(
                HttpMethod.Post,
                $"/api/v1/orgs/{orgId}/catalog/items",
                token,
                JsonContent.Create(new CreateInventoryItemDto
                {
                    ItemName = name,
                    Category = "Sarees",
                    Color = "Emerald",
                    Price = price,
                    Quantity = quantity
                })));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        return (await response.Content.ReadFromJsonAsync<InventoryItemDto>())!;
    }
}
