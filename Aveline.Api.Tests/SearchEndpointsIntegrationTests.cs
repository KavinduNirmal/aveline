using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

public class SearchEndpointsIntegrationTests : IAsyncLifetime
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

    private string CreateToken(string clerkId, string? userRole = null, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));
        if (orgRole is not null) claims.Add(new Claim("org_role", orgRole));

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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token)
    {
        return new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };
    }

    private sealed record SeededSearchData(
        Guid OrgId,
        string ClerkId,
        Guid UserId,
        Guid ItemId,
        Guid CustomerId,
        Guid ConversationId);

    private static async Task<SeededSearchData> SeedDataAsync(string suffix, string boutiqueRole = Roles.BoutiqueManager)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var member = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"search_user_{suffix}",
            Email = $"search_{suffix}@aveline.lk",
            FirstName = "Search",
            LastName = "Tester",
            Username = $"search_user_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Search Boutique {suffix}",
            Slug = $"search-boutique-{suffix}",
            OwnerUserId = member.Id,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = member.Id,
            BoutiqueRole = boutiqueRole,
            Status = MembershipStatus.Active,
        });

        // 1. Seed Inventory Item with "Emerald" in name / SKU
        var item = new InventoryItem
        {
            OrgId = org.Id,
            ItemName = $"Emerald Silk Saree {suffix}",
            Sku = $"SKU-EMR-{suffix}",
            Category = "Sarees",
            Color = "Emerald",
            Fabric = "Silk",
            Price = 45000m,
            Quantity = 3,
        };
        context.InventoryItems.Add(item);

        // 2. Seed Customer with "Emerald" in name
        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = $"+9477{suffix.PadLeft(7, '0')}",
            FullName = $"Emerald Perera {suffix}",
            Status = "returning",
            Level = "vip",
        };
        context.Customers.Add(customer);

        // 3. Seed Conversation
        var conversation = new Conversation
        {
            OrganizationId = org.Id,
            OwnerUserId = null, // Organization-shared
            CustomerId = customer.Id,
            ThreadId = $"thread_emr_{suffix}",
            Kind = ConversationKind.Salon,
            ExternalRef = $"Emerald Inquiry {suffix}",
            Status = ConversationStatus.Active,
            CreatedAt = DateTime.UtcNow,
        };
        context.Conversations.Add(conversation);

        await context.SaveChangesAsync();

        return new SeededSearchData(org.Id, member.ClerkId, member.Id, item.Id, customer.Id, conversation.Id);
    }

    [Fact]
    public async Task Search_WithValidQuery_ReturnsAggregatedResultsAcrossAllEntities()
    {
        var data = await SeedDataAsync("all_agg");
        var token = CreateToken(data.ClerkId);

        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{data.OrgId}/search?q=Emerald", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GlobalSearchResponseDto>();
        Assert.NotNull(body);
        Assert.True(body.Total >= 3);
        Assert.Contains(body.Items, i => i.Type == SearchEntityType.CatalogItem && i.Id == data.ItemId);
        Assert.Contains(body.Items, i => i.Type == SearchEntityType.Customer && i.Id == data.CustomerId);
        Assert.Contains(body.Items, i => i.Type == SearchEntityType.Conversation && i.Id == data.ConversationId);
    }

    [Fact]
    public async Task Search_WithScopeCatalog_ReturnsOnlyCatalogItems()
    {
        var data = await SeedDataAsync("scope_cat");
        var token = CreateToken(data.ClerkId);

        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{data.OrgId}/search?q=Emerald&scope=catalog", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GlobalSearchResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Items);
        Assert.All(body.Items, i => Assert.Equal(SearchEntityType.CatalogItem, i.Type));
    }

    [Fact]
    public async Task Search_WithScopeCustomers_ReturnsOnlyCustomers()
    {
        var data = await SeedDataAsync("scope_cust");
        var token = CreateToken(data.ClerkId);

        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{data.OrgId}/search?q=Emerald&scope=customers", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GlobalSearchResponseDto>();
        Assert.NotNull(body);
        Assert.NotEmpty(body.Items);
        Assert.All(body.Items, i => Assert.Equal(SearchEntityType.Customer, i.Type));
    }

    [Fact]
    public async Task Search_WithShortQuery_Returns400BadRequest()
    {
        var data = await SeedDataAsync("short_q");
        var token = CreateToken(data.ClerkId);

        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{data.OrgId}/search?q=E", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_WithNonExistentQuery_Returns200WithEmptyList()
    {
        var data = await SeedDataAsync("no_match");
        var token = CreateToken(data.ClerkId);

        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{data.OrgId}/search?q=UnmatchedZzXxYy", token);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<GlobalSearchResponseDto>();
        Assert.NotNull(body);
        Assert.Equal(0, body.Total);
        Assert.Empty(body.Items);
    }

    [Fact]
    public async Task Search_WithCrossTenantOrgId_Returns403Forbidden()
    {
        var dataA = await SeedDataAsync("tenant_a");
        var dataB = await SeedDataAsync("tenant_b");
        var tokenA = CreateToken(dataA.ClerkId);

        // Attempt to search Org B using Org A's token
        var request = Authorized(HttpMethod.Get, $"/api/v1/orgs/{dataB.OrgId}/search?q=Emerald", tokenA);
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
