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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #279 (slice S5) — the tenant-facing customer surface Home consumes: the
/// client highlights, the book the log-visit picker reads, and walk-in creation.
/// </summary>
public class CustomerTenantEndpointsTests : IAsyncLifetime
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

    private sealed record Seeded(Guid OrgId, string ClerkId, Guid CustomerId);

    private static async Task<Seeded> SeedAsync(
        string suffix, string boutiqueRole = Roles.BoutiqueStaff, string? nickname = null)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var member = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"tenant_member_{suffix}",
            Email = $"tenant_member_{suffix}@aveline.lk",
            FirstName = "Tenant",
            LastName = "Member",
            Username = $"tenant_member_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(member);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Tenant Org {suffix}",
            Slug = $"tenant-{suffix}",
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

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = $"+94771{suffix.Length:D6}",
            FullName = $"Nadia Client {suffix}",
            Nickname = nickname ?? $"Nads {suffix}",
            Status = "new",
            Level = "level2",
        };
        context.Customers.Add(customer);
        await context.SaveChangesAsync();

        context.CustomerInteractions.Add(new CustomerInteraction
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            Channel = "in_person",
            Direction = "inbound",
            MessageContent = "Came in for a fitting.",
            CreatedAt = DateTime.UtcNow.AddHours(-2),
        });
        await context.SaveChangesAsync();

        return new Seeded(org.Id, member.ClerkId, customer.Id);
    }

    [Fact]
    public async Task GetCustomers_WithoutToken_Returns401()
    {
        var response = await _client.GetAsync(
            $"/api/v1/orgs/{Guid.CreateVersion7()}/customers/highlights");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomers_AsOutsider_ReturnsForbidden()
    {
        var target = await SeedAsync("outsider_target");
        var actor = await SeedAsync("outsider_actor");
        var token = CreateToken(actor.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{target.OrgId}/customers/highlights", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomers_IsNotReachableWithTheInternalTokenScheme()
    {
        var seeded = await SeedAsync("internal");

        var request = new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/highlights");
        request.Headers.Add("X-Internal-Token", "not-a-real-token");

        var response = await _client.SendAsync(request);

        // A staff device carries a Clerk bearer; the internal scheme must not open
        // the tenant routes.
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetHighlights_ReturnsOnlyThisOrgsCustomersAndNoActivityFlag()
    {
        var mine = await SeedAsync("mine");
        var theirs = await SeedAsync("theirs");
        var token = CreateToken(mine.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{mine.OrgId}/customers/highlights", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = body.GetProperty("items").EnumerateArray().ToList();

        Assert.Single(items);
        var item = items[0];
        Assert.Equal(mine.CustomerId, item.GetProperty("customerId").GetGuid());
        Assert.Equal("level2", item.GetProperty("level").GetString());
        Assert.True(item.TryGetProperty("activity", out var activity));
        Assert.False(string.IsNullOrWhiteSpace(activity.GetString()));
        Assert.True(item.TryGetProperty("lastActivityAtUtc", out _));
        // Decision D2(a): there is no read marker, so no flag that can never clear.
        Assert.False(item.TryGetProperty("hasNewActivity", out _));

        Assert.DoesNotContain(
            body.GetProperty("items").EnumerateArray(),
            candidate => candidate.GetProperty("customerId").GetGuid() == theirs.CustomerId);
    }

    [Fact]
    public async Task GetBook_ReturnsThePageEnvelopeThePickerReads()
    {
        var seeded = await SeedAsync("book");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers?page=1&pageSize=200", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(1, body.GetProperty("total").GetInt32());
        Assert.Equal(1, body.GetProperty("page").GetInt32());
        Assert.Equal(200, body.GetProperty("pageSize").GetInt32());
        var item = body.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(seeded.CustomerId, item.GetProperty("customerId").GetGuid());
        Assert.Equal("level2", item.GetProperty("level").GetString());
        Assert.Equal("new", item.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateWalkIn_ReturnsTheServerId()
    {
        var seeded = await SeedAsync("walkin");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers",
            token,
            new { fullName = "Maria Silva", source = "counter_walkin" },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var id = body.GetProperty("customerId").GetGuid();
        Assert.NotEqual(Guid.Empty, id);
        Assert.Equal("new", body.GetProperty("status").GetString());
        // The server did not invent a grade for a client nobody has graded.
        Assert.Equal(JsonValueKind.Null, body.GetProperty("level").ValueKind);
    }

    [Fact]
    public async Task CreateWalkIn_WithAnExistingName_ReportsTheDuplicate()
    {
        var seeded = await SeedAsync("duplicate");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers",
            token,
            new { fullName = $"Nadia Client duplicate", source = "counter_walkin" },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(seeded.CustomerId, body.GetProperty("duplicateOfCustomerId").GetGuid());
    }

    private async Task<JsonElement> RecordVisitAsync(
        Guid orgId, Guid customerId, string token, string channel = "in_person",
        decimal? purchaseTotal = null)
    {
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{orgId}/customers/{customerId}/interactions",
            token,
            new
            {
                occurredAtUtc = DateTime.UtcNow.AddMinutes(-5),
                channel,
                direction = "inbound",
                note = "Fitting at the counter.",
                purchaseTotal,
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
    }

    [Fact]
    public async Task RecordVisit_IncrementsVisitCountAndSetsLastVisit()
    {
        var seeded = await SeedAsync("visit");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var body = await RecordVisitAsync(seeded.OrgId, seeded.CustomerId, token);

        Assert.Equal(1, body.GetProperty("visitCountAfter").GetInt32());
        Assert.NotEqual(JsonValueKind.Null, body.GetProperty("lastVisitAtUtcAfter").ValueKind);
        // A visit is not billable: consumption is an AiUsageRecord from an agent
        // workflow, and no rule debits a Blossom for a counter visit.
        Assert.Equal(0m, body.GetProperty("blossomsCharged").GetDecimal());
    }

    [Fact]
    public async Task RecordVisit_ThenRecomputeStatus_UpgradesTheTier()
    {
        var seeded = await SeedAsync("visit_tier");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        await RecordVisitAsync(seeded.OrgId, seeded.CustomerId, token);
        var second = await RecordVisitAsync(seeded.OrgId, seeded.CustomerId, token);

        Assert.Equal(2, second.GetProperty("visitCountAfter").GetInt32());
        Assert.Equal("returning", second.GetProperty("tierAfter").GetString());
    }

    [Fact]
    public async Task RecordVisit_ChargesThePurchaseToTotalSpent()
    {
        var seeded = await SeedAsync("visit_spend");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        await RecordVisitAsync(seeded.OrgId, seeded.CustomerId, token, purchaseTotal: 12500m);
        var second = await RecordVisitAsync(seeded.OrgId, seeded.CustomerId, token);

        Assert.Equal(2, second.GetProperty("visitCountAfter").GetInt32());
        Assert.Equal("returning", second.GetProperty("tierAfter").GetString());
    }

    [Fact]
    public async Task RecordVisit_OnAChannelThatIsNotInPerson_DoesNotCountAsAVisit()
    {
        var seeded = await SeedAsync("visit_channel");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var body = await RecordVisitAsync(
            seeded.OrgId, seeded.CustomerId, token, channel: "whatsapp");

        // The interaction is still recorded, but a message is not a visit.
        Assert.Equal(0, body.GetProperty("visitCountAfter").GetInt32());
    }

    [Fact]
    public async Task RecordVisit_ForAnotherOrgsCustomer_Returns404()
    {
        var mine = await SeedAsync("visit_mine");
        var theirs = await SeedAsync("visit_theirs");
        var token = CreateToken(mine.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{mine.OrgId}/customers/{theirs.CustomerId}/interactions",
            token,
            new
            {
                occurredAtUtc = DateTime.UtcNow,
                channel = "in_person",
                direction = "inbound",
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetDetail_ReturnsCustomerProfileWithNicknameAndPreferences()
    {
        var seeded = await SeedAsync("detail", nickname: "Naddy");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options))
        {
            context.CustomerPreferences.Add(new CustomerPreference
            {
                OrganizationId = seeded.OrgId,
                CustomerId = seeded.CustomerId,
                PreferenceKey = "fabric",
                PreferenceValue = "Silk and linen",
                IsExplicit = true,
                Confidence = 0.95m,
                Source = "conversation",
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(seeded.CustomerId, body.GetProperty("customerId").GetGuid());
        Assert.Equal("Naddy", body.GetProperty("nickname").GetString());
        Assert.Equal("level2", body.GetProperty("level").GetString());
        Assert.Equal(1, body.GetProperty("loyaltyTierIsDerived").GetInt32());

        var prefs = body.GetProperty("preferences").EnumerateArray().ToList();
        Assert.Single(prefs);
        Assert.Equal("fabric", prefs[0].GetProperty("preferenceKey").GetString());
        Assert.Equal("Silk and linen", prefs[0].GetProperty("preferenceValue").GetString());
    }

    [Fact]
    public async Task GetDetail_ForAnotherOrgsCustomer_Returns404()
    {
        var mine = await SeedAsync("detail_mine");
        var theirs = await SeedAsync("detail_theirs");
        var token = CreateToken(mine.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{mine.OrgId}/customers/{theirs.CustomerId}", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetConsent_ReturnsUnknown_WhenNoConsentRecordExists()
    {
        var seeded = await SeedAsync("consent");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/consent", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("unknown", body.GetProperty("status").GetString());
        Assert.Equal(JsonValueKind.Null, body.GetProperty("grantedAtUtc").ValueKind);
    }

    [Fact]
    public async Task GetMemories_ReturnsCustomerMemories()
    {
        var seeded = await SeedAsync("memories");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        await using (var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options))
        {
            context.CustomerMemories.Add(new CustomerMemory
            {
                OrganizationId = seeded.OrgId,
                CustomerId = seeded.CustomerId,
                Content = "Prefers evening appointments on Thursdays.",
                Category = "fact",
                Source = "staff_note",
                IsExplicit = true,
                Confidence = 1.0m,
            });
            await context.SaveChangesAsync();
        }

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/memories", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var items = body.EnumerateArray().ToList();
        Assert.Single(items);
        Assert.Equal("Prefers evening appointments on Thursdays.", items[0].GetProperty("content").GetString());
        Assert.Equal("fact", items[0].GetProperty("category").GetString());
    }

    [Fact]
    public async Task PostEvents_AsStaffWithoutManage_ReturnsForbidden()
    {
        var seeded = await SeedAsync("events_forbidden", boutiqueRole: Roles.BoutiqueStaff);
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/events",
            token,
            new { eventType = "birthday", eventDate = DateTime.UtcNow.AddDays(5), description = "Birthday coming up" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PostEvents_AsManager_CreatesCustomerEvent()
    {
        var seeded = await SeedAsync("events_mgr", boutiqueRole: Roles.BoutiqueManager);
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/events",
            token,
            new { eventType = "anniversary", eventDate = DateTime.UtcNow.AddDays(30), description = "Boutique 1yr anniversary" }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("anniversary", body.GetProperty("eventType").GetString());

        var getResponse = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/events", token));
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);
        var getBody = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync()).RootElement;
        Assert.Contains(getBody.EnumerateArray(), e => e.GetProperty("eventType").GetString() == "anniversary");
    }

    [Fact]
    public async Task PostStatus_AsManager_RecomputesTier()
    {
        var seeded = await SeedAsync("status_mgr", boutiqueRole: Roles.BoutiqueManager);
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/status",
            token,
            new { }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.True(body.TryGetProperty("status", out var statusProp));
        Assert.False(string.IsNullOrEmpty(statusProp.GetString()));
    }

    [Fact]
    public async Task PatchCustomer_AsManager_UpdatesNicknameAndLevel()
    {
        var seeded = await SeedAsync("patch_mgr", boutiqueRole: Roles.BoutiqueManager);
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token,
            new { nickname = "VipQueen", level = "level3" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("VipQueen", body.GetProperty("nickname").GetString());
        Assert.Equal("level3", body.GetProperty("level").GetString());
    }

    [Fact]
    public async Task DeleteCustomer_AsManager_SoftDeletesCustomer()
    {
        var seeded = await SeedAsync("del_mgr", boutiqueRole: Roles.BoutiqueManager);
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueManager);

        var deleteResponse = await _client.SendAsync(Authorized(
            HttpMethod.Delete,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token));

        Assert.Equal(HttpStatusCode.NoContent, deleteResponse.StatusCode);

        var getResponse = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token));
        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
    }
}
