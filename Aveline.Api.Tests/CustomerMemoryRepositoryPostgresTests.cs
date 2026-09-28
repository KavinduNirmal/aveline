using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Postgres-backed integration tests for the hybrid memory search (Testcontainers). These verify
/// the real <c>embedding vector(1536)</c> column, the generated <c>SearchVector tsvector</c> column
/// and its GIN index, per-leg ordering, the Reciprocal Rank Fusion path, and the tenant /
/// soft-delete / expiry / similarity-floor predicates in every mode - none of which the in-memory
/// provider can exercise (ADR-017, ADR-025).
/// </summary>
public class CustomerMemoryRepositoryPostgresTests : IAsyncLifetime
{
    /// <summary>The lexical query. `websearch_to_tsquery` ANDs its terms, so only a note carrying both matches.</summary>
    private const string QueryText = "banarasi silk";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private AppDbContext? _context;
    private readonly ITestOutputHelper _output;

    public CustomerMemoryRepositoryPostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _context = NewContext();

        // pgvector must be enabled before the migration can add a vector(1536) column.
        await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private AppDbContext NewContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(),
                npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options);

    private static float[] Embedding(int axis, float magnitude = 1f)
    {
        // A 1536-d vector that is +magnitude on `axis`, near 0 elsewhere, so cosine similarity
        // is well-separated by axis for deterministic assertions.
        var vector = new float[1536];
        vector[axis] = magnitude;
        return vector;
    }

    private static CustomerMemorySearchQuery Query(
        Guid orgId,
        Guid customerId,
        string text,
        float[]? embedding,
        string mode = "hybrid",
        int topK = 5,
        double minSimilarity = 0.0)
        => new(text, embedding, mode, orgId, customerId, topK, minSimilarity);

    private static async Task<CustomerMemory> AddAsync(
        CustomerMemoryRepository sut,
        Guid orgId,
        Guid customerId,
        string content,
        string category,
        float[]? embedding)
    {
        var memory = await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId,
            CustomerId = customerId,
            Content = content,
            Category = category,
        });

        if (embedding is not null)
        {
            await sut.UpdateEmbeddingAsync(orgId, memory.Id, embedding);
        }

        return memory;
    }

    /// <summary>
    /// Seeds a User + Organization + Customer (FK chain) and returns their ids, scoped to a
    /// fresh org per call so tests are isolated.
    /// </summary>
    private async Task<(Guid OrgId, Guid CustomerId)> SeedCustomerAsync(string phone)
    {
        var user = new User
        {
            ClerkId = $"user_{Guid.NewGuid():N}",
            FirstName = "Test",
            LastName = "Owner",
            Email = "owner@aveline.test",
            Username = $"owner_{Guid.NewGuid():N}",
            OrganizationId = "org_legacy",
            PhoneNumber = "+94770000000",
            UserRole = "owner",
            OrganizationRole = "org:owner",
        };
        _context!.Users.Add(user);

        var org = new Organization
        {
            Name = "Test Boutique",
            Slug = $"boutique-{Guid.NewGuid():N}",
            OwnerUserId = user.Id,
        };
        _context.Organizations.Add(org);

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = phone,
            FullName = "Sarah Perera",
        };
        _context.Customers.Add(customer);

        await _context.SaveChangesAsync();
        return (org.Id, customer.Id);
    }

    // ------------------------------------------------------------------ schema

    [Fact]
    public async Task The_migration_creates_the_search_vector_column_and_a_partial_gin_index()
    {
        await using var context = NewContext();

        var columns = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT column_name AS "Value"
                FROM information_schema.columns
                WHERE table_name = 'CustomerMemory' AND column_name = 'SearchVector'
                """)
            .ToListAsync();
        Assert.Equal(new[] { "SearchVector" }, columns);

        var indexDefinitions = await context.Database
            .SqlQueryRaw<string>(
                """
                SELECT indexdef AS "Value"
                FROM pg_indexes
                WHERE tablename = 'CustomerMemory' AND indexname = 'IX_CustomerMemory_SearchVector'
                """)
            .ToListAsync();

        var definition = Assert.Single(indexDefinitions);
        Assert.Contains("gin", definition, StringComparison.OrdinalIgnoreCase);
        // Partial on live rows: every leg filters DeletedAt, so a withdrawn note is not indexed.
        Assert.Contains("\"DeletedAt\" IS NULL", definition);
    }

    // ------------------------------------------------------------------ vector leg

    [Fact]
    public async Task SearchAsync_VectorMode_ReturnsNearestMemories_ByCosineSimilarity()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        var (_, otherCustomerId) = await SeedCustomerAsync("+94779876543");

        // Three memories for this customer, each with a distinct, well-separated embedding axis.
        var wedding = await AddAsync(sut, orgId, customerId,
            "Sarah has a wedding on Saturday", "event", Embedding(0));
        await AddAsync(sut, orgId, customerId,
            "Sarah prefers emerald silk", "preference", Embedding(1));
        await AddAsync(sut, orgId, customerId,
            "Loves party sarees", "preference", Embedding(2));
        // A memory belonging to another customer that must never appear in results.
        await AddAsync(sut, orgId, otherCustomerId,
            "Different customer's wedding", "event", Embedding(0));

        // Query nearest to axis 0 -> the wedding memory should rank first.
        var results = await sut.SearchAsync(
            Query(orgId, customerId, "wedding", Embedding(0), mode: "vector", topK: 3));

        Assert.NotEmpty(results);
        Assert.Equal(wedding.Id, results[0].Id);
        Assert.Equal("Sarah has a wedding on Saturday", results[0].Content);
        Assert.True(results[0].Similarity > 0.99);
        Assert.Equal(1, results[0].VectorRank);
        Assert.Null(results[0].LexicalRank);
        Assert.All(results, r => Assert.Equal(customerId, r.CustomerId));
    }

    [Fact]
    public async Task SearchAsync_VectorMode_ReturnsRequestedNumberOfResults()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        for (var i = 0; i < 5; i++)
        {
            await AddAsync(sut, orgId, customerId, $"Memory {i}", "fact", Embedding(i));
        }

        var results = await sut.SearchAsync(
            Query(orgId, customerId, "Memory", Embedding(0), mode: "vector", topK: 2));

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchAsync_VectorMode_EmptyResult_WhenNoEmbeddings()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        await AddAsync(sut, orgId, customerId, "No embedding attached", "fact", embedding: null);

        var results = await sut.SearchAsync(
            Query(orgId, customerId, "embedded", Embedding(0), mode: "vector"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_VectorMode_WithNoEmbedding_ReturnsNothingRatherThanAnsweringLexically()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        await AddAsync(sut, orgId, customerId, "Wants a banarasi silk saree", "preference", Embedding(0));

        // An explicit vector request with no vector cannot answer; answering lexically would
        // misreport what was measured.
        var results = await sut.SearchAsync(
            Query(orgId, customerId, QueryText, embedding: null, mode: "vector"));

        Assert.Empty(results);
    }

    [Fact]
    public async Task SearchAsync_VectorMode_AppliesTheSimilarityFloor()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        var near = await AddAsync(sut, orgId, customerId, "Emerald silk", "preference", Embedding(0));
        await AddAsync(sut, orgId, customerId, "Unrelated note", "fact", Embedding(7));

        var results = await sut.SearchAsync(
            Query(orgId, customerId, "silk", Embedding(0), mode: "vector", minSimilarity: 0.5));

        var hit = Assert.Single(results);
        Assert.Equal(near.Id, hit.Id);
        Assert.Equal(1.0, hit.Similarity!.Value, precision: 3);
    }

    // ------------------------------------------------------------------ lexical leg

    [Fact]
    public async Task SearchAsync_LexicalMode_FindsAnExactTokenTheDenseLegRanksPoorly()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        var exact = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(7));
        await AddAsync(sut, orgId, customerId,
            "Prefers emerald evening gowns", "preference", Embedding(0));

        var results = await sut.SearchAsync(
            Query(orgId, customerId, QueryText, embedding: null, mode: "lexical"));

        var hit = Assert.Single(results);
        Assert.Equal(exact.Id, hit.Id);
        Assert.Equal(1, hit.LexicalRank);
        Assert.Null(hit.VectorRank);
        Assert.Null(hit.Similarity);
    }

    [Fact]
    public async Task SearchAsync_LexicalMode_IgnoresTheSimilarityFloor()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        // Cosine 0 against the query axis, so the dense leg would drop it at any real floor; the
        // lexical leg has no cosine to floor and must still return the exact token.
        var exact = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(7));

        var results = await sut.SearchAsync(
            Query(orgId, customerId, QueryText, embedding: null, mode: "lexical", minSimilarity: 0.99));

        var hit = Assert.Single(results);
        Assert.Equal(exact.Id, hit.Id);
        Assert.Null(hit.Similarity);
    }

    // ------------------------------------------------------------------ hybrid fusion

    [Fact]
    public async Task SearchAsync_HybridMode_SurfacesAHitOnlyTheLexicalLegFinds()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        // The dense winner shares no vocabulary with the query; the lexical winner is a poor cosine
        // match (so the 0.5 floor drops it from the dense leg) but carries the exact tokens. Only a
        // fused query returns both, which is the recall claim the hybrid exists to make.
        var dense = await AddAsync(sut, orgId, customerId,
            "Prefers emerald evening gowns", "preference", Embedding(0));
        var lexical = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(7));

        var results = await sut.SearchAsync(
            Query(orgId, customerId, QueryText, Embedding(0), mode: "hybrid", minSimilarity: 0.5));

        Assert.Equal(2, results.Count);

        var lexicalHit = Assert.Single(results, r => r.Id == lexical.Id);
        Assert.Null(lexicalHit.Similarity);
        Assert.Null(lexicalHit.VectorRank);
        Assert.Equal(1, lexicalHit.LexicalRank);

        var denseHit = Assert.Single(results, r => r.Id == dense.Id);
        Assert.Equal(1, denseHit.VectorRank);
        Assert.Null(denseHit.LexicalRank);
        Assert.True(denseHit.Similarity > 0.99);
    }

    [Fact]
    public async Task SearchAsync_HybridMode_WithNoEmbedding_DegradesToTheLexicalLeg()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        var exact = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(0));

        var results = await sut.SearchAsync(
            Query(orgId, customerId, QueryText, embedding: null, mode: "hybrid"));

        var hit = Assert.Single(results);
        Assert.Equal(exact.Id, hit.Id);
        Assert.Null(hit.VectorRank);
        Assert.Equal(1, hit.LexicalRank);
    }

    // ------------------------------------------------------------------ scoping and liveness in every mode

    [Theory]
    [InlineData("hybrid")]
    [InlineData("lexical")]
    [InlineData("vector")]
    public async Task SearchAsync_InEveryMode_ExcludesAnotherCustomersMemory(string mode)
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        var (_, otherCustomerId) = await SeedCustomerAsync("+94779876543");

        var mine = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(0));
        await AddAsync(sut, orgId, otherCustomerId,
            "Wants a Banarasi silk saree", "preference", Embedding(0));

        var results = await sut.SearchAsync(Query(orgId, customerId, QueryText, Embedding(0), mode));

        var hit = Assert.Single(results);
        Assert.Equal(mine.Id, hit.Id);
        Assert.All(results, r => Assert.Equal(customerId, r.CustomerId));
    }

    [Theory]
    [InlineData("hybrid")]
    [InlineData("lexical")]
    [InlineData("vector")]
    public async Task SearchAsync_InEveryMode_ExcludesASoftDeletedMemory(string mode)
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        var live = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(0));
        var withdrawn = await AddAsync(sut, orgId, customerId,
            "Also wants a Banarasi silk saree", "preference", Embedding(0));
        withdrawn.DeletedAt = DateTime.UtcNow;
        await _context!.SaveChangesAsync();

        var results = await sut.SearchAsync(Query(orgId, customerId, QueryText, Embedding(0), mode));

        var hit = Assert.Single(results);
        Assert.Equal(live.Id, hit.Id);
        Assert.DoesNotContain(results, r => r.Id == withdrawn.Id);
    }

    [Theory]
    [InlineData("hybrid")]
    [InlineData("lexical")]
    [InlineData("vector")]
    public async Task SearchAsync_InEveryMode_ExcludesAnExpiredMemory(string mode)
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        var live = await AddAsync(sut, orgId, customerId,
            "Wants a Banarasi silk saree", "preference", Embedding(0));
        var expired = await AddAsync(sut, orgId, customerId,
            "Had a Banarasi silk fitting last week", "event", Embedding(0));
        expired.ExpiresAt = DateTime.UtcNow.AddDays(-1);
        await _context!.SaveChangesAsync();

        var results = await sut.SearchAsync(Query(orgId, customerId, QueryText, Embedding(0), mode));

        var hit = Assert.Single(results);
        Assert.Equal(live.Id, hit.Id);
        Assert.DoesNotContain(results, r => r.Id == expired.Id);
    }

    // ------------------------------------------------------------------ validation

    [Fact]
    public async Task SearchAsync_WithAnUnknownMode_IsRejectedRatherThanDefaulted()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        await Assert.ThrowsAsync<ArgumentException>(
            () => sut.SearchAsync(Query(orgId, customerId, QueryText, Embedding(0), mode: "bm25")));
    }
}
