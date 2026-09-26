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

    private sealed record Seeded(Guid OrgId, string ClerkId, Guid CustomerId);

    private static async Task<Seeded> SeedAsync(string suffix)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
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
            BoutiqueRole = Roles.BoutiqueStaff,
            Status = MembershipStatus.Active,
        });

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = $"+94771{suffix.Length:D6}",
            FullName = $"Nadia Client {suffix}",
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

    [Fact]
    public async Task CreateWalkIn_PersistsAPendingConsentRowAndReportsIt()
    {
        // D-6 / 0.1c: walk-in creation wrote no CustomerConsent row at all while the response
        // hardcoded "pending", so the staff UI was shown a consent that did not exist.
        var seeded = await SeedAsync("walkin_consent");
        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers",
            token,
            new { fullName = "Walkin Consent Client", source = "counter_walkin" },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        var customerId = body.GetProperty("customerId").GetGuid();
        Assert.Equal("pending", body.GetProperty("consentStatus").GetString());

        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);
        var row = await context.CustomerConsents.SingleOrDefaultAsync(
            candidate => candidate.OrganizationId == seeded.OrgId && candidate.CustomerId == customerId);

        Assert.NotNull(row);
        Assert.Equal("pending", row!.ConsentStatus);
    }

    [Fact]
    public async Task CreateWalkIn_OnADuplicate_ReportsThePersistedConsentNotALiteral()
    {
        // D-6 / 0.1c: the duplicate branch also hardcoded "pending". It must report the row the
        // client actually has, so a revoked duplicate is not shown as pending.
        var seeded = await SeedAsync("duplicate_consent");
        await using (var seedContext = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options))
        {
            seedContext.CustomerConsents.Add(new CustomerConsent
            {
                OrganizationId = seeded.OrgId,
                CustomerId = seeded.CustomerId,
                ConsentStatus = "revoked",
                ConsentRevokedAt = DateTime.UtcNow,
            });
            await seedContext.SaveChangesAsync();
        }

        var token = CreateToken(seeded.ClerkId, orgRole: Roles.BoutiqueStaff);
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers",
            token,
            new { fullName = "Nadia Client duplicate_consent", source = "counter_walkin" },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(seeded.CustomerId, body.GetProperty("duplicateOfCustomerId").GetGuid());
        Assert.Equal("revoked", body.GetProperty("consentStatus").GetString());
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
}
