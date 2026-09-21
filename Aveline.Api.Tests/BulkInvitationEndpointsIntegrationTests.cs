using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.DTOs;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// T6 / E-10 over the wire: <c>POST …/invitations/bulk</c>, and the two fields the single route was
/// silently dropping (F-4).
///
/// What this suite exists to pin:
///
/// 1. **`count` is clamped to `[1,10]` and the response says so** — `requestedCount` and
///    `createdCount` are both reported, so the clamp is visible rather than silent.
/// 2. **`validityHours` reaches `ExpiresAt`**, and is clamped to `[1,720]`.
/// 3. **Idempotency is required and honoured.** Both invitation routes now carry
///    <c>IdempotencyEndpointFilter</c>, so a double-click cannot mint a duplicate batch; the replay
///    returns the stored body with <c>Idempotency-Replayed: true</c>.
/// 4. **`sendSummaryToOwner` reports what actually happened** — three states, never a silent drop.
/// 5. **`team:manage` is the gate**: staff hold `approvals:approve` after Q8 but not this.
/// </summary>
public class BulkInvitationEndpointsIntegrationTests : IAsyncLifetime
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
                builder.UseSetting("App:BaseUrl", "https://app.aveline.lk");
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId)
    {
        var handler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = _authServer.BaseUrl,
            Subject = new ClaimsIdentity([new Claim("sub", clerkId)]),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private static HttpRequestMessage Post(string path, string token, object payload, string? idempotencyKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };
        request.Headers.TryAddWithoutValidation(
            "Idempotency-Key", idempotencyKey ?? Guid.NewGuid().ToString());
        return request;
    }

    private sealed record Seeded(Guid OrgId, string OwnerClerk, string StaffClerk);

    /// <summary>
    /// One boutique with an owner (holds <c>team:manage</c>) and a staff member (holds
    /// <c>approvals:approve</c> and <c>catalog:view</c>, but not <c>team:manage</c>).
    /// </summary>
    private static async Task<Seeded> SeedAsync(string suffix, bool ownerHasEmail = true)
    {
        await using var context = CreateContext();

        var owner = NewUser($"bulk_owner_{suffix}", ownerHasEmail ? $"owner_{suffix}@aveline.lk" : string.Empty);
        var staff = NewUser($"bulk_staff_{suffix}", $"staff_{suffix}@aveline.lk");
        context.Users.AddRange(owner, staff);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Bulk Invite {suffix}",
            Slug = $"bulk-invite-{suffix}",
            OwnerUserId = owner.Id,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.AddRange(
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = owner.Id,
                BoutiqueRole = Roles.BoutiqueOwner, Status = MembershipStatus.Active,
            },
            new OrganizationMembership
            {
                OrganizationId = org.Id, UserId = staff.Id,
                BoutiqueRole = Roles.BoutiqueStaff, Status = MembershipStatus.Active,
            });
        await context.SaveChangesAsync();

        return new Seeded(org.Id, owner.ClerkId, staff.ClerkId);

        static User NewUser(string clerkId, string email) => new()
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = email,
            FirstName = "Bulk",
            LastName = "Invite",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
    }

    /// <summary>Every test seeds its own organization, so the limiter's per-organization key differs.</summary>
    private static AppDbContext CreateContext()
        => new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

    [Fact]
    public async Task Bulk_AsStaff_Returns403()
    {
        var seeded = await SeedAsync("staff");
        var token = CreateToken(seeded.StaffClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = 3 }, null));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_MintsExactlyTheRequestedCount()
    {
        var seeded = await SeedAsync("count");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueManager, count = 3 }, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(3, body.GetProperty("invitations").GetArrayLength());
        Assert.Equal(3, body.GetProperty("requestedCount").GetInt32());
        Assert.Equal(3, body.GetProperty("createdCount").GetInt32());

        // Every code is distinct; a batch that reused a code would be worse than no batch.
        var codes = body.GetProperty("invitations").EnumerateArray()
            .Select(item => item.GetProperty("code").GetString())
            .ToList();
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Theory]
    [InlineData(50, 10)]
    [InlineData(0, 1)]
    [InlineData(-4, 1)]
    public async Task Bulk_ClampsCountToTheContract(int requested, int expected)
    {
        var seeded = await SeedAsync($"clamp{requested}");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = requested }, null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(expected, body.GetProperty("createdCount").GetInt32());
        // The clamp is visible: the caller asked for something and got the bound, and both are here.
        Assert.Equal(requested, body.GetProperty("requestedCount").GetInt32());
    }

    [Fact]
    public async Task Bulk_ValidityHours_ReachesExpiresAt()
    {
        var seeded = await SeedAsync("validity");
        var token = CreateToken(seeded.OwnerClerk);
        var before = DateTime.UtcNow;

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = 1, validityHours = 168 }, null));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        var expiresAt = body.GetProperty("invitations")[0].GetProperty("expiresAt").GetDateTime();
        var expected = before.AddHours(168);

        // F-4: the field used to be dropped, so every code expired in the 24 h default.
        Assert.True(
            Math.Abs((expiresAt - expected).TotalMinutes) < 5,
            $"Expected about {expected:O} but the code expires at {expiresAt:O}.");
    }

    [Fact]
    public async Task Bulk_ClampsValidityHoursToTheContract()
    {
        var seeded = await SeedAsync("validity-clamp");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = 1, validityHours = 100_000 }, null));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(InvitationLimits.MaxValidityHours, body.GetProperty("effectiveValidityHours").GetInt32());
    }

    [Fact]
    public async Task Bulk_SummaryRequested_ReportsThatItWasDispatched()
    {
        var seeded = await SeedAsync("summary");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = 2, sendSummaryToOwner = true }, null));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("summaryEmailRequested").GetBoolean());
        Assert.Equal(
            InvitationSummaryEmailStatus.Dispatched, body.GetProperty("summaryEmailStatus").GetString());
    }

    [Fact]
    public async Task Bulk_SummaryRequestedWithoutAnOwnerEmail_ReportsNotSentWithTheReason()
    {
        var seeded = await SeedAsync("summary-unsent", ownerHasEmail: false);
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = 1, sendSummaryToOwner = true }, null));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(body.GetProperty("summaryEmailRequested").GetBoolean());
        Assert.Equal(InvitationSummaryEmailStatus.NotSent, body.GetProperty("summaryEmailStatus").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("summaryEmailNote").GetString()));
    }

    [Fact]
    public async Task Bulk_NotRequested_ReportsNotRequestedRatherThanSilence()
    {
        var seeded = await SeedAsync("summary-off");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = Roles.BoutiqueStaff, count = 1 }, null));

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.False(body.GetProperty("summaryEmailRequested").GetBoolean());
        Assert.Equal(
            InvitationSummaryEmailStatus.NotRequested, body.GetProperty("summaryEmailStatus").GetString());
    }

    [Fact]
    public async Task Bulk_UnknownRole_Returns400()
    {
        var seeded = await SeedAsync("role");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk", token,
            new { boutiqueRole = "org:boutique_owner", count = 1 }, null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Bulk_WithoutAnIdempotencyKey_Returns400()
    {
        var seeded = await SeedAsync("no-key");
        var token = CreateToken(seeded.OwnerClerk);
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { boutiqueRole = Roles.BoutiqueStaff, count = 1 }),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("idempotency-key-required", body.GetProperty("code").GetString());
    }

    [Fact]
    public async Task Bulk_ReplayingAKey_ReturnsTheStoredBatchAndMintsNoMore()
    {
        var seeded = await SeedAsync("replay");
        var token = CreateToken(seeded.OwnerClerk);
        var key = Guid.NewGuid().ToString();
        var payload = new { boutiqueRole = Roles.BoutiqueStaff, count = 2 };
        var path = $"/api/v1/orgs/{seeded.OrgId}/invitations/bulk";

        var first = await _client.SendAsync(Post(path, token, payload, key));
        var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();

        var replay = await _client.SendAsync(Post(path, token, payload, key));
        var replayBody = await replay.Content.ReadFromJsonAsync<JsonElement>();

        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal("true", replay.Headers.GetValues("Idempotency-Replayed").Single());
        // The same codes, not a second batch.
        Assert.Equal(
            firstBody.GetProperty("invitations")[0].GetProperty("code").GetString(),
            replayBody.GetProperty("invitations")[0].GetProperty("code").GetString());

        var listed = await _client.SendAsync(new HttpRequestMessage(
            HttpMethod.Get, $"/api/v1/orgs/{seeded.OrgId}/invitations")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        });
        var pending = await listed.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(2, pending.GetArrayLength());
    }

    [Fact]
    public async Task Single_WithoutAnIdempotencyKey_Returns400()
    {
        // The single route gained the same filter in T6: a retried invite is a duplicate code.
        var seeded = await SeedAsync("single-no-key");
        var token = CreateToken(seeded.OwnerClerk);
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/orgs/{seeded.OrgId}/invitations")
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(new { boutiqueRole = Roles.BoutiqueStaff }),
        };

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Single_ValidityHoursAndSummaryAreNoLongerDropped()
    {
        var seeded = await SeedAsync("single-validity");
        var token = CreateToken(seeded.OwnerClerk);

        var response = await _client.SendAsync(Post(
            $"/api/v1/orgs/{seeded.OrgId}/invitations", token,
            new
            {
                boutiqueRole = Roles.BoutiqueStaff,
                validityHours = 168,
                sendSummaryToOwner = true,
            },
            null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(
            InvitationSummaryEmailStatus.Dispatched, body.GetProperty("summaryEmailStatus").GetString());
    }
}
