using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Audit.Models;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Privacy.Services;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Item 4.4 (plan §11 Phase 4, §9.2 "Separate staff and customer surfaces"): staff revocation under
/// the existing <c>BoutiqueCustomerAccessPolicy</c>. Two properties matter:
///
/// <list type="bullet">
///   <item>the route is policy-enforced - an unauthenticated caller gets 401 and a staff member
///     without <c>customers:manage</c> gets 403;</item>
///   <item>a staff revocation is recorded with a different <c>ActorKind</c> and <c>Source</c> than
///     the customer OTP path, which is the only reason two routes exist.</item>
/// </list>
/// </summary>
public class StaffConsentRevocationTests : IAsyncLifetime
{
    private const string PrivacyKey = "AQIDBAUGBwgJCgsMDQ4PEBESExQVFhcYGRobHB0eHyA=";

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
                builder.UseSetting("Privacy:LinkSigningKey", PrivacyKey);
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string clerkId, string? orgRole = null)
    {
        var claims = new List<Claim> { new("sub", clerkId) };
        if (orgRole is not null)
        {
            claims.Add(new Claim("org_role", orgRole));
        }

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

    private sealed record Seeded(Guid OrgId, Guid CustomerId, string StaffClerkId, string ManagerClerkId);

    private static async Task<Seeded> SeedAsync(string suffix)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        var owner = NewUser($"staff_owner_{suffix}", Roles.BoutiqueOwner);
        var staff = NewUser($"staff_member_{suffix}", Roles.BoutiqueStaff);
        var manager = NewUser($"staff_manager_{suffix}", Roles.BoutiqueManager);
        context.Users.AddRange(owner, staff, manager);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = $"Staff Org {suffix}",
            Slug = $"staff-{suffix}",
            OwnerUserId = owner.Id,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.AddRange(
            Membership(org.Id, owner.Id, Roles.BoutiqueOwner),
            Membership(org.Id, staff.Id, Roles.BoutiqueStaff),
            Membership(org.Id, manager.Id, Roles.BoutiqueManager));

        var customer = new Customer
        {
            OrganizationId = org.Id,
            PhoneNumber = UniquePhone(suffix),
            FullName = "Nadia Client",
            Status = "new",
        };
        context.Customers.Add(customer);
        context.CustomerConsents.Add(new CustomerConsent
        {
            OrganizationId = org.Id,
            CustomerId = customer.Id,
            ConsentStatus = ConsentStatuses.Granted,
            ConsentGrantedAt = DateTime.UtcNow,
            CreatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return new Seeded(org.Id, customer.Id, staff.ClerkId, manager.ClerkId);
    }

    private static User NewUser(string suffix, string boutiqueRole) => new()
    {
        Id = Guid.CreateVersion7(),
        ClerkId = suffix,
        Email = $"{suffix}@aveline.lk",
        FirstName = "Staff",
        LastName = "Member",
        Username = suffix,
        UserRole = Roles.Staff,
        OrganizationRole = boutiqueRole,
        HasCompletedOnboarding = true,
        AccountState = AccountState.Active,
    };

    /// <summary>A deterministic 9-digit subscriber number unique per suffix.</summary>
    private static string UniquePhone(string suffix)
    {
        var hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(suffix));
        var digits = string.Concat(hash.Take(6).Select(b => (char)('0' + (b % 10))));
        return $"+9477{digits}";
    }

    private static OrganizationMembership Membership(Guid orgId, Guid userId, string role) => new()
    {
        OrganizationId = orgId,
        UserId = userId,
        BoutiqueRole = role,
        Status = MembershipStatus.Active,
    };

    private async Task<HttpResponseMessage> PostConsentAsync(
        Guid orgId, Guid customerId, string status, string? token)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Post, $"/api/v1/orgs/{orgId}/customers/{customerId}/consent")
        {
            Content = JsonContent.Create(new { consentStatus = status }),
        };
        if (token is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }

        return await _client.SendAsync(request);
    }

    [Fact]
    public async Task WithoutAToken_TheStaffConsentRouteIsUnauthorized()
    {
        var seeded = await SeedAsync("anon");

        var response = await PostConsentAsync(seeded.OrgId, seeded.CustomerId, "revoked", token: null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task AStaffMemberWithoutCustomersManage_IsForbidden()
    {
        // `customers:view` is held by every role; `customers:manage` is not. A consent write is a
        // management act, so the access-level role must not be able to perform it.
        var seeded = await SeedAsync("staffonly");
        var token = CreateToken(seeded.StaffClerkId, orgRole: Roles.BoutiqueStaff);

        var response = await PostConsentAsync(seeded.OrgId, seeded.CustomerId, "revoked", token);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task AManagerCanRevokeAndTheAuditRecordsAUserActorDistinctFromTheCustomerPath()
    {
        var seeded = await SeedAsync("manager");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await PostConsentAsync(seeded.OrgId, seeded.CustomerId, "revoked", token);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("revoked", body.GetProperty("consentStatus").GetString());

        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);

        Assert.Equal(
            ConsentStatuses.Revoked,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == seeded.CustomerId)).ConsentStatus);

        var consentAudit = await context.ConsentAuditEntries
            .FirstAsync(a => a.CustomerId == seeded.CustomerId && a.Action == AuditAction.ConsentRevoked);
        // The customer OTP path writes ActorKind "Customer" and Source "otp_link"; a staff write must
        // not be mistakable for it.
        Assert.Equal(ConsentActorKinds.User, consentAudit.ActorKind);
        Assert.Equal(ConsentSources.Staff, consentAudit.Source);
        Assert.NotNull(consentAudit.ActorUserId);

        Assert.True(await context.AuditLogEntries.AnyAsync(
            a => a.OrganizationId == seeded.OrgId
                 && a.Action == AuditAction.ConsentRevoked
                 && a.ActorKind == AuditActorKind.User));
    }

    [Fact]
    public async Task AnUnknownStatus_IsA400WithTheTypedCode()
    {
        var seeded = await SeedAsync("badstatus");
        var token = CreateToken(seeded.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await PostConsentAsync(seeded.OrgId, seeded.CustomerId, "bogus", token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("invalid-consent-status", await response.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AClientInAnotherBoutique_IsNotFound()
    {
        // Tenant isolation: the route is org-scoped, so another organisation's client id must not
        // be revocable even by a manager of the caller's own organisation.
        var mine = await SeedAsync("mine");
        var theirs = await SeedAsync("theirs");
        var token = CreateToken(mine.ManagerClerkId, orgRole: Roles.BoutiqueManager);

        var response = await PostConsentAsync(mine.OrgId, theirs.CustomerId, "revoked", token);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options);
        Assert.Equal(
            ConsentStatuses.Granted,
            (await context.CustomerConsents.SingleAsync(c => c.CustomerId == theirs.CustomerId)).ConsentStatus);
    }
}
