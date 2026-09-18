using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

public class VisionServiceTests
{
    [Fact]
    public async Task AnalyzeAsync_WithEmptyImageUrl_ThrowsArgumentException()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);

        var act = () => service.AnalyzeAsync("", Guid.NewGuid());
        await act.Should().ThrowAsync<ArgumentException>();
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutApiKey_ReturnsDeterministicAnalysis()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", null }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/emerald-silk-saree.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("saree");
        result.PrimaryColor.Should().Be("emerald");
        result.Fabric.Should().Be("Pure Mulberry Silk");
        result.ConfidenceScore.Should().BeGreaterThan(0.9);
        result.SuggestedKeywords.Should().Contain("saree");
        result.SuggestedKeywords.Should().Contain("emerald");
    }

    [Fact]
    public async Task AnalyzeAsync_WithDressImageUrl_DetectsDressCategory()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/crimson-velvet-dress.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("dress");
        result.Fabric.Should().Be("Micro Velvet");
        result.ConfidenceScore.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task AnalyzeAsync_WithApiKeyAndSuccessfulResponse_ParsesTokensAndRecordsAdr010Usage()
    {
        var orgId = Guid.NewGuid();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", "test-vision-key" },
            { "Vision:Model", "gpt-4o-mini" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var jsonResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": "{\"category\":\"lehenga\",\"primary_color\":\"royal-blue\",\"secondary_colors\":[\"silver\"],\"pattern\":\"zari\",\"style\":\"bridal\",\"fabric\":\"silk\",\"confidence_score\":0.98,\"suggested_keywords\":[\"lehenga\",\"blue\",\"bridal\"]}"
                    }
                }
            ],
            "usage": {
                "prompt_tokens": 150,
                "completion_tokens": 42,
                "total_tokens": 192
            }
        }
        """;

        var fakeHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("https://api.openai.com") };

        var mockUsageTracker = new Mock<IUsageTrackerService>();
        RecordUsageRequest? capturedRequest = null;
        mockUsageTracker
            .Setup(u => u.RecordWorkflowUsageAsync(It.IsAny<RecordUsageRequest>(), It.IsAny<CancellationToken>()))
            .Callback<RecordUsageRequest, CancellationToken>((req, _) => capturedRequest = req)
            .ReturnsAsync(new AiUsageRecord());

        var service = new VisionService(httpClient, config, NullLogger<VisionService>.Instance, mockUsageTracker.Object);
        var result = await service.AnalyzeAsync("https://example.com/custom-garment.jpg", orgId);

        result.Should().NotBeNull();
        result.Category.Should().Be("lehenga");
        result.PrimaryColor.Should().Be("royal-blue");
        result.Pattern.Should().Be("zari");
        result.ConfidenceScore.Should().Be(0.98);

        mockUsageTracker.Verify(u => u.RecordWorkflowUsageAsync(It.IsAny<RecordUsageRequest>(), It.IsAny<CancellationToken>()), Times.Once);
        capturedRequest.Should().NotBeNull();
        capturedRequest!.OrganizationId.Should().Be(orgId);
        capturedRequest.WorkflowId.Should().Be("visual-image-analysis");
        capturedRequest.Model.Should().Be("gpt-4o-mini");
        capturedRequest.InputTokens.Should().Be(150);
        capturedRequest.OutputTokens.Should().Be(42);
    }

    [Fact]
    public async Task AnalyzeAsync_WhenUsageTrackerFails_DoesNotFailAnalysis()
    {
        var orgId = Guid.NewGuid();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", "test-vision-key" }
        };
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(inMemorySettings)
            .Build();

        var jsonResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": "{\"category\":\"saree\",\"primary_color\":\"gold\",\"secondary_colors\":[],\"pattern\":\"plain\",\"style\":\"traditional\",\"fabric\":\"cotton\",\"confidence_score\":0.9,\"suggested_keywords\":[\"saree\"]}"
                    }
                }
            ],
            "usage": {
                "prompt_tokens": 100,
                "completion_tokens": 20
            }
        }
        """;

        var fakeHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("https://api.openai.com") };

        var mockUsageTracker = new Mock<IUsageTrackerService>();
        mockUsageTracker
            .Setup(u => u.RecordWorkflowUsageAsync(It.IsAny<RecordUsageRequest>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("DB unavailable"));

        var service = new VisionService(httpClient, config, NullLogger<VisionService>.Instance, mockUsageTracker.Object);
        var result = await service.AnalyzeAsync("https://example.com/gold-saree.jpg", orgId);

        result.Should().NotBeNull();
        result.Category.Should().Be("saree");
        result.PrimaryColor.Should().Be("gold");
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutApiKey_WithLavenderImageUrl_DetectsPastelThemeAndHex()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/lavender-silk-lehenga.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("lehenga");
        result.PrimaryColor.Should().Be("lavender");
        result.ColorHex.Should().Be("#c8a2c8");
        result.SuggestedKeywords.Should().Contain(new[] { "lehenga", "lavender", "Pastels" });
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutApiKey_WithTerracottaImageUrl_DetectsEarthyThemeAndHex()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/terracotta-linen-dress.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("dress");
        result.PrimaryColor.Should().Be("terracotta");
        result.ColorHex.Should().Be("#e2725b");
        result.Fabric.Should().Be("Handloom Linen");
        result.SuggestedKeywords.Should().Contain(new[] { "dress", "terracotta", "Earthy Neutrals" });
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutApiKey_WithBurgundyImageUrl_DetectsRichBerriesThemeAndHex()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/burgundy-velvet-gown.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("gown");
        result.PrimaryColor.Should().Be("royal burgundy");
        result.ColorHex.Should().Be("#800020");
        result.Fabric.Should().Be("Micro Velvet");
        result.SuggestedKeywords.Should().Contain(new[] { "gown", "royal burgundy", "Rich Berries" });
    }

    [Fact]
    public async Task AnalyzeAsync_WithoutApiKey_WithTealImageUrl_DetectsOceanicThemeAndHex()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/teal-silk-saree.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("saree");
        result.PrimaryColor.Should().Be("teal");
        result.ColorHex.Should().Be("#008080");
        result.SuggestedKeywords.Should().Contain(new[] { "saree", "teal", "Oceanic Spectrum" });
    }

    [Fact]
    public async Task AnalyzeAsync_WithApiKeyAndColorThemeResponse_ExtractsThemeAndUndertoneIntoKeywords()
    {
        var orgId = Guid.NewGuid();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", "test-vision-key" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var jsonResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": "{\"category\":\"gown\",\"primary_color\":\"Dusty Sage\",\"color_hex\":\"#8A9A86\",\"color_theme\":\"Pastels\",\"undertone\":\"Cool\",\"secondary_colors\":[\"cream\"],\"pattern\":\"embroidered\",\"style\":\"couture\",\"fabric\":\"organza\",\"confidence_score\":0.97,\"suggested_keywords\":[\"gown\",\"sage\"]}"
                    }
                }
            ],
            "usage": { "prompt_tokens": 80, "completion_tokens": 30 }
        }
        """;

        var fakeHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("https://api.openai.com") };

        var service = new VisionService(httpClient, config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/sage-organza-gown.jpg", orgId);

        result.Should().NotBeNull();
        result.PrimaryColor.Should().Be("Dusty Sage");
        result.ColorHex.Should().Be("#8A9A86");
        result.SuggestedKeywords.Should().Contain(new[] { "Pastels", "Cool Undertone" });
    }

    [Fact]
    public async Task AnalyzeAsync_WithApiKeyAndGarmentTypeResponse_ParsesGarmentTypeAndSuggestedName()
    {
        var orgId = Guid.NewGuid();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", "test-vision-key" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var jsonResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": "{\"category\":\"Sarees\",\"garment_type\":\"Kanjeevaram Silk Saree\",\"suggested_item_name\":\"Royal Emerald Zari Brocade Silk Saree\",\"primary_color\":\"Emerald Green\",\"color_hex\":\"#0F5132\",\"color_theme\":\"Jewel Tones\",\"fabric\":\"Mulberry Silk\",\"pattern\":\"Gold Zari Brocade\",\"confidence_score\":0.99,\"suggested_keywords\":[\"Sarees\",\"Kanjeevaram Silk Saree\",\"Silk\"]}"
                    }
                }
            ],
            "usage": { "prompt_tokens": 100, "completion_tokens": 50 }
        }
        """;

        var fakeHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("https://api.openai.com") };

        var service = new VisionService(httpClient, config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/emerald-saree.jpg", orgId);

        result.Should().NotBeNull();
        result.Category.Should().Be("Sarees");
        result.GarmentType.Should().Be("Kanjeevaram Silk Saree");
        result.SuggestedItemName.Should().Be("Royal Emerald Zari Brocade Silk Saree");
        result.PrimaryColor.Should().Be("Emerald Green");
        result.ColorHex.Should().Be("#0F5132");
        result.SuggestedKeywords.Should().Contain(new[] { "Kanjeevaram Silk Saree", "Jewel Tones" });
    }

    [Fact]
    public async Task AnalyzeAsync_DeterministicFallback_PopulatesGarmentTypeAndSuggestedName()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", null }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/lavender-silk-lehenga.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.GarmentType.Should().Be("Embroidered Bridal Lehenga");
        result.SuggestedItemName.Should().NotBeNullOrWhiteSpace();
        result.SuggestedItemName.Should().Contain("Lavender");
        result.SuggestedKeywords.Should().Contain("Embroidered Bridal Lehenga");
    }

    [Fact]
    public async Task AnalyzeAsync_WithApiKeyAndGownResponse_ParsesSilhouetteAndDescription()
    {
        var orgId = Guid.NewGuid();
        var inMemorySettings = new Dictionary<string, string?>
        {
            { "Vision:ApiKey", "test-vision-key" }
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();

        var jsonResponse = """
        {
            "choices": [
                {
                    "message": {
                        "content": "{\"category\":\"Gowns\",\"garment_type\":\"Luminous Organza Evening Gown\",\"suggested_item_name\":\"Dusty Rose Luminous Organza Evening Gown\",\"primary_color\":\"Dusty Rose\",\"color_hex\":\"#DCAE96\",\"color_theme\":\"Pastels\",\"fabric\":\"Pure Organza\",\"pattern\":\"Botanical Floral Embroidery\",\"style\":\"Contemporary Luxe\",\"confidence_score\":0.98,\"suggested_keywords\":[\"Gowns\",\"Luminous Organza Evening Gown\",\"Organza\"]}"
                    }
                }
            ],
            "usage": { "prompt_tokens": 120, "completion_tokens": 60 }
        }
        """;

        var fakeHandler = new FakeHttpMessageHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(jsonResponse, Encoding.UTF8, "application/json")
        });
        var httpClient = new HttpClient(fakeHandler) { BaseAddress = new Uri("https://api.openai.com") };

        var service = new VisionService(httpClient, config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/rose-gown.jpg", orgId);

        result.Should().NotBeNull();
        result.Category.Should().Be("Gowns");
        result.GarmentType.Should().Be("Luminous Organza Evening Gown");
        result.SuggestedItemName.Should().Be("Dusty Rose Luminous Organza Evening Gown");
        result.PrimaryColor.Should().Be("Dusty Rose");
        result.ColorHex.Should().Be("#DCAE96");
        result.Fabric.Should().Be("Pure Organza");
    }

    [Fact]
    public async Task AnalyzeAsync_WithBase64ImageAndFileNameHint_AccuratelyIdentifiesClothCategory()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var base64DataUrl = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEASABIAAD/2wBDAP...";

        var lehengaResult = await service.AnalyzeAsync(base64DataUrl, Guid.NewGuid(), "burgundy_bridal_lehenga.jpg");
        lehengaResult.Category.Should().Be("lehenga");
        lehengaResult.GarmentType.Should().Be("Embroidered Bridal Lehenga");
        lehengaResult.PrimaryColor.Should().Be("royal burgundy");

        var sareeResult = await service.AnalyzeAsync(base64DataUrl, Guid.NewGuid(), "emerald_banarasi_saree.png");
        sareeResult.Category.Should().Be("saree");
        sareeResult.GarmentType.Should().Be("Banarasi Silk Brocade Saree");
        sareeResult.PrimaryColor.Should().Be("emerald");

        var kurtaResult = await service.AnalyzeAsync(base64DataUrl, Guid.NewGuid(), "mustard_anarkali_kurti.jpg");
        kurtaResult.Category.Should().Be("kurta");
        kurtaResult.GarmentType.Should().Be("Anarkali Kurta & Tunic");
        kurtaResult.PrimaryColor.Should().Be("mustard ochre");

        var blazerResult = await service.AnalyzeAsync(base64DataUrl, Guid.NewGuid(), "navy_velvet_blazer.jpeg");
        blazerResult.Category.Should().Be("blazer");
        blazerResult.GarmentType.Should().Be("Structured Velvet Jacket");
        blazerResult.PrimaryColor.Should().Be("navy");

        var shawlResult = await service.AnalyzeAsync(base64DataUrl, Guid.NewGuid(), "cashmere_pashmina_shawl.webp");
        shawlResult.Category.Should().Be("shawl");
        shawlResult.GarmentType.Should().Be("Handwoven Cashmere Shawl");

        var jewelryResult = await service.AnalyzeAsync(base64DataUrl, Guid.NewGuid(), "antique_gold_kundan_necklace.jpg");
        jewelryResult.Category.Should().Be("jewelry");
        jewelryResult.GarmentType.Should().Be("Heirloom Kundan Necklace");
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}

