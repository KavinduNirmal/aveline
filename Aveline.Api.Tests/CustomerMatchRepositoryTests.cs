using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Aveline.Api.Tests;

public class CustomerMatchRepositoryTests
{
    private static AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;
        return new AppDbContext(options);
    }

    [Fact]
    public async Task GenerateMatchesForInventoryItemAsync_WhenNoCustomersExist_ReturnsEmptyList()
    {
        using var db = CreateDbContext();
        var repo = new CustomerMatchRepository(db);

        var matches = await repo.GenerateMatchesForInventoryItemAsync(Guid.NewGuid(), Guid.NewGuid(), 10);
        matches.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateMatchesForInventoryItemAsync_WithMatchingPreferences_ReturnsCalculatedMatches()
    {
        using var db = CreateDbContext();
        var orgId = Guid.NewGuid();

        var item = new InventoryItem
        {
            Id = Guid.NewGuid(),
            OrgId = orgId,
            ItemName = "Emerald Bridal Saree",
            Category = "saree",
            Color = "emerald",
            Price = 60000m,
            Quantity = 2,
            Status = "available"
        };
        await db.InventoryItems.AddAsync(item);

        var matchingCustomer = new Customer
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            FullName = "Nimanthi Wickramasinghe",
            PhoneNumber = "+94771122334",
            Status = "vip",
            Preferences = new List<CustomerPreference>
            {
                new() { OrganizationId = orgId, PreferenceKey = "color", PreferenceValue = "emerald" },
                new() { OrganizationId = orgId, PreferenceKey = "category", PreferenceValue = "saree" }
            }
        };

        var otherCustomer = new Customer
        {
            Id = Guid.NewGuid(),
            OrganizationId = orgId,
            FullName = "Samanthi Silva",
            PhoneNumber = "+94779988776",
            Status = "new",
            Preferences = new List<CustomerPreference>
            {
                new() { OrganizationId = orgId, PreferenceKey = "color", PreferenceValue = "black" }
            }
        };

        await db.Customers.AddRangeAsync(matchingCustomer, otherCustomer);
        await db.SaveChangesAsync();

        var repo = new CustomerMatchRepository(db);
        var matches = await repo.GenerateMatchesForInventoryItemAsync(item.Id, orgId, 5);

        matches.Should().NotBeEmpty();
        matches[0].CustomerId.Should().Be(matchingCustomer.Id);
        matches[0].CustomerName.Should().Be("Nimanthi Wickramasinghe");
        matches[0].MatchScore.Should().BeGreaterThan(0.7);
        matches[0].MatchingPreferences.Should().Contain(p => p.Contains("emerald"));

        // Verify persistence in CustomerMatches table
        var savedInDb = await db.CustomerMatches.ToListAsync();
        savedInDb.Should().Contain(m => m.CustomerId == matchingCustomer.Id && m.ItemId == item.Id);
    }
}
