using System.Security.Claims;
using System.Security.Cryptography;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.ApiAccess.Authentication;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Shared.Models;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// The acceptance criterion for B1 (business KPIs §5.5): a principal built from a real,
/// signature-validated Clerk-shaped token — the same <c>sub</c>/<c>user_id</c>/<c>org_id</c>
/// claims the live <c>jwt-aveline-v1</c> template mints — produces an
/// <see cref="ApiRequestSample"/> whose <c>UserId</c>/<c>OrganizationId</c> are the seeded
/// GUIDs, resolved through the in-memory <see cref="IClaimIdentityMap"/> that
/// <see cref="ClaimIdentityMapRefresher"/> rebuilds off the request path.
/// </summary>
/// <remarks>
/// This drives <see cref="ApiTelemetryMiddleware"/> with the real
/// <see cref="AuthenticationConfiguration.BuildTokenValidationParameters"/> rather than only
/// synthesising a <see cref="ClaimsPrincipal"/>, so the claim-type mapping the bearer handler
/// performs is part of what is under test. The claims reach the middleware exactly as they
/// arrive in production; only the transport is skipped.
/// </remarks>
public class AttributionB1Tests
{
    private const string ClerkUserId = "user_2abcDEFghiJKL";
    private const string ClerkOrgId = "org_2xyzQRS";
    private const string Authority = "https://clerk.test";

    private static readonly RsaSecurityKey SigningKey = new(RSA.Create(2048)) { KeyId = "test-kid" };

