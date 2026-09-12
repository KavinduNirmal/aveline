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
        result.Fabric.Should().Be("silk");
        result.ConfidenceScore.Should().BeGreaterThan(0.9);
        result.SuggestedKeywords.Should().Contain(new[] { "saree", "emerald", "silk" });
    }

    [Fact]
    public async Task AnalyzeAsync_WithDressImageUrl_DetectsDressCategory()
    {
        var config = new ConfigurationBuilder().Build();
        var service = new VisionService(new HttpClient(), config, NullLogger<VisionService>.Instance);
        var result = await service.AnalyzeAsync("https://example.com/crimson-velvet-dress.jpg", Guid.NewGuid());

        result.Should().NotBeNull();
        result.Category.Should().Be("dress");
        result.Fabric.Should().Be("velvet");
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

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(response);
        }
    }
}

