using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Testcontainers.PostgreSql;
using Xunit;
using Xunit.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Postgres-backed integration tests for Visual Intelligence &amp; Sourcing (Slice 2)
/// tables, entities, and repositories using Testcontainers.
/// </summary>
public class VisualIntelligencePostgresTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("pgvector/pgvector:pg16")
        .WithDatabase("aveline_test")
        .WithUsername("aveline")
        .WithPassword("aveline_test_pw")
        .Build();

    private AppDbContext? _context;
    private readonly ITestOutputHelper _output;

    public VisualIntelligencePostgresTests(ITestOutputHelper output) => _output = output;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql(_postgres.GetConnectionString(), npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;
        _context = new AppDbContext(options);

        // Enable vector extension then run migrations
        await _context.Database.ExecuteSqlRawAsync("CREATE EXTENSION IF NOT EXISTS vector");
        await _context.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private async Task<(Guid OrgId, Guid CustomerId)> SeedCustomerAsync()
    {
        var user = new User
        {
            ClerkId = $"user_{Guid.NewGuid():N}",
            FirstName = "Test",
            LastName = "Owner",
            Email = $"owner_{Guid.NewGuid():N}@aveline.test",
            Username = $"owner_{Guid.NewGuid():N}",
            OrganizationId = "org_legacy",
            PhoneNumber = "+94770000000",
            UserRole = "owner",
            OrganizationRole = "org:owner",
        };
        _context!.Users.Add(user);

        var org = new Organization
        {
            Name = "Luxury Boutique",
            Slug = $"boutique-{Guid.NewGuid():N}",
            OwnerUserId = user.Id,
        };
        _context.Organizations.Add(org);

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = $"+9477{Random.Shared.Next(1000000, 9999999)}",
            FullName = "Ananya Sharma",
            Status = "vip"
        };
        _context.Customers.Add(customer);

        await _context.SaveChangesAsync();
        return (org.Id, customer.Id);
    }

    [Fact]
    public async Task InventoryRepository_CanInsertAndSearchItems_InRealPostgres()
    {
        var (orgId, _) = await SeedCustomerAsync();
        var repo = new InventoryRepository(_context!);

        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Royal Banarasi Silk Saree",
            Category = "saree",
            Color = "emerald",
            Sizes = new List<string> { "FreeSize" },
            Price = 75000m,
            Quantity = 4,
            Status = "available",
            CreatedAtUtc = DateTime.UtcNow
        };

        await repo.AddAsync(item);

        var searchResults = await repo.SearchAsync(
            orgId: orgId,
            category: "saree",
            color: "emerald",
            size: null,
            minPrice: 50000m,
            maxPrice: 100000m,
            inStockOnly: true,
            page: 1,
            pageSize: 10
        );

        searchResults.Should().NotBeEmpty();
        searchResults[0].Id.Should().Be(item.Id);
        searchResults[0].ItemName.Should().Be("Royal Banarasi Silk Saree");
    }

    [Fact]
    public async Task CustomerMatchRepository_CanPersistAndRetrieveMatches_InRealPostgres()
    {
        var (orgId, customerId) = await SeedCustomerAsync();
        var customerMatchRepo = new CustomerMatchRepository(_context!);

        var itemId = Guid.NewGuid();
        var match = new CustomerMatch
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemId = itemId,
            CustomerId = customerId,
            MatchConfidence = 0.94m,
            MatchReason = "Customer has strong preference for jewel-tone silks",
            CreatedAtUtc = DateTime.UtcNow
        };

        await customerMatchRepo.AddRangeAsync(new[] { match });

        var matches = await customerMatchRepo.GetEnrichedMatchesByItemIdAsync(itemId, orgId, 0.7);

        matches.Should().NotBeEmpty();
        matches[0].CustomerId.Should().Be(customerId);
        matches[0].CustomerName.Should().Be("Ananya Sharma");
        matches[0].MatchScore.Should().BeApproximately(0.94, 0.01);
    }

    [Fact]
    public async Task SourcingRequestAndSupplier_CanPersistAndQuery_InRealPostgres()
    {
        var (orgId, customerId) = await SeedCustomerAsync();
        var supplierRepo = new SupplierRepository(_context!);
        var sourcingRepo = new SourcingRequestRepository(_context!);

        var supplier = new Supplier
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            SupplierName = "Chennai Master Weavers",
            ContactEmail = "orders@chennaiweavers.com",
            DeliveryTimeDays = 6,
            IsActive = true
        };

        await supplierRepo.AddAsync(supplier);

        var sourcingRequest = new SourcingRequest
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            CustomerId = customerId,
            SupplierId = supplier.Id,
            Category = "saree",
            Color = "ruby red",
            ItemDescription = "Bridal Kanchipuram silk saree",
            ProposedPrice = 85000m,
            Status = "pending"
        };

        await sourcingRepo.AddAsync(sourcingRequest);

        var retrievedSupplier = await supplierRepo.GetByIdAsync(supplier.Id, orgId);
        retrievedSupplier.Should().NotBeNull();
        retrievedSupplier!.SupplierName.Should().Be("Chennai Master Weavers");

        var retrievedRequest = await sourcingRepo.GetByIdAsync(sourcingRequest.Id, orgId);
        retrievedRequest.Should().NotBeNull();
        retrievedRequest!.Category.Should().Be("saree");
        retrievedRequest.Status.Should().Be("pending");
    }
}