    private static string CreateToken(string clerkId, string? clerkOrgId = null, string? userIdClaim = null)
    {
        var claims = new List<Claim>
        {
            new("sub", clerkId),
            new("user_id", userIdClaim ?? clerkId),
            new("user_role", "owner"),
        };

        if (clerkOrgId is not null)
        {
            claims.Add(new Claim("org_id", clerkOrgId));
            claims.Add(new Claim("org_role", "org:admin"));
        }

        var handler = new JsonWebTokenHandler();
        return handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Authority,
            Audience = "aveline-api",
            SigningCredentials = new SigningCredentials(SigningKey, SecurityAlgorithms.RsaSha256),
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddHours(1),
        });
    }

    /// <summary>
    /// Validates the token the way the API does and returns the resulting principal. This is
    /// what proves the claim chain: <c>NameClaimType = ClaimTypes.NameIdentifier</c> makes the
    /// bearer handler map <c>sub</c> so that RequestPrincipal can find it.
    /// </summary>
    private static async Task<ClaimsPrincipal> PrincipalFromTokenAsync(string token)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.RequireHttpsMetadata = false;
                options.TokenValidationParameters =
                    AuthenticationConfiguration.BuildTokenValidationParameters(Authority);
                options.TokenValidationParameters.IssuerSigningKey = SigningKey;
            });

        await using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IAuthenticationService>();
        var context = new DefaultHttpContext { RequestServices = provider };
        context.Request.Headers.Authorization = $"Bearer {token}";

        var result = await handler.AuthenticateAsync(context, JwtBearerDefaults.AuthenticationScheme);
        Assert.True(result.Succeeded, $"Token validation failed: {result.Failure?.Message}");
        return result.Principal!;
    }

    private static ApiTelemetryMiddleware Middleware(TelemetryChannel channel, IClaimIdentityMap map) =>
        new(
            _ => Task.CompletedTask,
            channel,
            Microsoft.Extensions.Options.Options.Create(new TelemetryOptions
            {
                Enabled = true,
                BufferCapacity = 100,
                SuccessSampleRate = 0,
                SlowRequestMs = 1000,
                IpHashSalt = "test-salt",
                ExcludedPaths = [],
            }),
            map,
            NullLogger<ApiTelemetryMiddleware>.Instance);

    private static async Task<ClaimIdentityMap> BuildMapAsync(params object[] entities)
    {
        var databaseName = $"AttrMap_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<AppDbContext>(options => options.UseInMemoryDatabase(databaseName));
        await using var provider = services.BuildServiceProvider();

        // `AddRange(params object[])` would box the array as a single entity, so add each one
        // explicitly.
        await using (var seed = provider.GetRequiredService<AppDbContext>())
        {
            foreach (var entity in entities)
            {
                seed.Add(entity);
            }

            await seed.SaveChangesAsync();
        }

        var map = new ClaimIdentityMap();
        var refresher = new ClaimIdentityMapRefresher(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Aveline.Api.Common.Jobs.InMemoryDistributedJobLock(),
            map,
            NullLogger<ClaimIdentityMapRefresher>.Instance);

        var processed = await refresher.RunAsync(CancellationToken.None);
        Assert.True(
            entities.Length == 0 || processed > 0,
            $"The claim map rebuilt {processed} entries from {entities.Length} seeded rows.");
        return map;
    }

    private static async Task<ApiRequestSample> InvokeAsync(ClaimsPrincipal principal, IClaimIdentityMap map)
    {
        var channel = new TelemetryChannel(10);
        var context = new DefaultHttpContext { User = principal };
        context.Request.Method = "GET";
        context.Request.Path = "/api/v1/users/me";

        await Middleware(channel, map).InvokeAsync(context);

        Assert.True(channel.TryRead(out var sample), "The telemetry middleware recorded no sample.");
        return sample;
    }

    private static User AUser(string clerkId, Guid id) => new()
    {
        Id = id,
        ClerkId = clerkId,
        Email = $"{clerkId}@attr.test",
        FirstName = "Ada",
        LastName = "Attributed",
        Username = clerkId,
        UserRole = "owner",
        OrganizationRole = "boutique_owner",
        HasCompletedOnboarding = true,
        AccountState = AccountState.Active,
    };

    [Fact]
    public async Task AClerkShapedUserIdIsAttributedToTheAvelineGuid()
    {
        var userId = Guid.CreateVersion7();
        var map = await BuildMapAsync(AUser(ClerkUserId, userId));

        var principal = await PrincipalFromTokenAsync(CreateToken(ClerkUserId));
        var sample = await InvokeAsync(principal, map);

        Assert.Equal(userId, sample.UserId);
    }

    [Fact]
    public async Task AClerkShapedOrgIdIsAttributedToTheAvelineGuid()
    {
        var userId = Guid.CreateVersion7();
        var organizationId = Guid.CreateVersion7();
        var map = await BuildMapAsync(
            AUser(ClerkUserId, userId),
            new Organization
            {
                Id = organizationId,
                Name = "Attributed Atelier",
                Slug = "attributed-atelier",
                ClerkOrgId = ClerkOrgId,
                OwnerUserId = userId,
            });

        var principal = await PrincipalFromTokenAsync(CreateToken(ClerkUserId, ClerkOrgId));
        var sample = await InvokeAsync(principal, map);

        Assert.Equal(userId, sample.UserId);
        Assert.Equal(organizationId, sample.OrganizationId);
    }

    [Fact]
    public async Task TheBearerHandlerMapsTheClerkSubjectOntoTheNameIdentifierClaim()
    {
        // This is the claim-chain half of B1. The handler's default inbound claim mapping
        // renames `sub` to ClaimTypes.NameIdentifier, so RequestPrincipal's second lookup
        // succeeds even though `user_id` itself carries a Clerk id. Pin the mapping, because
        // turning MapInboundClaims off would silently break attribution again.
        var principal = await PrincipalFromTokenAsync(CreateToken(ClerkUserId));

        Assert.Equal(ClerkUserId, principal.FindFirstValue(ClaimTypes.NameIdentifier));
        // The raw `sub` type no longer survives the mapping; the mapped type is the contract.
        Assert.Null(principal.FindFirstValue("sub"));
    }

    [Fact]
    public async Task ATrueGuidUserIdClaimStillWinsWithoutTheMap()
    {
        var userId = Guid.CreateVersion7();
        var otherUserId = Guid.CreateVersion7();
        // The map knows the Clerk id, but the token carries the GUID directly, as a future
        // token shape would. The Guid branch is consulted first (DR-7).
        var map = await BuildMapAsync(AUser(ClerkUserId, otherUserId));

        var principal = await PrincipalFromTokenAsync(
            CreateToken(ClerkUserId, userIdClaim: userId.ToString()));
        var sample = await InvokeAsync(principal, map);

        Assert.Equal(userId, sample.UserId);
    }

    [Fact]
    public async Task AnUnknownClerkIdLeavesTheSampleUnattributedAndIsCounted()
    {
        var map = await BuildMapAsync();

        var principal = await PrincipalFromTokenAsync(
            CreateToken($"user_absent_{Guid.NewGuid():N}"));
        var sample = await InvokeAsync(principal, map);

        Assert.Null(sample.UserId);
        Assert.Null(sample.OrganizationId);
        Assert.True(map.UnresolvedCount >= 1);
    }

    [Fact]
    public async Task AnApiKeyPrincipalIsAttributedFromItsOwnGuidClaims()
    {
        // Regression guard: the map must not change API-key traffic, whose claims are already
        // GUIDs. No map entries exist at all.
        var organizationId = Guid.CreateVersion7();
        var apiKeyId = Guid.CreateVersion7();
        var map = await BuildMapAsync();

        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ApiKeyClaimTypes.OrganizationId, organizationId.ToString()),
                new Claim(ApiKeyClaimTypes.ApiKeyId, apiKeyId.ToString()),
            ],
            authenticationType: "ApiKey"));

        var sample = await InvokeAsync(principal, map);

        Assert.Equal(organizationId, sample.OrganizationId);
        Assert.Equal(apiKeyId, sample.ApiKeyId);
        Assert.Null(sample.UserId);
    }

    [Fact]
    public async Task ASoftDeletedUserStillResolves()
    {
        var userId = Guid.CreateVersion7();
        var user = AUser(ClerkUserId, userId);
        user.DeletedAt = DateTime.UtcNow.AddDays(-1);
        var map = await BuildMapAsync(user);

        var principal = await PrincipalFromTokenAsync(CreateToken(ClerkUserId));
        var sample = await InvokeAsync(principal, map);

        Assert.Equal(userId, sample.UserId);
    }
}
