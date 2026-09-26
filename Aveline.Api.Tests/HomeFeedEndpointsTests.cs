using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.VisualIntelligence.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #278 (slice S4) — the derived Home focus feed and the dismissal record
/// that makes "sign off" an act rather than a local list edit.
/// </summary>
public class HomeFeedEndpointsTests : IAsyncLifetime
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
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
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

    private HttpRequestMessage Authorized(
        HttpMethod method, string path, string token, object? body = null, string? idempotencyKey = null)
    {
        var request = new HttpRequestMessage(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

        if (body is not null)
        {
            request.Content = JsonContent.Create(body);
        }

        if (idempotencyKey is not null)
        {
            request.Headers.Add("Idempotency-Key", idempotencyKey);
        }

        return request;
    }

    private sealed record Seeded(Guid OrgId, string ClerkId, Guid ItemId, Guid CustomerId);

    /// <summary>
    /// Seeds one boutique with a member, one low-stock item and one upcoming customer
    /// event, so the wardrobe and patron domains both have something to derive.
    /// </summary>
    private static async Task<Seeded> SeedAsync(
        string suffix, string timeZone = "UTC", string itemName = "Raw silk")
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

        var member = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"home_member_{suffix}",
            Email = $"home_member_{suffix}@aveline.lk",
            FirstName = "Home",
            LastName = "Member",
            Username = $"home_member_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Home Org {suffix}",
            Slug = $"home-{suffix}",
            OwnerUserId = member.Id,
            TimeZone = timeZone,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = member.Id,
            BoutiqueRole = Roles.BoutiqueStaff,
            Status = MembershipStatus.Active,
        });

        var item = new InventoryItem
        {
            OrgId = org.Id,
            ItemName = itemName,
            Category = "fabric",
            StockQuantity = 2,
            Status = "available",
        };
        context.InventoryItems.Add(item);

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = $"+9477{suffix.GetHashCode() & 0xFFFFFF:D7}",
            FullName = "Mrs. Silva",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        context.CustomerEvents.Add(new CustomerEvent
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            EventType = "wedding",
            EventDate = DateTime.UtcNow.AddDays(2),
            IsActive = true,
        });

        await context.SaveChangesAsync();
        return new Seeded(org.Id, member.ClerkId, item.Id, customer.Id);
    }

    private async Task<JsonElement> GetFeedAsync(Guid orgId, string token)
    {
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{orgId}/stats/home", token));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task GetHomeFeed_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync($"/api/v1/orgs/{Guid.CreateVersion7()}/stats/home");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHomeFeed_DerivesWardrobeAndPatronDockets()
    {
        var seeded = await SeedAsync("derive");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var feed = await GetFeedAsync(seeded.OrgId, token);

        var items = feed.GetProperty("items").EnumerateArray().ToList();
        Assert.Contains(items, item => item.GetProperty("domain").GetString() == "wardrobe");
        Assert.Contains(items, item => item.GetProperty("domain").GetString() == "patron");

        var wardrobe = items.First(item => item.GetProperty("domain").GetString() == "wardrobe");
        // The id addresses the docket; the source key addresses the fact behind it,
        // which is what a dismissal is keyed to.
        Assert.False(string.IsNullOrWhiteSpace(wardrobe.GetProperty("sourceKey").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(wardrobe.GetProperty("contentHash").GetString()));
        Assert.True(wardrobe.GetProperty("caps").GetProperty("canComplete").GetBoolean());

        // `logistics` has no writer yet, and the feed says so rather than inventing 0.
        Assert.False(feed.GetProperty("dataQuality").GetProperty("logisticsAvailable").GetBoolean());
        Assert.True(feed.GetProperty("dataQuality").GetProperty("wardrobeAvailable").GetBoolean());
    }

    [Fact]
    public async Task GetHomeFeed_ReturnsOnlyThisOrgsDockets()
    {
        var first = await SeedAsync("tenant_a", itemName: "Org A silk");
        var second = await SeedAsync("tenant_b", itemName: "Org B silk");
        var token = CreateToken(first.ClerkId, orgRole: Roles.BoutiqueStaff);

        var feed = await GetFeedAsync(first.OrgId, token);

        var titles = feed.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("title").GetString())
            .ToList();
        Assert.Contains(titles, title => title!.Contains("Org A silk"));
        Assert.DoesNotContain(titles, title => title!.Contains("Org B silk"));
    }

    [Fact]
    public async Task GetHomeFeed_UsesTheOrganizationTimeZoneForTheDay()
    {
        var seeded = await SeedAsync("timezone", timeZone: "Asia/Colombo");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var feed = await GetFeedAsync(seeded.OrgId, token);

        var window = feed.GetProperty("window");
        Assert.Equal("Asia/Colombo", window.GetProperty("timeZone").GetString());

        var expected = TimeZoneInfo.ConvertTimeFromUtc(
            DateTime.UtcNow, TimeZoneInfo.FindSystemTimeZoneById("Asia/Colombo")).Date;
        Assert.Equal(expected.ToString("yyyy-MM-dd"), window.GetProperty("localDate").GetString());
    }

    [Fact]
    public async Task DismissedDocket_DoesNotReappearOnTheNextRead()
    {
        var seeded = await SeedAsync("dismiss");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var feed = await GetFeedAsync(seeded.OrgId, token);
        var wardrobe = feed.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("domain").GetString() == "wardrobe");

        var dismiss = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/focus/dismissals",
            token,
            new
            {
                sourceKey = wardrobe.GetProperty("sourceKey").GetString(),
                domain = "wardrobe",
                decision = "signOff",
                contentHash = wardrobe.GetProperty("contentHash").GetString(),
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, dismiss.StatusCode);

        // The underlying condition (low stock) is unchanged, so only the dismissal
        // can keep the docket out.
        var after = await GetFeedAsync(seeded.OrgId, token);
        var sources = after.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("sourceKey").GetString())
            .ToList();
        Assert.DoesNotContain(wardrobe.GetProperty("sourceKey").GetString(), sources);
    }

    [Fact]
    public async Task Dismissal_OfChangedContentAtTheSameSourceKey_Reappears()
    {
        var seeded = await SeedAsync("changed");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var feed = await GetFeedAsync(seeded.OrgId, token);
        var wardrobe = feed.GetProperty("items").EnumerateArray()
            .First(item => item.GetProperty("domain").GetString() == "wardrobe");

        await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/focus/dismissals",
            token,
            new
            {
                sourceKey = wardrobe.GetProperty("sourceKey").GetString(),
                domain = "wardrobe",
                decision = "signOff",
                contentHash = wardrobe.GetProperty("contentHash").GetString(),
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        // The fact behind the docket changes (the item is renamed), so the hash no
        // longer matches and the docket must come back rather than stay suppressed.
        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options))
        {
            var item = await context.InventoryItems.FindAsync(seeded.ItemId);
            item!.ItemName = "Raw silk (restocked line)";
            await context.SaveChangesAsync();
        }

        var after = await GetFeedAsync(seeded.OrgId, token);
        var sources = after.GetProperty("items").EnumerateArray()
            .Select(item => item.GetProperty("sourceKey").GetString())
            .ToList();
        Assert.Contains(wardrobe.GetProperty("sourceKey").GetString(), sources);
    }

    [Fact]
    public async Task Dismissal_OfASourceKeyNotInTheFeed_Returns404()
    {
        var seeded = await SeedAsync("foreign");
        var other = await SeedAsync("foreign_other");
        var token = CreateToken(other.ClerkId, orgRole: Roles.BoutiqueStaff);

        // Org B's member dismisses a docket that only exists in org A's feed. The
        // answer must be indistinguishable from "no such docket".
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{other.OrgId}/focus/dismissals",
            token,
            new
            {
                sourceKey = seeded.ItemId.ToString(),
                domain = "wardrobe",
                decision = "signOff",
                contentHash = "irrelevant",
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Dismissal_AsOutsider_ReturnsForbidden()
    {
        var seeded = await SeedAsync("outsider_target");
        var other = await SeedAsync("outsider_actor");
        var token = CreateToken(other.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/focus/dismissals",
            token,
            new
            {
                sourceKey = seeded.ItemId.ToString(),
                domain = "wardrobe",
                decision = "signOff",
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }
}
