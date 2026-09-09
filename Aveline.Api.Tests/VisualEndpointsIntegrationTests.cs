using System.Net;
using System.Net.Http.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aveline.Api.Tests;

public class VisualEndpointsIntegrationTests : IAsyncLifetime
{
    private const string InternalKey = "test-key";
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
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task InternalEndpoint_WithoutInternalKey_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/search-inventory")
        {
            Content = JsonContent.Create(new { })
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task InternalEndpoint_WithWrongInternalKey_ReturnsUnauthorized()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/search-inventory")
        {
            Content = JsonContent.Create(new { })
        };
        request.Headers.Add("X-Internal-Token", "wrong-secret-key");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task SearchInventory_WithValidInternalKey_Returns200()
    {
        var body = new
        {
            orgId = Guid.NewGuid(),
            color = "emerald",
            category = "saree"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/search-inventory")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task SearchInventory_WithValidInternalTokenHeader_Returns200()
    {
        var body = new
        {
            orgId = Guid.NewGuid(),
            color = "emerald",
            category = "saree"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/search-inventory")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);
    }

    // --- Endpoint 1: GET /api/internal/visual/inventory/{itemId} ---

    [Fact]
    public async Task GetInventoryItem_WithoutInternalKey_ReturnsUnauthorized()
    {
        var itemId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/inventory/{itemId}?orgId={orgId}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetInventoryItem_WhenNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/inventory/{itemId}?orgId={orgId}");
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetInventoryItem_WhenExists_ReturnsOkWithItem()
    {
        var orgId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Royal Kanjivaram Silk",
            Category = "saree",
            Color = "crimson",
            Price = 65000,
            Quantity = 4,
            Status = "available"
        };

        // Seed item in DB context
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InventoryItems.AddAsync(item);
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/inventory/{item.Id}?orgId={orgId}");
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<InventoryItemDto>();
        result.Should().NotBeNull();
        result!.Id.Should().Be(item.Id);
        result.ItemName.Should().Be("Royal Kanjivaram Silk");
        result.Price.Should().Be(65000);
    }

    // --- Endpoint 3: POST /api/internal/visual/inventory ---

    [Fact]
    public async Task CreateInventoryItem_WithoutInternalKey_ReturnsUnauthorized()
    {
        var body = new
        {
            orgId = Guid.NewGuid(),
            itemName = "Banarasi Brocade Saree",
            category = "saree",
            color = "gold",
            price = 55000,
            quantity = 2
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/inventory")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateInventoryItem_WithValidData_ReturnsCreated()
    {
        var orgId = Guid.NewGuid();
        var body = new
        {
            orgId = orgId,
            itemName = "Banarasi Brocade Saree",
            category = "saree",
            color = "gold",
            sizes = new List<string> { "FreeSize" },
            price = 55000m,
            quantity = 5
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/inventory")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Created);

        var created = await response.Content.ReadFromJsonAsync<InventoryItemDto>();
        created.Should().NotBeNull();
        created!.ItemName.Should().Be("Banarasi Brocade Saree");
        created.OrgId.Should().Be(orgId);
        created.Price.Should().Be(55000m);
        created.Quantity.Should().Be(5);
    }

    // --- Endpoint 4: PUT /api/internal/visual/inventory/{itemId} ---

    [Fact]
    public async Task UpdateInventoryItem_WithoutInternalKey_ReturnsUnauthorized()
    {
        var itemId = Guid.NewGuid();
        var body = new { orgId = Guid.NewGuid(), itemName = "Updated Saree" };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/visual/inventory/{itemId}")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateInventoryItem_WhenNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();
        var body = new
        {
            orgId = Guid.NewGuid(),
            itemName = "Updated Saree",
            category = "saree",
            color = "blue",
            price = 45000m,
            quantity = 2,
            status = "available"
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/visual/inventory/{itemId}")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateInventoryItem_WithValidData_ReturnsOk()
    {
        var orgId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Original Saree",
            Category = "saree",
            Color = "green",
            Price = 30000m,
            Quantity = 10,
            Status = "available"
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InventoryItems.AddAsync(item);
            await db.SaveChangesAsync();
        }

        var updateBody = new
        {
            orgId = orgId,
            itemName = "Updated Saree Premium",
            category = "saree",
            color = "emerald",
            sizes = new List<string> { "M", "L" },
            price = 35000m,
            quantity = 8,
            status = "available"
        };

        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/internal/visual/inventory/{item.Id}")
        {
            Content = JsonContent.Create(updateBody)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<InventoryItemDto>();
        updated.Should().NotBeNull();
        updated!.ItemName.Should().Be("Updated Saree Premium");
        updated.Color.Should().Be("emerald");
        updated.Price.Should().Be(35000m);
        updated.Quantity.Should().Be(8);
    }

    // --- Endpoint 5: PATCH /api/internal/visual/inventory/{itemId}/status ---

    [Fact]
    public async Task UpdateInventoryStatus_WithoutInternalKey_ReturnsUnauthorized()
    {
        var itemId = Guid.NewGuid();
        var body = new { orgId = Guid.NewGuid(), status = "reserved" };

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/internal/visual/inventory/{itemId}/status")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task UpdateInventoryStatus_WhenNotFound_ReturnsNotFound()
    {
        var itemId = Guid.NewGuid();
        var body = new { orgId = Guid.NewGuid(), status = "reserved" };

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/internal/visual/inventory/{itemId}/status")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task UpdateInventoryStatus_WithValidData_ReturnsOk()
    {
        var orgId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Status Test Saree",
            Category = "saree",
            Color = "ruby",
            Price = 40000m,
            Quantity = 3,
            Status = "available"
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InventoryItems.AddAsync(item);
            await db.SaveChangesAsync();
        }

        var statusBody = new
        {
            orgId = orgId,
            status = "reserved"
        };

        using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/internal/visual/inventory/{item.Id}/status")
        {
            Content = JsonContent.Create(statusBody)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var updated = await response.Content.ReadFromJsonAsync<InventoryItemDto>();
        updated.Should().NotBeNull();
        updated!.Status.Should().Be("reserved");
    }

    // --- Endpoint 6: GET /api/internal/visual/inventory/low-stock ---

    [Fact]
    public async Task GetLowStockInventory_WithoutInternalKey_ReturnsUnauthorized()
    {
        var orgId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/inventory/low-stock?orgId={orgId}&threshold=3");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetLowStockInventory_WithThreshold_ReturnsMatchingLowStockItems()
    {
        var orgId = Guid.NewGuid();

        var lowStockItem = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Scarce Velvet Shawl",
            Category = "shawl",
            Color = "maroon",
            Price = 18000m,
            Quantity = 2,
            Status = "available"
        };

        var highStockItem = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Abundant Cotton Kurta",
            Category = "kurta",
            Color = "white",
            Price = 8000m,
            Quantity = 25,
            Status = "available"
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InventoryItems.AddRangeAsync(lowStockItem, highStockItem);
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/inventory/low-stock?orgId={orgId}&threshold=5");
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemDto>>();
        items.Should().NotBeNull();
        items!.Should().ContainSingle(x => x.Id == lowStockItem.Id);
        items.Should().NotContain(x => x.Id == highStockItem.Id);
    }

    // --- Endpoint 7: POST /api/internal/visual/analyze-image ---

    [Fact]
    public async Task AnalyzeImage_WithoutInternalKey_ReturnsUnauthorized()
    {
        var body = new { orgId = Guid.NewGuid(), imageUrl = "https://example.com/saree.jpg" };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/analyze-image")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AnalyzeImage_WithValidImagePayload_ReturnsAnalysisResults()
    {
        var orgId = Guid.NewGuid();
        var body = new
        {
            orgId = orgId,
            imageUrl = "https://example.com/saree.jpg",
            prompt = "Extract visual attributes and color palette"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/analyze-image")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<ImageAnalysisResultDto>();
        result.Should().NotBeNull();
        result!.Category.Should().NotBeNullOrWhiteSpace();
        result.PrimaryColor.Should().NotBeNullOrWhiteSpace();
        result.ConfidenceScore.Should().BeGreaterThan(0);
    }

    // --- Endpoint 8: GET /api/internal/visual/customer-matches/{itemId} ---

    [Fact]
    public async Task GetCustomerMatches_WithoutInternalKey_ReturnsUnauthorized()
    {
        var itemId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/customer-matches/{itemId}?orgId={orgId}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetCustomerMatches_WithValidItem_ReturnsCustomerMatchList()
    {
        var orgId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/customer-matches/{itemId}?orgId={orgId}&minScore=0.7");
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var matches = await response.Content.ReadFromJsonAsync<List<CustomerMatchDto>>();
        matches.Should().NotBeNull();
    }

    // --- Endpoint 9: POST /api/internal/visual/customer-matches/{itemId}/generate ---

    [Fact]
    public async Task GenerateCustomerMatches_WithoutInternalKey_ReturnsUnauthorized()
    {
        var itemId = Guid.NewGuid();
        var body = new { orgId = Guid.NewGuid(), maxMatches = 10 };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/visual/customer-matches/{itemId}/generate")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GenerateCustomerMatches_WithValidItem_ReturnsGeneratedMatches()
    {
        var itemId = Guid.NewGuid();
        var orgId = Guid.NewGuid();
        var body = new
        {
            orgId = orgId,
            maxMatches = 5
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/api/internal/visual/customer-matches/{itemId}/generate")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<List<CustomerMatchDto>>();
        result.Should().NotBeNull();
        result!.Should().NotBeEmpty();
    }

    // --- Endpoint 10: POST /api/internal/visual/outfits/compose ---

    [Fact]
    public async Task ComposeOutfit_WithoutInternalKey_ReturnsUnauthorized()
    {
        var body = new
        {
            orgId = Guid.NewGuid(),
            primaryItemId = Guid.NewGuid(),
            occasion = "wedding"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/outfits/compose")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ComposeOutfit_WithValidPrimaryItem_ReturnsComposedOutfitLook()
    {
        var orgId = Guid.NewGuid();
        var primaryItem = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Royal Sapphire Saree",
            Category = "saree",
            Color = "sapphire blue",
            Price = 48000m,
            Quantity = 3,
            Status = "available"
        };

        var blouseItem = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Zari Embroidered Raw Silk Blouse",
            Category = "blouse",
            Color = "gold",
            Price = 12000m,
            Quantity = 5,
            Status = "available"
        };

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await db.InventoryItems.AddRangeAsync(primaryItem, blouseItem);
            await db.SaveChangesAsync();
        }

        var body = new
        {
            orgId = orgId,
            primaryItemId = primaryItem.Id,
            occasion = "wedding",
            style = "royal luxury"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/outfits/compose")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var outfit = await response.Content.ReadFromJsonAsync<ComposedOutfitDto>();
        outfit.Should().NotBeNull();
        outfit!.LookName.Should().NotBeNullOrWhiteSpace();
        outfit.PrimaryItem.Should().NotBeNull();
        outfit.PrimaryItem.Id.Should().Be(primaryItem.Id);
    }

    // --- Endpoint 11: POST /api/internal/visual/sourcing-requests ---

    [Fact]
    public async Task CreateSourcingRequest_WithoutInternalKey_ReturnsUnauthorized()
    {
        var body = new
        {
            orgId = Guid.NewGuid(),
            category = "saree",
            color = "burgundy",
            description = "Silk Saree with gold zari"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/sourcing-requests")
        {
            Content = JsonContent.Create(body)
        };

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task CreateSourcingRequest_WithValidData_ReturnsCreated()
    {
        var orgId = Guid.NewGuid();
        var body = new
        {
            orgId = orgId,
            category = "saree",
            color = "burgundy",
            description = "Silk Saree with gold zari",
            targetPrice = 50000m,
            quantityNeeded = 2,
            urgency = "high"
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/internal/visual/sourcing-requests")
        {
            Content = JsonContent.Create(body)
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<SourcingRequestDto>();
        result.Should().NotBeNull();
        result!.Category.Should().Be("saree");
        result.Color.Should().Be("burgundy");
        result.Status.Should().Be("pending");
    }

    // --- Endpoint 12: GET /api/internal/visual/suppliers/{supplierId}/catalog ---

    [Fact]
    public async Task GetSupplierCatalog_WithoutInternalKey_ReturnsUnauthorized()
    {
        var supplierId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/suppliers/{supplierId}/catalog?orgId={orgId}");
        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetSupplierCatalog_WithValidSupplier_ReturnsCatalogItemList()
    {
        var supplierId = Guid.NewGuid();
        var orgId = Guid.NewGuid();

        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/internal/visual/suppliers/{supplierId}/catalog?orgId={orgId}&category=saree&color=emerald");
        request.Headers.Add("X-Internal-Token", InternalKey);

        var response = await _client.SendAsync(request);

        response.StatusCode.Should()
            .Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<SupplierCatalogItemDto>>();
        items.Should().NotBeNull();
        items!.Should().NotBeEmpty();
    }
}
