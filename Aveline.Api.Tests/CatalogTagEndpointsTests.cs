using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Models;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

public class CatalogTagEndpointsTests : IAsyncLifetime
{
    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };
        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId, string userRole = "staff")
    {
        var claims = new List<Claim>
        {
            new("sub", clerkId),
            new("user_role", userRole)
        };

        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };

        return handler.CreateToken(descriptor);
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) }
        };
        if (content != null)
        {
            request.Content = content;
        }
        return request;
    }

    private async Task<(User User, Organization Org)> SeedMemberAndOrgAsync(string prefix)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var user = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"{prefix}_clerk_id",
            Email = $"{prefix}@example.com",
            FirstName = "Test",
            LastName = "User",
            Username = $"{prefix}_user",
            UserRole = Roles.Staff,
            OrganizationRole = Roles.BoutiqueOwner,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
            IsActive = true
        };

        var org = new Organization
        {
            Id = Guid.CreateVersion7(),
            Slug = $"{prefix}-org",
            Name = $"{prefix} Atelier",
            OwnerUserId = user.Id,
            IsActive = true,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var membership = new OrganizationMembership
        {
            UserId = user.Id,
            OrganizationId = org.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        db.Users.Add(user);
        db.Organizations.Add(org);
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        return (user, org);
    }

    [Fact]
    public async Task GetTags_ReturnsTenantTagsOrderedBySortOrder()
    {
        // Arrange
        var (user, org) = await SeedMemberAndOrgAsync("tag_list");
        var token = CreateToken(user.ClerkId);

        // Act
        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/tags", token);
        var response = await _client.SendAsync(request);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await response.Content.ReadFromJsonAsync<List<CatalogTagDto>>();
        tags.Should().NotBeNull();
        tags!.Count.Should().BeGreaterThanOrEqualTo(8);
        tags[0].Slug.Should().Be("bridal");
        tags[1].Slug.Should().Be("festive");
    }

    [Fact]
    public async Task CreateTag_EnforcesTenantIsolationAndSlugUniqueness()
    {
        // Arrange
        var (userA, orgA) = await SeedMemberAndOrgAsync("tag_create_a");
        var (userB, orgB) = await SeedMemberAndOrgAsync("tag_create_b");
        var tokenA = CreateToken(userA.ClerkId);
        var tokenB = CreateToken(userB.ClerkId);

        var payload = new CreateCatalogTagDto
        {
            Slug = "exclusive-silk",
            Label = "Exclusive Silk",
            ColorHex = "#A123BC",
            SortOrder = 10
        };

        // Act 1: Create for Org A
        var req1 = Authorized(HttpMethod.Post, $"/api/v1/orgs/{orgA.Id}/catalog/tags", tokenA, JsonContent.Create(payload));
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await res1.Content.ReadFromJsonAsync<CatalogTagDto>();
        created.Should().NotBeNull();
        created!.Slug.Should().Be("exclusive-silk");
        created.ColorHex.Should().Be("#A123BC");

        // Act 2: Duplicate create for Org A should conflict
        var req2 = Authorized(HttpMethod.Post, $"/api/v1/orgs/{orgA.Id}/catalog/tags", tokenA, JsonContent.Create(payload));
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        // Act 3: Same slug for Org B should succeed (multi-tenant isolation)
        var req3 = Authorized(HttpMethod.Post, $"/api/v1/orgs/{orgB.Id}/catalog/tags", tokenB, JsonContent.Create(payload));
        var res3 = await _client.SendAsync(req3);
        res3.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task QueryItems_WithTagFilter_ReturnsMatchingTaggedItems()
    {
        // Arrange
        var (user, org) = await SeedMemberAndOrgAsync("tag_filter");
        var token = CreateToken(user.ClerkId);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var tagBridal = new CatalogTag { Id = Guid.NewGuid(), OrgId = org.Id, Slug = "bridal-test", Label = "Bridal Test", SortOrder = 1, CreatedAtUtc = DateTime.UtcNow };
            var tagEvening = new CatalogTag { Id = Guid.NewGuid(), OrgId = org.Id, Slug = "evening-test", Label = "Evening Test", SortOrder = 2, CreatedAtUtc = DateTime.UtcNow };
            db.CatalogTags.AddRange(tagBridal, tagEvening);

            var item1 = new InventoryItem
            {
                Id = Guid.NewGuid(),
                OrgId = org.Id,
                ItemName = "Royal Bridal Kanjeevaram",
                Category = "Sarees",
                Color = "Gold",
                Price = 120000m,
                Cost = 60000m,
                Quantity = 3,
                Status = "available",
                CreatedAtUtc = DateTime.UtcNow
            };

            var item2 = new InventoryItem
            {
                Id = Guid.NewGuid(),
                OrgId = org.Id,
                ItemName = "Midnight Velvet Gown",
                Category = "Gowns",
                Color = "Black",
                Price = 85000m,
                Cost = 40000m,
                Quantity = 2,
                Status = "available",
                CreatedAtUtc = DateTime.UtcNow
            };

            db.InventoryItems.AddRange(item1, item2);

            db.InventoryItemTags.Add(new InventoryItemTag { ItemId = item1.Id, TagId = tagBridal.Id, OrgId = org.Id });
            db.InventoryItemTags.Add(new InventoryItemTag { ItemId = item2.Id, TagId = tagEvening.Id, OrgId = org.Id });

            await db.SaveChangesAsync();
        }

        // Act: Query for bridal-test
        var query = new CatalogQueryRequest
        {
            OrganizationId = org.Id,
            TagIds = new List<string> { "bridal-test" }
        };

        var req = Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items/query", token, JsonContent.Create(query));
        var res = await _client.SendAsync(req);

        // Assert
        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var result = await res.Content.ReadFromJsonAsync<CatalogPagedResponse>();
        result.Should().NotBeNull();
        result!.Items.Should().HaveCount(1);
        result.Items[0].ItemName.Should().Be("Royal Bridal Kanjeevaram");
        result.Items[0].Tags.Should().Contain("bridal-test");
    }

    [Fact]
    public async Task AssignItemTags_UpdatesPieceTagsDynamically()
    {
        // Arrange
        var (user, org) = await SeedMemberAndOrgAsync("tag_assign");
        var token = CreateToken(user.ClerkId);
        var itemId = Guid.NewGuid();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var item = new InventoryItem
            {
                Id = itemId,
                OrgId = org.Id,
                ItemName = "Handloom Banarasi Saree",
                Category = "Sarees",
                Color = "Crimson",
                Price = 95000m,
                Cost = 45000m,
                Quantity = 4,
                Status = "available",
                CreatedAtUtc = DateTime.UtcNow
            };
            db.InventoryItems.Add(item);
            await db.SaveChangesAsync();
        }

        // Act 1: Assign tags via PUT /items/{itemId}/tags
        var assignDto = new AssignItemTagsDto
        {
            Tags = new List<string> { "bridal", "handloom", "custom-atelier" }
        };
        var assignReq = Authorized(HttpMethod.Put, $"/api/v1/orgs/{org.Id}/catalog/items/{itemId}/tags", token, JsonContent.Create(assignDto));
        var assignRes = await _client.SendAsync(assignReq);
        assignRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Act 2: Verify via GET /items/{itemId}/tags
        var getReq = Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{itemId}/tags", token);
        var getRes = await _client.SendAsync(getReq);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var tags = await getRes.Content.ReadFromJsonAsync<List<CatalogTagDto>>();
        tags.Should().NotBeNull();
        tags!.Select(t => t.Slug).Should().Contain(new[] { "bridal", "handloom", "custom-atelier" });
    }
}
