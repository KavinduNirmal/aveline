using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Billing.Models;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

public class SearchEndpointsTests : IAsyncLifetime
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
                builder.UseSetting("Media:Provider", "database");
                builder.UseSetting("Media:ReadFromCloudinary", "false");
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
            Audience = "aveline-api",
            Claims = new Dictionary<string, object>
            {
                [ClaimTypes.NameIdentifier] = clerkId,
                ["sub"] = clerkId,
            },
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
            SigningCredentials = new SigningCredentials(_signingKey, SecurityAlgorithms.RsaSha256),
        };
        return handler.CreateToken(descriptor);
    }

    private async Task<(Organization Org, User User, OrganizationMembership Membership)> SeedOrgAndStaffAsync(
        string boutiqueRole = Roles.BoutiqueStaff)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var org = new Organization
        {
            Id = Guid.NewGuid(),
            Name = "Maison Ceylon",
            Slug = $"maison-{Guid.NewGuid():N}"[..20],
            PlanTier = PlanTier.Bloom,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var user = new User
        {
            Id = Guid.NewGuid(),
            ClerkId = $"user_{Guid.NewGuid():N}",
            Email = $"staff_{Guid.NewGuid():N}@example.com",
            UserRole = Roles.Staff,
            OrganizationRole = boutiqueRole,
            OrganizationId = org.Id.ToString(),
            AccountState = AccountState.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        var membership = new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = user.Id,
            BoutiqueRole = boutiqueRole,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };

        db.Organizations.Add(org);
        db.Users.Add(user);
        db.OrganizationMemberships.Add(membership);
        await db.SaveChangesAsync();

        return (org, user, membership);
    }

    [Fact]
    public async Task Search_ShortQuery_ReturnsBadRequest()
    {
        var (org, user, _) = await SeedOrgAndStaffAsync();
        var token = CreateToken(user.ClerkId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/search?q=a");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Search_NonMember_ReturnsForbidden()
    {
        var (org1, _, _) = await SeedOrgAndStaffAsync();
        var (_, user2, _) = await SeedOrgAndStaffAsync();
        var token = CreateToken(user2.ClerkId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{org1.Id}/search?q=silk");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Search_ValidQuery_ReturnsOkWithPagedEnvelope()
    {
        var (org, user, _) = await SeedOrgAndStaffAsync();
        var token = CreateToken(user.ClerkId);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/v1/orgs/{org.Id}/search?q=silk");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<SearchResultPageDto>();
        Assert.NotNull(result);
        Assert.Equal(1, result.Page);
        Assert.NotNull(result.Items);
    }
}
