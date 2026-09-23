using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Authorization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #242 — environment-sensitive hardening: the demo policy endpoints are
/// Development-only (M-14). The Production readiness payload's version suppression (M-3)
/// is covered by <see cref="HealthEndpointsIntegrationTests"/>.
/// </summary>
public class EnvironmentHardeningIntegrationTests : IAsyncLifetime
{
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
                builder.UseEnvironment("Production");
                ApplyCommonSettings(builder);
                // Required by the Production startup guard (M-1).
                builder.UseSetting("Telemetry:IpHashSalt", "test-production-ip-salt");
                // Required by the Production scrape-token guard (S-1).
                builder.UseSetting("Metrics:ScrapeToken", "test-production-scrape-token");
                // Required by the Production media-provider guard (Q11): this host boots the safe
                // default (Media:Provider=database), which Production refuses without the
                // documented escape hatch.
                builder.UseSetting("Media:AllowDatabaseProviderInProduction", "true");
            });

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _agentServer.DisposeAsync();
        await _authServer.DisposeAsync();
    }

    private void ApplyCommonSettings(IWebHostBuilder builder)
    {
        builder.UseSetting("Clerk:Authority", _authServer.BaseUrl);
        builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
        builder.UseSetting("AgentService:BaseUrl", _agentServer.BaseUrl);
        builder.UseSetting("AgentService:InternalToken", "test-internal-token");
        builder.UseSetting("Observability:AgentIsCritical", "false");
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

    private static HttpRequestMessage Authorized(HttpMethod method, string path, string token) =>
        new(method, path)
        {
            Headers = { Authorization = new AuthenticationHeaderValue("Bearer", token) },
        };

    private static async Task SeedActiveUserAsync(string clerkId)
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
            FirstName = "Env",
            LastName = "User",
            Username = clerkId,
            UserRole = Roles.Staff,
            OrganizationRole = string.Empty,
            HasCompletedOnboarding = true,
            AccountState = AccountState.Active,
        });
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task DemoPolicyEndpoints_AreNotMappedInProduction()
    {
        // M-14: the fallback-policy demo route is a real 200 when mapped (Development)
        // and a 404 when unmapped (Production) for an authenticated caller.
        const string clerkId = "env_prod";
        await SeedActiveUserAsync(clerkId);

        var request = Authorized(HttpMethod.Get, "/api/v1/policies/fallback/authed-by-default", CreateToken(clerkId));
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DemoPolicyEndpoints_AreMappedInDevelopment()
    {
        const string clerkId = "env_dev";
        await SeedActiveUserAsync(clerkId);

        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder => ApplyCommonSettings(builder));
        using var client = factory.CreateClient();

        var request = Authorized(HttpMethod.Get, "/api/v1/policies/fallback/authed-by-default", CreateToken(clerkId));
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
