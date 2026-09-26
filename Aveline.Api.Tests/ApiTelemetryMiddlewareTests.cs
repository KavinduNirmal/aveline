using System.Collections.Frozen;
using System.Security.Claims;
using Aveline.Api.Modules.ApiAccess.Authentication;
using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #221 — the request telemetry middleware records the route template, honours the
/// exclusion list and kill switch, decides raw-log sampling, and drops the oldest sample
/// (incrementing the counter) when the bounded buffer is full.
/// </summary>
public class ApiTelemetryMiddlewareTests
{
    private const string RouteTemplate = "/api/v1/things/{id}";

    private static TelemetryOptions Options(Action<TelemetryOptions>? configure = null)
    {
        var options = new TelemetryOptions
        {
            Enabled = true,
            BufferCapacity = 100,
            SuccessSampleRate = 0,
            SlowRequestMs = 1000,
            IpHashSalt = "test-salt",
            ExcludedPaths = ["/health", "/health/live", "/health/ready", "/openapi"],
        };
        configure?.Invoke(options);
        return options;
    }

    private static DefaultHttpContext Context(string method = "GET", string path = "/api/v1/things/42")
    {
        var context = new DefaultHttpContext();
        context.Request.Method = method;
        context.Request.Path = path;
        context.SetEndpoint(new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(RouteTemplate),
            order: 0,
            new EndpointMetadataCollection(),
            displayName: RouteTemplate));
        return context;
    }

    private static ApiTelemetryMiddleware Middleware(
        TelemetryChannel channel,
        TelemetryOptions options,
        Action<HttpContext>? onRequest = null,
        IClaimIdentityMap? identities = null)
        => new(
            context =>
            {
                onRequest?.Invoke(context);
                return Task.CompletedTask;
            },
            channel,
            Microsoft.Extensions.Options.Options.Create(options),
            identities ?? new ClaimIdentityMap(),
            NullLogger<ApiTelemetryMiddleware>.Instance);

    [Fact]
    public async Task RecordsTheRouteTemplateAndAttribution()
    {
        var channel = new TelemetryChannel(100);
        var context = Context();
        context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        var organizationId = Guid.CreateVersion7();
        var apiKeyId = Guid.CreateVersion7();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ApiKeyClaimTypes.OrganizationId, organizationId.ToString()),
                new Claim(ApiKeyClaimTypes.ApiKeyId, apiKeyId.ToString()),
            ],
            authenticationType: "ApiKey"));

        await Middleware(channel, Options(), c => c.Response.StatusCode = 200).InvokeAsync(context);

        Assert.True(channel.TryRead(out var sample));
        Assert.Equal(RouteTemplate, sample.RouteTemplate);
        Assert.Equal(organizationId, sample.OrganizationId);
        Assert.Equal(apiKeyId, sample.ApiKeyId);
        Assert.Equal(200, sample.StatusCode);
        Assert.NotNull(sample.ClientIpHash);
        Assert.NotEqual(context.Connection.RemoteIpAddress?.ToString(), sample.ClientIpHash);
    }

    [Fact]
    public async Task UsesTheClaimOrganizationWhenNoApiKeyClaimIsPresent()
    {
        var channel = new TelemetryChannel(100);
        var context = Context();
        var organizationId = Guid.CreateVersion7();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("org_id", organizationId.ToString())], authenticationType: "Bearer"));

        await Middleware(channel, Options()).InvokeAsync(context);

        Assert.True(channel.TryRead(out var sample));
        Assert.Equal(organizationId, sample.OrganizationId);
        Assert.Null(sample.ApiKeyId);
    }

    [Theory]
    [InlineData("OPTIONS", "/api/v1/things/42")]
    [InlineData("GET", "/health")]
    [InlineData("GET", "/health/ready")]
    [InlineData("GET", "/openapi/v1.json")]
    public async Task ExcludedRequestsAreNotRecorded(string method, string path)
    {
        var channel = new TelemetryChannel(100);
        await Middleware(channel, Options()).InvokeAsync(Context(method, path));

        Assert.False(channel.TryRead(out _));
    }

    [Fact]
    public async Task TheKillSwitchDisablesRecording()
    {
        var channel = new TelemetryChannel(100);
        await Middleware(channel, Options(o => o.Enabled = false)).InvokeAsync(Context());
        Assert.False(channel.TryRead(out _));
    }

    [Fact]
    public async Task NonSuccessResponsesAreAlwaysMarkedForRawPersistence()
    {
        var channel = new TelemetryChannel(100);
        await Middleware(channel, Options(), c => c.Response.StatusCode = 500).InvokeAsync(Context());

        Assert.True(channel.TryRead(out var sample));
        Assert.True(sample.ShouldPersistRaw);
    }

    [Fact]
    public async Task FastSuccessResponsesFollowTheSampleRate()
    {
        var channel = new TelemetryChannel(100);
        await Middleware(channel, Options(o => o.SuccessSampleRate = 0), c => c.Response.StatusCode = 200)
            .InvokeAsync(Context());

        Assert.True(channel.TryRead(out var sample));
        Assert.False(sample.ShouldPersistRaw);
    }

    [Fact]
    public async Task WhenTheBufferIsFull_TheOldestSampleIsDroppedAndCounted()
    {
        var channel = new TelemetryChannel(1);
        var middleware = Middleware(channel, Options());

        await middleware.InvokeAsync(Context(path: "/api/v1/things/1"));
        await middleware.InvokeAsync(Context(path: "/api/v1/things/2"));
        await middleware.InvokeAsync(Context(path: "/api/v1/things/3"));

        Assert.Equal(2, channel.DroppedSamples);
        Assert.True(channel.TryRead(out _));
        Assert.False(channel.TryRead(out _));
    }

    [Fact]
    public async Task ResolvesAClerkShapedUserIdThroughTheClaimMap()
    {
        // B1 regression guard: a Clerk token's `user_id` is `user_…`, not a GUID, so the
        // middleware must find the Aveline GUID through the in-memory claim map.
        var channel = new TelemetryChannel(100);
        var context = Context();
        var userId = Guid.CreateVersion7();
        var map = new ClaimIdentityMap();
        map.Replace(
            new Dictionary<string, Guid> { ["user_clerk_1"] = userId }.ToFrozenDictionary(),
            FrozenDictionary<string, Guid>.Empty);
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("user_id", "user_clerk_1"), new Claim("sub", "user_clerk_1")],
            authenticationType: "Bearer"));

        await Middleware(channel, Options(), identities: map).InvokeAsync(context);

        Assert.True(channel.TryRead(out var sample));
        Assert.Equal(userId, sample.UserId);
    }

    [Fact]
    public async Task AFailingDownstreamRequestStillProducesASample()
    {
        var channel = new TelemetryChannel(100);
        var middleware = new ApiTelemetryMiddleware(
            _ => throw new InvalidOperationException("boom"),
            channel,
            Microsoft.Extensions.Options.Options.Create(Options()),
            new ClaimIdentityMap(),
            NullLogger<ApiTelemetryMiddleware>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => middleware.InvokeAsync(Context()));

        Assert.True(channel.TryRead(out var sample));
        Assert.Equal(RouteTemplate, sample.RouteTemplate);
    }
}
