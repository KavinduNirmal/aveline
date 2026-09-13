using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Infrastructure.Integrations;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>Issue #59 — admin access request lifecycle with a faked Clerk client.</summary>
public class AdminApprovalFlowIntegrationTests : IAsyncLifetime
{
    private sealed class RecordingClerkAdminClient : IClerkAdminClient
    {
        public List<string> Granted = new();
        public bool ShouldFail { get; set; }

        public Task GrantAdminRoleAsync(string clerkUserId, CancellationToken cancellationToken = default)
        {
            if (ShouldFail)
            {
                throw new HttpRequestException("Clerk unavailable");
            }
            Granted.Add(clerkUserId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ClerkSession>> ListSessionsAsync(
            string clerkUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<ClerkSession>>([]);

        public Task<int> RevokeAllSessionsAsync(
            string clerkUserId, CancellationToken cancellationToken = default) =>
            Task.FromResult(0);
    }

    private readonly RecordingClerkAdminClient _clerk = new();

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
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IClerkAdminClient>();
                    services.AddSingleton<IClerkAdminClient>(_clerk);
                });
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? userRole = null, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (userRole != null) claims.Add(new Claim("user_role", userRole));
        if (email != null) claims.Add(new Claim("email", email));
        if (email != null) claims.Add(new Claim("first_name", "Request"));
        if (email != null) claims.Add(new Claim("last_name", "User"));

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

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    /// <summary>
    /// The account-state gate (M-15) requires a local Active user before the review routes
    /// (`/admin/requests/{id}/*`) are reachable; submitting a request stays exempt.
    /// </summary>
    private static async Task SeedActiveUserAsync(string clerkId, string role = Roles.Admin)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        if (await context.Users.AnyAsync(u => u.ClerkId == clerkId))
        {
            return;
        }

        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Review",
            LastName = "Admin",
            Username = clerkId,
            UserRole = role,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task User_SubmitsRequest_IsIdempotent()
    {
        var token = CreateToken("admin_req_user", "staff", "req.user@aveline.lk");

        var first = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests", token));
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Pending", firstDoc.GetProperty("status").GetString());
        var id = firstDoc.GetProperty("id").GetString();

        var second = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests", token));
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal(id, secondDoc.GetProperty("id").GetString());
    }

    [Fact]
    public async Task NonReviewer_CannotListOrApprove()
    {
        var token = CreateToken("admin_req_staff", "staff", "plain.staff@aveline.lk");
        await SeedActiveUserAsync("admin_req_staff", Roles.Staff);

        var list = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/admin/requests", token));
        Assert.Equal(HttpStatusCode.Forbidden, list.StatusCode);

        var approve = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests/00000000-0000-0000-0000-000000000000/approve", token));
        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
    }

    [Fact]
    public async Task Reviewer_Approves_GrantsRoleViaClerk()
    {
        var requester = CreateToken("admin_req_approve", "staff", "approve.me@aveline.lk");
        var submit = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests", requester));
        var id = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString();

        var reviewer = CreateToken("admin_reviewer_1", "admin", "reviewer@aveline.lk");
        await SeedActiveUserAsync("admin_reviewer_1");
        var approve = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/admin/requests/{id}/approve", reviewer));

        Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var doc = JsonDocument.Parse(await approve.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("Approved", doc.GetProperty("status").GetString());
        Assert.Contains("admin_req_approve", _clerk.Granted);
    }

    [Fact]
    public async Task Moderator_CannotApproveTheirOwnRequest()
    {
        var token = CreateToken("admin_req_self", "moderator", "self.approver@aveline.lk");
        await SeedActiveUserAsync("admin_req_self", Roles.Moderator);

        var submit = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests", token));
        Assert.Equal(HttpStatusCode.OK, submit.StatusCode);
        var id = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString();

        var approve = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/admin/requests/{id}/approve", token));

        Assert.Equal(HttpStatusCode.Forbidden, approve.StatusCode);
        Assert.DoesNotContain("admin_req_self", _clerk.Granted);
    }

    [Fact]
    public async Task Reviewer_Rejects_AndResubmissionAllowed_ButApproveAfterRejectConflicts()
    {
        var requester = CreateToken("admin_req_reject", "staff", "reject.me@aveline.lk");
        var submit = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests", requester));
        var id = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString();

        var reviewer = CreateToken("admin_reviewer_2", "admin", "reviewer2@aveline.lk");
        await SeedActiveUserAsync("admin_reviewer_2");
        var reject = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/admin/requests/{id}/reject", reviewer));
        Assert.Equal(HttpStatusCode.OK, reject.StatusCode);

        var after = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/admin/requests/{id}/approve", reviewer));
        Assert.Equal(HttpStatusCode.Conflict, after.StatusCode);
        Assert.DoesNotContain("admin_req_reject", _clerk.Granted);
    }

    [Fact]
    public async Task ReviewerApproval_WhenClerkFails_Returns502_AndStaysPending()
    {
        var requester = CreateToken("admin_req_fail", "staff", "fail.me@aveline.lk");
        var submit = await _client.SendAsync(
            Authorized(HttpMethod.Post, "/api/v1/admin/requests", requester));
        var id = JsonDocument.Parse(await submit.Content.ReadAsStringAsync()).RootElement
            .GetProperty("id").GetString();

        _clerk.ShouldFail = true;
        var reviewer = CreateToken("admin_reviewer_3", "admin", "reviewer3@aveline.lk");
        await SeedActiveUserAsync("admin_reviewer_3");
        var approve = await _client.SendAsync(
            Authorized(HttpMethod.Post, $"/api/v1/admin/requests/{id}/approve", reviewer));

        Assert.Equal(HttpStatusCode.BadGateway, approve.StatusCode);
        _clerk.ShouldFail = false;

        var reviewer2 = CreateToken("admin_reviewer_4", "admin", "reviewer4@aveline.lk");
        await SeedActiveUserAsync("admin_reviewer_4");
        var list = await _client.SendAsync(
            Authorized(HttpMethod.Get, "/api/v1/admin/requests", reviewer2));
        var doc = JsonDocument.Parse(await list.Content.ReadAsStringAsync()).RootElement;
        var pending = doc.EnumerateArray().FirstOrDefault(e => e.GetProperty("id").GetString() == id);
        Assert.Equal("Pending", pending.GetProperty("status").GetString());
    }
}
