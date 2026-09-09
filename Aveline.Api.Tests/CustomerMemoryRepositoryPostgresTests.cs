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
/// Postgres-backed integration tests for the pgvector semantic memory search
/// (Testcontainers). These verify the real <c>embedding vector(1536)</c> column, the HNSW
/// index, and cosine-similarity ordering that the in-memory provider cannot exercise.
/// </summary>
public class CustomerMemoryRepositoryPostgresTests : IAsyncLifetime
{
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

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        _context = new AppDbContext(options);

        // pgvector must be enabled before the migration can add a vector(1536) column.
        await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private static float[] Embedding(int axis, float magnitude = 1f)
    {
        // A 1536-d vector that is +magnitude on `axis`, near 0 elsewhere, so cosine similarity
        // is well-separated by axis for deterministic assertions.
        var vector = new float[1536];
        vector[axis] = magnitude;
        return vector;
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

    [Fact]
    public async Task SearchSemanticAsync_ReturnsNearestMemories_ByCosineSimilarity()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        var (_, otherCustomerId) = await SeedCustomerAsync("+94779876543");

        // Three memories for this customer, each with a distinct, well-separated embedding axis.
        var wedding = await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId, CustomerId = customerId,
            Content = "Sarah has a wedding on Saturday", Category = "event",
        });
        var silk = await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId, CustomerId = customerId,
            Content = "Sarah prefers emerald silk", Category = "preference",
        });
        var party = await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId, CustomerId = customerId,
            Content = "Loves party sarees", Category = "preference",
        });
        // A memory belonging to another customer that must never appear in results.
        await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId, CustomerId = otherCustomerId,
            Content = "Different customer's wedding", Category = "event",
        });

        await sut.UpdateEmbeddingAsync(orgId, wedding.Id, Embedding(0));
        await sut.UpdateEmbeddingAsync(orgId, silk.Id, Embedding(1));
        await sut.UpdateEmbeddingAsync(orgId, party.Id, Embedding(2));

        // Query nearest to axis 0 -> the wedding memory should rank first.
        var results = await sut.SearchSemanticAsync(orgId, customerId, Embedding(0), topK: 3);

        Assert.NotEmpty(results);
        Assert.Equal(wedding.Id, results[0].Id);
        Assert.Equal("Sarah has a wedding on Saturday", results[0].Content);
        Assert.True(results[0].Similarity > 0.99);
        Assert.All(results, r => Assert.Equal(customerId, r.CustomerId));
    }

    [Fact]
    public async Task SearchSemanticAsync_ReturnsRequestedNumberOfResults()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");

        for (var i = 0; i < 5; i++)
        {
            var memory = await sut.AddAsync(new CustomerMemory
            {
                OrganizationId = orgId, CustomerId = customerId,
                Content = $"Memory {i}", Category = "fact",
            });
            await sut.UpdateEmbeddingAsync(orgId, memory.Id, Embedding(i));
        }

        var results = await sut.SearchSemanticAsync(orgId, customerId, Embedding(0), topK: 2);

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task SearchSemanticAsync_EmptyResult_WhenNoEmbeddings()
    {
        var sut = new CustomerMemoryRepository(_context!);
        var (orgId, customerId) = await SeedCustomerAsync("+94771234567");
        await sut.AddAsync(new CustomerMemory
        {
            OrganizationId = orgId, CustomerId = customerId,
            Content = "No embedding attached", Category = "fact",
        });

        var results = await sut.SearchSemanticAsync(orgId, customerId, Embedding(0), topK: 5);

        Assert.Empty(results);
    }
}
