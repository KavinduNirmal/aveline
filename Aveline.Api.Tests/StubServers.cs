using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.IdentityModel.Tokens;

namespace Aveline.Api.Tests;

/// <summary>
/// A minimal real Kestrel server that serves an OpenID Connect discovery document
/// and a JWKS for a locally-generated RSA key. Used so the app's JwtBearer handler
/// runs its real discovery -> signature -> issuer -> lifetime pipeline offline.
/// </summary>
public sealed class StubAuthServer : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly JsonElement _jwks;

    public StubAuthServer(RsaSecurityKey signingKey)
    {
        _app = CreateApp();
        _jwks = BuildJwks(signingKey);
    }

    public string BaseUrl { get; private set; } = string.Empty;

    public async Task StartAsync()
    {
        await _app.StartAsync();
        BaseUrl = _app.Urls.First().TrimEnd('/');
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    private WebApplication CreateApp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();

        app.MapGet("/.well-known/openid-configuration", (HttpContext context) =>
        {
            var issuer = $"{context.Request.Scheme}://{context.Request.Host}";
            return Results.Json(new
            {
                issuer,
                jwks_uri = $"{issuer}/.well-known/jwks.json",
            });
        });

        app.MapGet("/.well-known/jwks.json", () => Results.Json(_jwks));

        return app;
    }

    private static JsonElement BuildJwks(RsaSecurityKey signingKey)
    {
        var jwk = JsonWebKeyConverter.ConvertFromRSASecurityKey(signingKey);
        jwk.Kid = "test-kid";
        jwk.Alg = SecurityAlgorithms.RsaSha256;

        return JsonSerializer.SerializeToElement(new { keys = new[] { jwk } });
    }
}

/// <summary>
/// A minimal real Kestrel server that mirrors the agent service contract
/// (<c>POST /agents/ping</c>): records the internal token header and echoes the body.
/// </summary>
public sealed class StubAgentServer : IAsyncDisposable
{
    private readonly WebApplication _app;

    public StubAgentServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.Listen(IPAddress.Loopback, 0));
        _app = builder.Build();

        _app.MapPost("/agents/ping", async (HttpContext context) =>
        {
            ReceivedInternalToken = context.Request.Headers[InternalTokenHeaderName].FirstOrDefault();
            using var reader = new StreamReader(context.Request.Body);
            ReceivedBody = await reader.ReadToEndAsync();
            return Results.Text(ReceivedBody, "application/json", statusCode: StatusCodes.Status200OK);
        });
    }

    public const string InternalTokenHeaderName = "X-Internal-Token";

    public string BaseUrl { get; private set; } = string.Empty;
    public string? ReceivedInternalToken { get; private set; }
    public string? ReceivedBody { get; private set; }

    public async Task StartAsync()
    {
        await _app.StartAsync();
        BaseUrl = _app.Urls.First().TrimEnd('/');
    }

    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
