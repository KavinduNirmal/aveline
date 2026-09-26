using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Commerce.Models;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// T2 — customer CRUD on the tenant surface. The surface had only list, highlights, walk-in create
/// and record-interaction, so a customer detail page would have shown a visit count with nothing
/// behind it, and the <c>Location</c> header <c>POST …/interactions</c> already emits
/// (<c>CustomerTenantEndpoints.cs:109-110</c>) pointed at a route that did not exist.
///
/// Two semantics are pinned here rather than left to convention: the <b>409 phone conflict</b> (an
/// already-observed gap — the create path returned 500 because there is no <c>DbUpdateException</c>
/// mapping on this surface) and the <b>idempotent soft delete</b>.
/// </summary>
public class CustomerTenantCrudTests : IAsyncLifetime
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
        if (body is not null) request.Content = JsonContent.Create(body);
        if (idempotencyKey is not null) request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private sealed record Seeded(
        Guid OrgId,
        string StaffClerkId,
        string ManagerClerkId,
        Guid CustomerId,
        Guid OtherCustomerId);

    /// <summary>
    /// Seeds one organization with a <b>manager</b> membership, a <b>staff</b> membership and two
    /// clients. The role matters: `customers:manage` is held by manager/supervisor/owner and not by
    /// staff, so the two memberships are how the write gate is probed.
    /// </summary>
    private static async Task<Seeded> SeedAsync(string suffix, string? openOrderStatus = null)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

        var manager = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"crud_manager_{suffix}",
            Email = $"crud_manager_{suffix}@aveline.lk",
            FirstName = "Mira",
            LastName = "Manager",
            Username = $"crud_manager_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        var staff = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = $"crud_staff_{suffix}",
            Email = $"crud_staff_{suffix}@aveline.lk",
            FirstName = "Sam",
            LastName = "Staff",
            Username = $"crud_staff_{suffix}",
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.AddRange(manager, staff);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"CRUD Org {suffix}",
            Slug = $"crud-{suffix}",
            OwnerUserId = manager.Id,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = org.Id,
                UserId = manager.Id,
                BoutiqueRole = Roles.BoutiqueManager,
                Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id,
                UserId = staff.Id,
                BoutiqueRole = Roles.BoutiqueStaff,
                Status = MembershipStatus.Active,
            });

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = "+94771000001",
            FullName = "Nadia Client",
            Email = "nadia@example.lk",
            Status = "returning",
            Level = "level2",
            TotalSpent = 42000m,
            VisitCount = 3,
            LastVisitAt = DateTime.UtcNow.AddDays(-4),
        };
        var other = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = "+94771000002",
            FullName = "Ravi Client",
            Status = "new",
        };
        context.Customers.AddRange(customer, other);
        await context.SaveChangesAsync();

        context.CustomerPreferences.Add(new CustomerPreference
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            PreferenceKey = "nickname",
            PreferenceValue = "Nadi",
        });
        context.CustomerTags.Add(new CustomerTag
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            Tag = "wedding-season",
        });
        context.CustomerInteractions.AddRange(
            new CustomerInteraction
            {
                OrganizationId = org.Id,
                CustomerId = customer.Id,
                Channel = "in_person",
                Direction = "inbound",
                MessageContent = "Fitting for the reception.",
                CreatedAt = DateTime.UtcNow.AddDays(-4),
            },
            new CustomerInteraction
            {
                OrganizationId = org.Id,
                CustomerId = customer.Id,
                Channel = "whatsapp",
                Direction = "inbound",
                MessageContent = "Asked about the silk saree.",
                CreatedAt = DateTime.UtcNow.AddDays(-2),
            });
        await context.SaveChangesAsync();

        if (openOrderStatus is not null)
        {
            context.Orders.Add(new Order
            {
                OrganizationId = org.Id,
                CustomerId = customer.Id,
                CustomerName = "Nadia Client",
                Status = openOrderStatus,
                Subtotal = 42000m,
                Total = 42000m,
                CreatedAt = DateTime.UtcNow.AddDays(-1),
            });
            await context.SaveChangesAsync();
        }

        return new Seeded(org.Id, staff.ClerkId, manager.ClerkId, customer.Id, other.Id);
    }

    private static async Task<Guid> ManagerIdAsync(string clerkId)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);
        var user = await context.Users.AsNoTracking().FirstAsync(u => u.ClerkId == clerkId);
        return user.Id;
    }

    // ── E-6: read one client ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetCustomer_ReturnsTheTenantSafeDetail()
    {
        var seeded = await SeedAsync("detail");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(seeded.CustomerId, body.GetProperty("customerId").GetGuid());
        Assert.Equal("Nadia Client", body.GetProperty("fullName").GetString());
        Assert.Equal("Nadi", body.GetProperty("nickname").GetString());
        Assert.Equal("level2", body.GetProperty("level").GetString());
        Assert.Equal("returning", body.GetProperty("status").GetString());
        Assert.Equal(42000m, body.GetProperty("totalSpent").GetDecimal());
        Assert.Equal(3, body.GetProperty("visitCount").GetInt32());
        Assert.Equal(2, body.GetProperty("interactionCount").GetInt32());
        Assert.Contains("wedding-season", body.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
        Assert.True(body.GetProperty("loyaltyTierIsDerived").GetInt32() == 1);
    }

    [Fact]
    public async Task GetCustomer_DoesNotLeakAiContextOrInternalTokens()
    {
        var seeded = await SeedAsync("no_leak");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));

        var raw = await response.Content.ReadAsStringAsync();
        // The detail DTO is deliberately not the internal customer shape.
        foreach (var forbidden in new[] { "memory", "aiContext", "internalToken", "parsedIntent" })
        {
            Assert.DoesNotContain(forbidden, raw, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task GetCustomer_ForAnotherOrgsCustomer_Returns404()
    {
        var mine = await SeedAsync("detail_mine");
        var theirs = await SeedAsync("detail_theirs");
        var token = CreateToken(mine.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{mine.OrgId}/customers/{theirs.CustomerId}", token));

        // Indistinguishable from "does not exist", matching the by-slug precedent.
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCustomer_ForADeletedCustomer_Returns404()
    {
        var seeded = await SeedAsync("detail_deleted");
        // The delete needs `customers:manage`, so the manager identity drives this test; the read
        // back is then checked from the same caller.
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        // Delete it through the API, then read it back.
        var delete = await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));
        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── E-9: interaction history ─────────────────────────────────────────────────────────────────

    [Fact]
    public async Task GetInteractions_ReturnsThePagedHistoryNewestFirst()
    {
        var seeded = await SeedAsync("interactions");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/interactions",
            token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, body.GetProperty("total").GetInt32());
        var items = body.GetProperty("items").EnumerateArray().ToList();
        Assert.Equal("whatsapp", items[0].GetProperty("channel").GetString());
        Assert.Equal("Asked about the silk saree.", items[0].GetProperty("note").GetString());
        Assert.True(items[0].GetProperty("occurredAtUtc").GetDateTime() >
                    items[1].GetProperty("occurredAtUtc").GetDateTime());
    }

    [Fact]
    public async Task GetInteractions_ForAnotherOrgsCustomer_Returns404()
    {
        var mine = await SeedAsync("inter_mine");
        var theirs = await SeedAsync("inter_theirs");
        var token = CreateToken(mine.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/orgs/{mine.OrgId}/customers/{theirs.CustomerId}/interactions",
            token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ── E-7: update ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task PatchCustomer_AsStaff_Returns403()
    {
        var seeded = await SeedAsync("patch_staff");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token,
            new { fullName = "Renamed By Staff" }));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PatchCustomer_AsManager_UpdatesTheWritableFieldsOnly()
    {
        var seeded = await SeedAsync("patch_manager");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token,
            new
            {
                fullName = "Nadia Perera",
                nickname = "Nadi P",
                email = "nadia.perera@example.lk",
                level = "vip",
                // `status` is derived by the loyalty rule; sending it must not move it.
                status = "dormant",
            }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Nadia Perera", body.GetProperty("fullName").GetString());
        Assert.Equal("Nadi P", body.GetProperty("nickname").GetString());
        Assert.Equal("vip", body.GetProperty("level").GetString());
        Assert.Equal("returning", body.GetProperty("status").GetString());
    }

    [Fact]
    public async Task PatchCustomer_NormalisesThePhoneToE164()
    {
        var seeded = await SeedAsync("patch_phone");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token,
            new { phoneNumber = "077 123 4599" }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("+94771234599", body.GetProperty("phoneNumber").GetString());
    }

    [Fact]
    public async Task PatchCustomer_OnAPhoneAlreadyInUse_Returns409()
    {
        var seeded = await SeedAsync("patch_conflict");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Patch,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}",
            token,
            // The other client in the same organisation already owns this number.
            new { phoneNumber = "+94771000002" }));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("customer-phone-conflict", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task CreateWalkIn_OnAPhoneAlreadyInUse_Returns409Not500()
    {
        // The create path had no `DbUpdateException` mapping, so the unique
        // `(OrganizationId, PhoneNumber)` index turned a collision into a 500.
        var seeded = await SeedAsync("create_conflict");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers",
            token,
            new { fullName = "Different Person", phoneNumber = "+94771000002" },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("customer-phone-conflict", body.GetProperty("code").GetString());
    }

    // ── E-8: delete ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteCustomer_AsStaff_Returns403()
    {
        var seeded = await SeedAsync("delete_staff");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomer_SoftDeletesAndIsIdempotent()
    {
        var seeded = await SeedAsync("delete_manager");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);
        var path = $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}";

        var first = await _client.SendAsync(Authorized(HttpMethod.Delete, path, token));
        Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);

        // Deleting the end state again is the same answer, not a 404: the soft-delete filter means
        // the caller could not tell the difference anyway.
        var second = await _client.SendAsync(Authorized(HttpMethod.Delete, path, token));
        Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        // The row still exists (soft delete), it is merely invisible.
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);
        var row = await context.Customers.IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == seeded.CustomerId);
        Assert.NotNull(row);
        Assert.NotNull(row!.DeletedAt);
    }

    [Fact]
    public async Task DeleteCustomer_WithAnOpenOrder_Returns409AndLeavesTheClientIntact()
    {
        var seeded = await SeedAsync("delete_open_order", openOrderStatus: "pending_hold");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);
        var path = $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}";

        var response = await _client.SendAsync(Authorized(HttpMethod.Delete, path, token));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("customer-has-open-orders", body.GetProperty("code").GetString());
        Assert.Equal(1, body.GetProperty("openOrders").GetInt32());

        var stillThere = await _client.SendAsync(Authorized(HttpMethod.Get, path, token));
        Assert.Equal(HttpStatusCode.OK, stillThere.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomer_WithOnlyTerminalOrders_Succeeds()
    {
        // A completed or cancelled order is not live business, so it must not block the delete.
        var seeded = await SeedAsync("delete_terminal_order", openOrderStatus: "completed");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}", token));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task DeleteCustomer_ForAnotherOrgsCustomer_Returns404()
    {
        var mine = await SeedAsync("delete_mine");
        var theirs = await SeedAsync("delete_theirs");
        var token = CreateToken(mine.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Delete, $"/api/v1/orgs/{mine.OrgId}/customers/{theirs.CustomerId}", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task TheInteractionLocationHeaderNowResolves()
    {
        // Before E-6 the `Location` header on the visit receipt pointed at a route that did not
        // exist. This is the assertion that closes that dangling contract.
        var seeded = await SeedAsync("location");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var created = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers/{seeded.CustomerId}/interactions",
            token,
            new
            {
                occurredAtUtc = DateTime.UtcNow,
                channel = "in_person",
                direction = "inbound",
                note = "Second fitting.",
            },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location;
        Assert.NotNull(location);

        var followed = await _client.SendAsync(Authorized(HttpMethod.Get, location!.ToString(), token));
        Assert.Equal(HttpStatusCode.OK, followed.StatusCode);
    }

    // ── E-9b: the customer Salon ────────────────────────────────────────────────────────────────

    [Fact]
    public async Task CreateWalkIn_CreatesTheCustomerSalon_WithTheAvelineGreeting()
    {
        // A client without a Salon is invisible to the Salon list, so the concierge thread for that
        // client does not exist until someone opens it by hand. Two clients in the demo boutique had
        // no Salon at all. Creating a client is the moment the thread becomes meaningful, so it is
        // created there - organization-shared (OwnerUserId NULL, ADR-021), greeted by Aveline.
        var seeded = await SeedAsync("walkin_salon");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Post,
            $"/api/v1/orgs/{seeded.OrgId}/customers",
            token,
            new { fullName = "Salon Client", phoneNumber = "+94771000099" },
            idempotencyKey: Guid.NewGuid().ToString()));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.Content.ReadFromJsonAsync<JsonElement>();
        var customerId = created.GetProperty("customerId").GetGuid();

        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

        var salon = await context.Conversations
            .AsNoTracking()
            .SingleAsync(c => c.OrganizationId == seeded.OrgId && c.CustomerId == customerId);

        Assert.Equal(ConversationKind.Salon, salon.Kind);
        Assert.Equal(ConversationStatus.Active, salon.Status);
        // Organization-shared: a customer-bound Salon must not be owned by the staff member who
        // happened to record the walk-in, or the client's thread would vanish for everyone else.
        Assert.Null(salon.OwnerUserId);
        Assert.False(string.IsNullOrWhiteSpace(salon.ThreadId));

        var messages = await context.Messages
            .AsNoTracking()
            .Where(m => m.ConversationId == salon.Id)
            .ToListAsync();
        Assert.Single(messages);
    }
}
