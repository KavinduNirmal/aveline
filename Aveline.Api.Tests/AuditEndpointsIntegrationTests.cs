using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #241 — the audit log read surface. The audit subsystem was write-only:
/// <c>audit:view</c> and <c>AuditViewPolicy</c> were dead until the read endpoints
/// existed (finding C-1).
/// </summary>
public class AuditEndpointsIntegrationTests : IAsyncLifetime
{
    private const string DatabaseName = "AvelineInMemoryDb";

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

    private static AppDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: DatabaseName)
            .Options);

    private static async Task<Guid> SeedAuditEntryAsync(
        string entityType,
        string action,
        DateTime createdAt,
        Guid? organizationId = null,
        Guid? actorUserId = null)
    {
        await using var context = CreateContext();
        var entry = new AuditLogEntry
        {
            OrganizationId = organizationId,
            ActorKind = AuditActorKind.User,
            ActorUserId = actorUserId,
            ActorRef = "user_test",
            Action = action,
            EntityType = entityType,
            EntityId = Guid.CreateVersion7().ToString(),
            BeforeJson = null,
            AfterJson = """{"blossomDelta":250.0}""",
            Reason = "Goodwill credit.",
            RequestId = "req-audit-test",
            CreatedAt = createdAt,
        };
        context.AuditLogEntries.Add(entry);
        await context.SaveChangesAsync();
        return entry.Id;
    }

    private string CreateToken(string clerkId, string? userRole)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (userRole is not null) claims.Add(new Claim("user_role", userRole));

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

    /// <summary>
    /// The account-state gate (M-15) requires a local Active user before an /admin route
    /// beyond the onboarding exemption is reachable.
    /// </summary>
    private static async Task SeedActiveUserAsync(string clerkId)
    {
        await using var context = CreateContext();
        if (await context.Users.AnyAsync(u => u.ClerkId == clerkId))
        {
            return;
        }

        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Audit",
            LastName = "Admin",
            Username = clerkId,
            UserRole = Roles.Admin,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    private static async Task<JsonElement> BodyAsync(HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    [Fact]
    public async Task Admin_CanListAndReadAnAuditEntry()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var entityType = $"AuditTest_{suffix}";
        var entryId = await SeedAuditEntryAsync(
            entityType, "audit.test.created", DateTime.UtcNow.AddMinutes(-1));

        var token = CreateToken($"audit_admin_{suffix}", Roles.Admin);
        await SeedActiveUserAsync($"audit_admin_{suffix}");

        var list = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/audit?entityType={entityType}", token));
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        var page = await BodyAsync(list);
        Assert.Equal(1, page.GetProperty("page").GetInt32());
        Assert.Equal(50, page.GetProperty("pageSize").GetInt32());
        Assert.Equal(1, page.GetProperty("total").GetInt32());
        var item = page.GetProperty("items").EnumerateArray().Single();
        Assert.Equal(entryId.ToString(), item.GetProperty("id").GetString());
        Assert.Equal("audit.test.created", item.GetProperty("action").GetString());
        Assert.False(item.TryGetProperty("beforeJson", out _));
        Assert.False(item.TryGetProperty("afterJson", out _));

        var read = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/audit/{entryId}", token));
        Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        var entry = await BodyAsync(read);
        Assert.Equal(entryId.ToString(), entry.GetProperty("id").GetString());
        Assert.Equal(entityType, entry.GetProperty("entityType").GetString());
        Assert.Equal("Goodwill credit.", entry.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task NonAdmin_IsForbidden()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var entityType = $"AuditTest_{suffix}";
        var entryId = await SeedAuditEntryAsync(
            entityType, "audit.test.staff", DateTime.UtcNow);

        var token = CreateToken($"audit_staff_{suffix}", Roles.Staff);

        var list = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/audit", token));
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var read = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/audit/{entryId}", token));
        Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task Anonymous_IsUnauthorized()
    {
        var entryId = Guid.CreateVersion7();

        var list = await _client.GetAsync("/api/v1/admin/audit");
        Assert.Equal(HttpStatusCode.Unauthorized, list.StatusCode);

        var read = await _client.GetAsync($"/api/v1/admin/audit/{entryId}");
        Assert.Equal(HttpStatusCode.Unauthorized, read.StatusCode);
    }

    [Fact]
    public async Task FiltersAndPaging_Work()
    {
        var suffix = Guid.NewGuid().ToString("N");
        var entityType = $"AuditPage_{suffix}";
        var organizationId = Guid.CreateVersion7();
        var actorUserId = Guid.CreateVersion7();
        var now = DateTime.UtcNow;

        await SeedAuditEntryAsync(
            entityType, $"audit.page.oldest.{suffix}", now.AddMinutes(-30), organizationId, actorUserId);
        var middle = await SeedAuditEntryAsync(
            entityType, $"audit.page.middle.{suffix}", now.AddMinutes(-20), organizationId, actorUserId);
        var newest = await SeedAuditEntryAsync(
            entityType, $"audit.page.newest.{suffix}", now.AddMinutes(-10), organizationId, actorUserId);

        var token = CreateToken($"audit_paging_{suffix}", Roles.Admin);
        await SeedActiveUserAsync($"audit_paging_{suffix}");

        var firstPage = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/audit?entityType={entityType}&page=1&pageSize=2",
            token));
        Assert.Equal(HttpStatusCode.OK, firstPage.StatusCode);
        var first = await BodyAsync(firstPage);
        Assert.Equal(3, first.GetProperty("total").GetInt32());
        Assert.Equal(2, first.GetProperty("pageSize").GetInt32());
        var firstItems = first.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal(2, firstItems.Length);
        Assert.Equal(newest.ToString(), firstItems[0].GetProperty("id").GetString());
        Assert.Equal(middle.ToString(), firstItems[1].GetProperty("id").GetString());

        var secondPage = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/audit?entityType={entityType}&page=2&pageSize=2",
            token));
        var second = await BodyAsync(secondPage);
        Assert.Single(second.GetProperty("items").EnumerateArray());

        var byAction = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/audit?action=audit.page.middle.{suffix}",
            token));
        var actionPage = await BodyAsync(byAction);
        Assert.Equal(1, actionPage.GetProperty("total").GetInt32());

        var byOrg = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/audit?organizationId={organizationId}&actorUserId={actorUserId}&entityType={entityType}",
            token));
        var orgPage = await BodyAsync(byOrg);
        Assert.Equal(3, orgPage.GetProperty("total").GetInt32());

        var from = Uri.EscapeDataString(now.AddMinutes(-25).ToString("O"));
        var to = Uri.EscapeDataString(now.AddMinutes(-5).ToString("O"));
        var byWindow = await _client.SendAsync(Authorized(
            HttpMethod.Get,
            $"/api/v1/admin/audit?entityType={entityType}&from={from}&to={to}",
            token));
        var windowPage = await BodyAsync(byWindow);
        Assert.Equal(2, windowPage.GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task AuditList_ClampsAnOutOfRangePage()
    {
        var clerkId = $"audit_page_clamp_{Guid.NewGuid():N}";
        await SeedActiveUserAsync(clerkId);
        var token = CreateToken(clerkId, Roles.Admin);

        // An unbounded page wrapped (page - 1) * pageSize to a negative OFFSET, which
        // PostgreSQL rejects with a 500 (§3.8(a)).
        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, "/api/v1/admin/audit?page=2147483647", token));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.Equal(10_000, body.GetProperty("page").GetInt32());
    }

    [Fact]
    public async Task UnknownId_Returns404WithMessage()
    {
        var clerkId = $"audit_missing_{Guid.NewGuid():N}";
        await SeedActiveUserAsync(clerkId);
        var token = CreateToken(clerkId, Roles.Admin);

        var response = await _client.SendAsync(Authorized(
            HttpMethod.Get, $"/api/v1/admin/audit/{Guid.CreateVersion7()}", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var body = await BodyAsync(response);
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("message").GetString()));
    }
}
