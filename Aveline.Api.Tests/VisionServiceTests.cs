using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using Aveline.Api.Modules.VisualIntelligence.Services;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
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
}
