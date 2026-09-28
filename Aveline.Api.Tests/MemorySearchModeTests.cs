using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Common;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.CustomerConcierge.Services;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// The search mode vocabulary and the service behaviour it drives, without a database. The hybrid
/// SQL itself needs Postgres (<see cref="CustomerMemoryRepositoryPostgresTests"/>); what is
/// testable here is that a mode is validated rather than silently defaulted, that an absent mode is
/// hybrid, and that the service only pays for an embedding call on the legs that need one.
/// </summary>
public class MemorySearchModeTests
{
    // ------------------------------------------------------------------ the vocabulary

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Normalise_WithAnAbsentMode_MeansHybrid(string? mode)
        => MemorySearchModes.Normalise(mode).Should().Be(MemorySearchModes.Hybrid);

    [Theory]
    [InlineData("hybrid", "hybrid")]
    [InlineData("HYBRID", "hybrid")]
    [InlineData(" lexical ", "lexical")]
    [InlineData("Vector", "vector")]
    public void Normalise_WithAKnownMode_ReturnsTheCanonicalForm(string mode, string expected)
        => MemorySearchModes.Normalise(mode).Should().Be(expected);

    [Theory]
    [InlineData("bm25")]
    [InlineData("dense")]
    [InlineData("keyword")]
    public void Normalise_WithAnUnknownMode_ThrowsRatherThanDefaulting(string mode)
    {
        var act = () => MemorySearchModes.Normalise(mode);

        act.Should().Throw<ArgumentException>("a misspelled mode must not be read as hybrid");
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("hybrid", true)]
    [InlineData("LEXICAL", true)]
    [InlineData("vector", true)]
    [InlineData("bm25", false)]
    public void IsValid_MatchesTheVocabulary(string? mode, bool expected)
        => MemorySearchModes.IsValid(mode).Should().Be(expected);

    // ------------------------------------------------------------------ the service

    private static CustomerMemoryService BuildService(
        Mock<ICustomerMemoryRepository> memories,
        Mock<IEmbeddingService> embedding)
    {
        var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"MemorySearchMode_{Guid.NewGuid()}")
            .Options);

        var gate = new Mock<IConsentGateService>();
        gate.Setup(g => g.CheckAsync(
                It.IsAny<Guid>(), It.IsAny<Guid?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(ConsentDecision.Process("granted"));

        return new CustomerMemoryService(
            context,
            memories.Object,
            Mock.Of<ICustomerRepository>(),
            Mock.Of<ICustomerEventRepository>(),
            gate.Object,
            Mock.Of<ICustomerTagRepository>(),
            embedding.Object,
            NullLogger<CustomerMemoryService>.Instance);
    }

    private static Mock<ICustomerMemoryRepository> CapturingRepository(
        Action<CustomerMemorySearchQuery> onSearch)
    {
        var repository = new Mock<ICustomerMemoryRepository>();
        repository
            .Setup(r => r.SearchAsync(It.IsAny<CustomerMemorySearchQuery>(), It.IsAny<CancellationToken>()))
            .Callback<CustomerMemorySearchQuery, CancellationToken>((query, _) => onSearch(query))
            .ReturnsAsync(new List<CustomerMemorySearchResult>());
        return repository;
    }

    private static MemorySearchRequest Request(string? mode = null) => new()
    {
        OrganizationId = Guid.NewGuid(),
        CustomerId = Guid.NewGuid(),
        Query = "banarasi silk",
        Mode = mode ?? MemorySearchModes.Hybrid,
    };

    [Fact]
    public async Task SearchAsync_WithAnAbsentMode_AsksForHybrid()
    {
        CustomerMemorySearchQuery? captured = null;
        var memories = CapturingRepository(query => captured = query);
        var embedding = new Mock<IEmbeddingService>();
        embedding
            .Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new float[1536]);

        await BuildService(memories, embedding).SearchAsync(Request(mode: null));

        captured.Should().NotBeNull();
        captured!.Mode.Should().Be(MemorySearchModes.Hybrid);
    }

    [Fact]
    public async Task SearchAsync_WithAnUnknownMode_IsRefusedBeforeReadingAnything()
    {
        var searched = false;
        var memories = CapturingRepository(_ => searched = true);
        var embedding = new Mock<IEmbeddingService>();

        var act = () => BuildService(memories, embedding).SearchAsync(Request(mode: "bm25"));

        await act.Should().ThrowAsync<ArgumentException>();
        searched.Should().BeFalse("the mode is validated before the store is read");
        embedding.Verify(
            e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task SearchAsync_WithLexicalMode_DoesNotPayForAnEmbeddingCall()
    {
        var memories = CapturingRepository(_ => { });
        var embedding = new Mock<IEmbeddingService>();

        await BuildService(memories, embedding).SearchAsync(Request(mode: MemorySearchModes.Lexical));

        embedding.Verify(
            e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never,
            "the lexical leg has no dense half, so the embedding call would be wasted spend");
    }

    [Fact]
    public async Task SearchAsync_WhenEmbeddingFails_PassesNoVectorSoTheRepositoryDegrades()
    {
        CustomerMemorySearchQuery? captured = null;
        var memories = CapturingRepository(query => captured = query);
        var embedding = new Mock<IEmbeddingService>();
        embedding
            .Setup(e => e.GenerateAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("embedding provider unavailable"));

        // The request is not failed: partial (lexical) results beat an error page, and the NULL
        // vector ranks make the degradation visible (mirrors HandbookService.TryEmbedQueryAsync).
        await BuildService(memories, embedding).SearchAsync(Request(mode: MemorySearchModes.Hybrid));

        captured.Should().NotBeNull();
        captured!.QueryEmbedding.Should().BeNull();
        captured.Mode.Should().Be(MemorySearchModes.Hybrid);
    }
}
