using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration tests for tenant-scoped integration credential endpoints
/// (<c>PUT/GET/DELETE /api/v1/orgs/&#123;organizationId&#125;/integrations</c>).
/// </summary>
public class IntegrationEndpointsIntegrationTests : IAsyncLifetime
{
    private const string Base64Key = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="; // 32 zero bytes

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
                builder.UseSetting("Credentials:EncryptionKey", Base64Key);
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private string CreateToken(string userId, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (email != null) claims.Add(new Claim("email", email));

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

    private HttpRequestMessage AuthorizedJson(HttpMethod method, string path, string token, object payload) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
            Content = JsonContent.Create(payload),
        };

    private HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    private async Task<(User owner, Organization org)> SeedActiveOwnerAsync(string clerkId, string slug)
    {
        await using var context = CreateContext();
        var owner = new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Owner",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        };
        context.Users.Add(owner);
        await context.SaveChangesAsync();

        var org = new Organization
        {
            Name = slug.Replace('-', ' '),
            Slug = slug,
            OwnerUserId = owner.Id,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        };
        context.Organizations.Add(org);
        await context.SaveChangesAsync();

        context.OrganizationMemberships.Add(new OrganizationMembership
        {
            OrganizationId = org.Id,
            UserId = owner.Id,
            BoutiqueRole = Roles.BoutiqueOwner,
            Status = MembershipStatus.Active,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow,
        });
        await context.SaveChangesAsync();

        return (owner, org);
    }

    [Fact]
    public async Task Owner_SavesListsAndDeletesIntegration()
    {
        var (owner, org) = await SeedActiveOwnerAsync("int_owner_a", "integrations-a");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var put = await _client.SendAsync(AuthorizedJson(HttpMethod.Put,
            $"/api/v1/orgs/{org.Id}/integrations/whatsapp", token,
            new { credentials = new Dictionary<string, string> { ["accessToken"] = "wa-secret-token" } }));
        Assert.Equal(HttpStatusCode.OK, put.StatusCode);

        var get = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/integrations", token));
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var body = await get.Content.ReadAsStringAsync();
        Assert.Contains("WhatsApp", body);
        Assert.DoesNotContain("wa-secret-token", body); // never expose plaintext

        var del = await _client.SendAsync(Authorized(HttpMethod.Delete,
            $"/api/v1/orgs/{org.Id}/integrations/whatsapp", token));
        Assert.Equal(HttpStatusCode.NoContent, del.StatusCode);

        var getAfter = await _client.SendAsync(Authorized(HttpMethod.Get,
            $"/api/v1/orgs/{org.Id}/integrations", token));
        var afterBody = await getAfter.Content.ReadAsStringAsync();
        Assert.DoesNotContain("WhatsApp", afterBody);
    }

    [Fact]
    public async Task Owner_UnknownIntegrationType_Returns400()
    {
        var (owner, org) = await SeedActiveOwnerAsync("int_owner_bad", "integrations-bad");
        var token = CreateToken(owner.ClerkId, owner.Email);

        var put = await _client.SendAsync(AuthorizedJson(HttpMethod.Put,
            $"/api/v1/orgs/{org.Id}/integrations/snapchat", token,
            new { credentials = new Dictionary<string, string> { ["accessToken"] = "x" } }));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task Owner_MissingRequiredCredentialField_Returns400()
    {
        var (owner, org) = await SeedActiveOwnerAsync("int_owner_missing", "integrations-missing");
        var token = CreateToken(owner.ClerkId, owner.Email);

        // Instagram requires clientId/clientSecret/accessToken; only accessToken provided.
        var put = await _client.SendAsync(AuthorizedJson(HttpMethod.Put,
            $"/api/v1/orgs/{org.Id}/integrations/instagram", token,
            new { credentials = new Dictionary<string, string> { ["accessToken"] = "ig" } }));
        Assert.Equal(HttpStatusCode.BadRequest, put.StatusCode);
    }

    [Fact]
    public async Task UnauthenticatedRequest_Returns401()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/orgs/00000000-0000-0000-0000-000000000000/integrations");
        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Owner_CannotAccessAnotherOrgsIntegrations_Returns403()
    {
        var (ownerA, orgA) = await SeedActiveOwnerAsync("int_owner_a2", "integrations-a2");
        var (_, orgB) = await SeedActiveOwnerAsync("int_owner_b", "integrations-b");
        var tokenA = CreateToken(ownerA.ClerkId, ownerA.Email);

        // ownerA has no membership in orgB -> denied.
        var put = await _client.SendAsync(AuthorizedJson(HttpMethod.Put,
            $"/api/v1/orgs/{orgB.Id}/integrations/whatsapp", tokenA,
            new { credentials = new Dictionary<string, string> { ["accessToken"] = "x" } }));
        Assert.Equal(HttpStatusCode.Forbidden, put.StatusCode);
    }

    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: "AvelineInMemoryDb")
            .Options;
        return new AppDbContext(options);
    }
}
