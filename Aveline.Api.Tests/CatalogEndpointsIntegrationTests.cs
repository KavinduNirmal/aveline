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

public class CatalogEndpointsIntegrationTests : IAsyncLifetime
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
        db.Users.Add(user);

        var org = new Organization
        {
            Id = Guid.CreateVersion7(),
            Name = $"{prefix} Boutique",
            Slug = $"{prefix}-boutique",
            OwnerUserId = user.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Organizations.Add(org);

        var membership = new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.OrganizationMemberships.Add(membership);

        await db.SaveChangesAsync();
        return (user, org);
    }

    [Fact]
    public async Task GetItems_WithoutAuth_ReturnsUnauthorized()
    {
        var orgId = Guid.NewGuid();
        var response = await _client.GetAsync($"/api/v1/orgs/{orgId}/catalog/items");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetItems_WithActiveMember_ReturnsOk()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_items");
        var token = CreateToken(user.ClerkId);

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemDto>>();
        items.Should().NotBeNull();
    }

    [Fact]
    public async Task CreateAndGetItem_Flow_Succeeds()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_create");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Silk Banarasi Saree",
            Category = "saree",
            Color = "crimson",
            Price = 450.00m,
            Quantity = 8,
            Sizes = new List<string> { "Free Size" },
            Description = "Pure silk handloom saree"
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));

        postResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var created = await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        created.Should().NotBeNull();
        created!.ItemName.Should().Be("Silk Banarasi Saree");
        created.OrganizationId.Should().Be(org.Id);

        // Get by ID
        var getResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}", token));

        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var fetched = await getResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        fetched.Should().NotBeNull();
        fetched!.Id.Should().Be(created.Id);
        fetched.ItemName.Should().Be("Silk Banarasi Saree");
    }

    [Fact]
    public async Task UpdateItem_AndStatus_Succeeds()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_update");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Linen Kurta",
            Category = "kurta",
            Color = "navy",
            Price = 120.00m,
            Quantity = 15
        };

        var postResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));
        var created = (await postResponse.Content.ReadFromJsonAsync<InventoryItemDto>())!;

        // Update item details
        var updateDto = new UpdateInventoryItemDto
        {
            ItemName = "Premium Linen Kurta",
            Price = 140.00m,
            Quantity = 12
        };
        var putResponse = await _client.SendAsync(
            Authorized(HttpMethod.Put, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}", token, JsonContent.Create(updateDto)));

        putResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var updated = await putResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        updated!.ItemName.Should().Be("Premium Linen Kurta");
        updated.Price.Should().Be(140.00m);

        // Update status
        var statusDto = new UpdateInventoryStatusDto { Status = "reserved" };
        var patchResponse = await _client.SendAsync(
            Authorized(HttpMethod.Patch, $"/api/v1/orgs/{org.Id}/catalog/items/{created.Id}/status", token, JsonContent.Create(statusDto)));

        patchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await patchResponse.Content.ReadFromJsonAsync<InventoryItemDto>();
        patched!.Status.Should().Be("reserved");
    }

    [Fact]
    public async Task GetLowStock_ReturnsLowStockItems()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_lowstock");
        var token = CreateToken(user.ClerkId);

        var createDto = new CreateInventoryItemDto
        {
            ItemName = "Scarce Velvet Dupatta",
            Category = "dupatta",
            Color = "gold",
            Price = 85.00m,
            Quantity = 2
        };
        await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/items", token, JsonContent.Create(createDto)));

        var response = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/low-stock?threshold=3", token));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var items = await response.Content.ReadFromJsonAsync<List<InventoryItemDto>>();
        items.Should().NotBeNull();
        items!.Should().Contain(x => x.ItemName == "Scarce Velvet Dupatta");
    }

    [Fact]
    public async Task Lookbooks_And_Sourcing_Endpoints_WorkCorrectly()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_lookbooks");
        var token = CreateToken(user.ClerkId);

        // Lookbooks query
        var lookbooksResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/lookbooks", token));
        lookbooksResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Sourcing requests query
        var sourcingResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/sourcing", token));
        sourcingResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        // Create sourcing request
        var createSourcingDto = new CreateSourcingRequestDto
        {
            Category = "lehenga",
            Color = "rose gold",
            Description = "Rose Gold Embroidered Lehenga",
            TargetPrice = 750.00m,
            QuantityNeeded = 5,
            Urgency = "high"
        };
        var createSourcingResponse = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/orgs/{org.Id}/catalog/sourcing", token, JsonContent.Create(createSourcingDto)));
        createSourcingResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var createdSourcing = (await createSourcingResponse.Content.ReadFromJsonAsync<SourcingRequestDto>())!;

        // Patch sourcing status
        var updateStatusDto = new UpdateSourcingStatusDto("ordered");
        var patchSourcingResponse = await _client.SendAsync(
            Authorized(HttpMethod.Patch, $"/api/v1/orgs/{org.Id}/catalog/sourcing/{createdSourcing.Id}/status", token, JsonContent.Create(updateStatusDto)));
        patchSourcingResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Suppliers_Endpoints_ReturnOk()
    {
        var (user, org) = await SeedMemberAndOrgAsync("cat_suppliers");
        var token = CreateToken(user.ClerkId);

        var suppliersResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/suppliers", token));
        suppliersResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var catalogResponse = await _client.SendAsync(
            Authorized(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/catalog/suppliers/{Guid.NewGuid()}/catalog", token));
        catalogResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}
