using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

public class QrCodeServiceTests
{
    private readonly Mock<IInventoryRepository> _repoMock;
    private readonly QrCodeService _service;
    private readonly Guid _orgId = Guid.NewGuid();

    public QrCodeServiceTests()
    {
        _repoMock = new Mock<IInventoryRepository>();
        _service = new QrCodeService(_repoMock.Object);
    }

    [Fact]
    public void GeneratePng_ReturnsValidPngHeaderAndBytes()
    {
        // Act
        var bytes = _service.GeneratePng("https://aveline.app", size: 250);

        // Assert
        bytes.Should().NotBeNull();
        bytes.Length.Should().BeGreaterThan(50);
        // PNG magic bytes: 0x89 0x50 0x4E 0x47 0x0D 0x0A 0x1A 0x0A
        bytes[0].Should().Be(0x89);
        bytes[1].Should().Be(0x50); // 'P'
        bytes[2].Should().Be(0x4E); // 'N'
        bytes[3].Should().Be(0x47); // 'G'
    }

    [Fact]
    public void GenerateSvg_ReturnsValidXmlSvgString()
    {
        // Act
        var svg = _service.GenerateSvg("https://aveline.app", size: 250);

        // Assert
        svg.Should().NotBeNullOrWhiteSpace();
        svg.Should().Contain("<svg");
        svg.Should().Contain("</svg>");
    }

    [Fact]
    public void GenerateQrResponse_WithSvgFormat_ReturnsSvgDataUrl()
    {
        // Act
        var response = _service.GenerateQrResponse(new GenerateQrDto
        {
            Payload = "aveline://test",
            Format = "svg",
            Size = 200
        });

        // Assert
        response.Should().NotBeNull();
        response.Payload.Should().Be("aveline://test");
        response.Format.Should().Be("svg");
        response.Svg.Should().Contain("<svg");
        response.DataUrl.Should().StartWith("data:image/svg+xml;utf8,");
    }

    [Fact]
    public void GenerateQrResponse_WithPngFormat_ReturnsBase64AndDataUrl()
    {
        // Act
        var response = _service.GenerateQrResponse(new GenerateQrDto
        {
            Payload = "aveline://png-test",
            Format = "png",
            Size = 200
        });

        // Assert
        response.Should().NotBeNull();
        response.Payload.Should().Be("aveline://png-test");
        response.Base64.Should().NotBeNullOrWhiteSpace();
        response.DataUrl.Should().StartWith("data:image/png;base64,");
    }

    [Fact]
    public async Task GenerateItemQrDtoAsync_WithExistingItem_ReturnsStructuredPayload()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = itemId,
            OrgId = _orgId,
            ItemName = "Silk Velvet Blazer",
            Sku = "BLZ-SLK-001",
            Price = 450.00m,
            StockQuantity = 5,
            Category = "Blazers"
        };

        _repoMock.Setup(r => r.GetByIdAsync(itemId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var response = await _service.GenerateItemQrDtoAsync(_orgId, itemId, "json", 300);

        // Assert
        response.Should().NotBeNull();
        response.Payload.Should().Contain(itemId.ToString());
        response.Payload.Should().Contain("BLZ-SLK-001");
        response.Payload.Should().Contain("aveline_inventory_item");
    }

    [Fact]
    public async Task ScanAndResolveAsync_WithStructuredJson_ResolvesItem()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = itemId,
            OrgId = _orgId,
            ItemName = "Cashmere Wrap Coat",
            Sku = "COT-CSH-002",
            Price = 890.00m,
            StockQuantity = 3,
            Category = "Outerwear"
        };

        _repoMock.Setup(r => r.GetByIdAsync(itemId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var jsonPayload = JsonSerializer.Serialize(new ItemQrPayload
        {
            Type = "aveline_inventory_item",
            OrgId = _orgId,
            ItemId = itemId,
            Sku = "COT-CSH-002"
        });

        // Act
        var result = await _service.ScanAndResolveAsync(_orgId, new ScanQrDto { Code = jsonPayload });

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Found.Should().BeTrue();
        result.ScanType.Should().Be("InventoryItem");
        result.Item.Should().NotBeNull();
        result.Item!.Id.Should().Be(itemId);
        result.Item.ItemName.Should().Be("Cashmere Wrap Coat");
    }

    [Fact]
    public async Task ScanAndResolveAsync_WithDirectGuid_ResolvesItem()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = itemId,
            OrgId = _orgId,
            ItemName = "Satin Midi Skirt",
            Price = 180.00m,
            StockQuantity = 10,
            Category = "Skirts"
        };

        _repoMock.Setup(r => r.GetByIdAsync(itemId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _service.ScanAndResolveAsync(_orgId, new ScanQrDto { Code = itemId.ToString() });

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Found.Should().BeTrue();
        result.Item!.ItemName.Should().Be("Satin Midi Skirt");
    }

    [Fact]
    public async Task ScanAndResolveAsync_WithSku_ResolvesItem()
    {
        // Arrange
        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = _orgId,
            ItemName = "Pleated Chiffon Gown",
            Sku = "GWN-CHF-999",
            Price = 620.00m,
            StockQuantity = 2,
            Category = "Eveningwear"
        };

        _repoMock.Setup(r => r.GetBySkuAsync("GWN-CHF-999", _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        // Act
        var result = await _service.ScanAndResolveAsync(_orgId, new ScanQrDto { Code = "GWN-CHF-999" });

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Found.Should().BeTrue();
        result.Item!.ItemName.Should().Be("Pleated Chiffon Gown");
        result.Item.Sku.Should().Be("GWN-CHF-999");
    }

    [Fact]
    public async Task ScanAndResolveAsync_WithDeepLinkUrl_ResolvesItem()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var item = new InventoryItem
        {
            Id = itemId,
            OrgId = _orgId,
            ItemName = "Tailored Wool Trousers",
            Price = 240.00m,
            StockQuantity = 7,
            Category = "Trousers"
        };

        _repoMock.Setup(r => r.GetByIdAsync(itemId, _orgId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(item);

        var url = $"https://aveline.app/app/b/luxury-boutique/catalog/items/{itemId}?source=qr";

        // Act
        var result = await _service.ScanAndResolveAsync(_orgId, new ScanQrDto { Code = url });

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Found.Should().BeTrue();
        result.Item!.Id.Should().Be(itemId);
    }

    [Fact]
    public async Task ScanAndResolveAsync_WithCrossTenantOrgId_ReturnsFoundFalse()
    {
        // Arrange
        var itemId = Guid.NewGuid();
        var otherOrgId = Guid.NewGuid();

        var jsonPayload = JsonSerializer.Serialize(new ItemQrPayload
        {
            Type = "aveline_inventory_item",
            OrgId = otherOrgId,
            ItemId = itemId,
            Sku = "SKU-CROSS-001"
        });

        // Act
        var result = await _service.ScanAndResolveAsync(_orgId, new ScanQrDto { Code = jsonPayload });

        // Assert
        result.Should().NotBeNull();
        result.Found.Should().BeFalse();
        result.Message.Should().Contain("different boutique organization");
        _repoMock.Verify(r => r.GetByIdAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ScanAndResolveAsync_WithUnknownPayload_ReturnsGenericPayloadResult()
    {
        // Act
        var result = await _service.ScanAndResolveAsync(_orgId, new ScanQrDto { Code = "random-unmatched-string" });

        // Assert
        result.Should().NotBeNull();
        result.Success.Should().BeTrue();
        result.Found.Should().BeFalse();
        result.ScanType.Should().Be("GenericPayload");
    }
}
