using System.Security.Claims;
using Aveline.Api.Configurations;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// Verifies the JWT validation rules configured for Clerk tokens
/// (see <see cref="AuthenticationConfiguration.BuildTokenValidationParameters"/>).
/// Tokens are signed locally with a test RSA key, so these tests are offline and
/// independent of the live Clerk instance.
/// </summary>
public class JwtValidationTests
{
    private const string Authority = "https://test.clerk.accounts.dev";

    private static (string Token, RsaSecurityKey Key) CreateToken(
        string issuer,
        RsaSecurityKey signingKey,
        DateTime? expires = null,
        DateTime? notBefore = null,
        IEnumerable<Claim>? claims = null,
        string? audience = null)
    {
        var tokenHandler = new JsonWebTokenHandler();
        var descriptor = new SecurityTokenDescriptor
        {
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(signingKey, SecurityAlgorithms.RsaSha256),
            Expires = expires ?? DateTime.UtcNow.AddHours(1),
            NotBefore = notBefore,
            Subject = new ClaimsIdentity(claims ?? new[] { new Claim("sub", "user_123") }),
        };

        return (tokenHandler.CreateToken(descriptor), signingKey);
    }

    private static TokenValidationResult Validate(string token, RsaSecurityKey key)
    {
        var handler = new JsonWebTokenHandler();
        var parameters = AuthenticationConfiguration.BuildTokenValidationParameters(Authority);
        parameters.IssuerSigningKey = key;

        return handler.ValidateTokenAsync(token, parameters).GetAwaiter().GetResult();
    }

    private static RsaSecurityKey NewKey() => new(System.Security.Cryptography.RSA.Create(2048));

    [Fact]
    public void ValidToken_WithCorrectIssuer_Validates()
    {
        var key = NewKey();
        var (token, _) = CreateToken(Authority, key);

        var result = Validate(token, key);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ValidToken_ExposesSubAndRoleClaims()
    {
        var key = NewKey();
        var claims = new[]
        {
            new Claim("sub", "user_123"),
            new Claim("user_role", "associate"),
            new Claim("org_role", "org:admin"),
        };
        var (token, _) = CreateToken(Authority, key, claims: claims);

        var result = Validate(token, key);

        Assert.True(result.IsValid);
        Assert.Equal("user_123", result.ClaimsIdentity?.FindFirst("sub")?.Value);
        Assert.Equal("associate", result.ClaimsIdentity?.FindFirst("user_role")?.Value);
        Assert.Equal("org:admin", result.ClaimsIdentity?.FindFirst("org_role")?.Value);
    }

    [Fact]
    public void Token_SignedWithWrongKey_Fails()
    {
        var (token, _) = CreateToken(Authority, NewKey());

        var result = Validate(token, NewKey());

        Assert.False(result.IsValid);
    }

    [Fact]
    public void ExpiredToken_Fails()
    {
        var key = NewKey();
        var (token, _) = CreateToken(
            Authority,
            key,
            expires: DateTime.UtcNow.AddHours(-1),
            notBefore: DateTime.UtcNow.AddHours(-2));

        var result = Validate(token, key);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Token_NotYetValid_Fails()
    {
        var key = NewKey();
        var (token, _) = CreateToken(
            Authority,
            key,
            expires: DateTime.UtcNow.AddHours(1),
            notBefore: DateTime.UtcNow.AddHours(1));

        var result = Validate(token, key);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Token_WithWrongIssuer_Fails()
    {
        var key = NewKey();
        var (token, _) = CreateToken("https://other.clerk.accounts.dev", key);

        var result = Validate(token, key);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void Token_WithoutAudience_Validates()
    {
        var key = NewKey();
        var (token, _) = CreateToken(Authority, key);

        var result = Validate(token, key);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Token_WithMismatchedAudience_StillValidates()
    {
        var key = NewKey();
        var (token, _) = CreateToken(Authority, key, audience: "some-other-audience");

        var result = Validate(token, key);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Token_WithoutRoleClaims_Validates_AndHasNoRoles()
    {
        var key = NewKey();
        var claims = new[] { new Claim("sub", "user_123") };
        var (token, _) = CreateToken(Authority, key, claims: claims);

        var result = Validate(token, key);

        Assert.True(result.IsValid);
        Assert.Empty(result.ClaimsIdentity?.FindAll(ClaimTypes.Role) ?? []);
    }
}
