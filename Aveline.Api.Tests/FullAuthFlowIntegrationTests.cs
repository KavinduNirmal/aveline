using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Integration test of the full auth flow, fully offline:
/// Clerk-style JWT -> Aveline.Api (real JwtBearer + JWKS pipeline) -> agent service
/// (internal X-Internal-Token) -> response, with user identity propagated.
/// See docs/tests/README.md.
/// </summary>
public class FullAuthFlowIntegrationTests : IAsyncLifetime
{
    private const string InternalToken = "test-internal-token";

    private RsaSecurityKey _signingKey = null!;
    private StubAuthServer _authServer = null!;
    private StubAgentServer _agentServer = null!;
    private WebApplicationFactory<Program> _factory = null!;
    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _signingKey = new RsaSecurityKey(RSA.Create(2048)) { KeyId = "test-kid" };

        _authServer = new StubAuthServer(_signingKey);
        await _authServer.StartAsync();

        _agentServer = new StubAgentServer();
        await _agentServer.StartAsync();

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl + "/");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
            });
        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _authServer.DisposeAsync();
        await _agentServer.DisposeAsync();
    }

    private static HttpRequestMessage PingRequest() => new(HttpMethod.Post, "/api/v1/agents/ping")
    {
        Content = new StringContent("{}", Encoding.UTF8, "application/json"),
    };

    private string CreateToken(string userId, string? userRole = null, string? orgRole = null, string? email = null)
    {
        var claims = new List<Claim> { new("sub", userId) };
        if (userRole is not null)
        {
            claims.Add(new Claim("user_role", userRole));
        }
        if (orgRole is not null)
        {
            claims.Add(new Claim("org_role", orgRole));
        }
        if (email is not null)
        {
            claims.Add(new Claim("email", email));
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

    [Fact]
    public async Task NoToken_Returns401()
    {
        var response = await _client.SendAsync(PingRequest());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(_agentServer.ReceivedInternalToken);
    }

    [Fact]
    public async Task AllResponses_Include_SecurityHeaders()
    {
        var response = await _client.SendAsync(PingRequest());

        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Equal("no-referrer", response.Headers.GetValues("Referrer-Policy").Single());

        var csp = response.Headers.GetValues("Content-Security-Policy").Single();
        Assert.Contains("default-src 'none'", csp);
        Assert.Contains("frame-ancestors 'none'", csp);

        var permissions = response.Headers.GetValues("Permissions-Policy").Single();
        Assert.Contains("camera=()", permissions);
        Assert.Contains("geolocation=()", permissions);
    }

    [Fact]
    public async Task HttpsResponses_Include_Hsts()
    {
        using var httpsClient = _factory.CreateClient(
            new WebApplicationFactoryClientOptions { BaseAddress = new Uri("https://aveline.test") });

        var response = await httpsClient.GetAsync("/health/live");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var hsts = response.Headers.GetValues("Strict-Transport-Security").Single();
        Assert.Contains("max-age=", hsts);
    }

    [Fact]
    public async Task InvalidToken_Returns401()
    {
        var request = PingRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "garbage.token.value");

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(_agentServer.ReceivedInternalToken);
    }

    [Fact]
    public async Task Claims_ReturnsTheEmailCarriedByTheToken()
    {
        // B1 (A9): the endpoint read FindFirstValue("email") against a principal whose
        // inbound claim mapping had already rewritten `email` to ClaimTypes.Email, so a
        // real bearer token yielded a null email. The JwtBearer pipeline below is the
        // real one (discovery -> signature -> issuer -> inbound map), so this test fails
        // unless the endpoint reads the claim type the principal actually carries.
        var token = CreateToken(
            "user_claims_email", userRole: "staff", email: "owner@boutique.lk");

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/auth/claims");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("owner@boutique.lk", body.GetProperty("email").GetString());
    }

    [Fact]
    public async Task ValidToken_FullFlow_AgentReceivesInternalToken_IdentityPropagated()
    {
        var token = CreateToken("user_123", userRole: "staff", orgRole: "org:boutique_supervisor");

        // Complete onboarding first so onboarding gate permits downstream access
        var onboardingReq = new HttpRequestMessage(HttpMethod.Post, "/api/v1/users/onboarding")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(new
                {
                    displayName = "Test Associate",
                    phoneNumber = "+94771234567",
                    address = "123 Galle Road, Colombo"
                }),
                Encoding.UTF8,
                "application/json")
        };
        onboardingReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var onboardingRes = await _client.SendAsync(onboardingReq);
        Assert.Equal(HttpStatusCode.OK, onboardingRes.StatusCode);

        var request = PingRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(InternalToken, _agentServer.ReceivedInternalToken);

        var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("user_123", body.GetProperty("userId").GetString());
        var roles = body.GetProperty("roles").EnumerateArray().Select(r => r.GetString()).ToArray();
        Assert.Equal(new[] { "staff", "org:boutique_supervisor" }, roles);
    }

    [Fact]
    public async Task ValidToken_WithoutRoles_Returns403_AndAgentNotCalled()
    {
        var request = PingRequest();
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Bearer",
            CreateToken("user_123"));

        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Null(_agentServer.ReceivedInternalToken);
    }

    [Fact]
    public async Task Handled500_StillCarriesSecurityHeaders_AndStableEnvelope()
    {
        const string clerkId = "user_hardening_500";
        await SeedActiveUserAsync(clerkId);
        var token = CreateToken(clerkId, userRole: "staff");

        // The failure probe is Development-only (M-14), so this test runs its own host.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Development");
                builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
                builder.UseSetting("Database:InMemoryName", TestDatabase.Name());
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl + "/");
                builder.UseSetting("AgentService:InternalToken", InternalToken);
            });
        using var client = factory.CreateClient();

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/policies/fallback/unhandled");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        // The framework clears the response before invoking the handler, so these must be
        // re-applied inside it (M-13 / reconciliation §3.6).
        Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'none'", response.Headers.GetValues("Content-Security-Policy").Single());

        var body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("Deliberate Development-only failure probe", body);
        var envelope = JsonDocument.Parse(body).RootElement;
        Assert.Equal(500, envelope.GetProperty("status").GetInt32());
        Assert.True(envelope.TryGetProperty("traceId", out _));
    }

    private static async Task SeedActiveUserAsync(string clerkId)
    {
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: TestDatabase.Name())
            .Options);

        if (await context.Users.AnyAsync(user => user.ClerkId == clerkId))
        {
            return;
        }

        context.Users.Add(new User
        {
            Id = Guid.CreateVersion7(),
            ClerkId = clerkId,
            Email = $"{clerkId}@aveline.lk",
            FirstName = "Hardening",
            LastName = "Probe",
            Username = clerkId,
            UserRole = "staff",
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }
}
