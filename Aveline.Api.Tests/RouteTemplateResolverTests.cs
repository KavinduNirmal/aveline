using Aveline.Api.Modules.Statistics.Telemetry;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;

namespace Aveline.Api.Tests;

/// <summary>Issue #221 — route-template extraction must never leak the raw path (FR-6.2).</summary>
public class RouteTemplateResolverTests
{
    private static Endpoint EndpointFor(string template)
        => new RouteEndpoint(
            _ => Task.CompletedTask,
            RoutePatternFactory.Parse(template),
            order: 0,
            new EndpointMetadataCollection(),
            displayName: template);

    [Fact]
    public void ResolvesTheRouteTemplate_NotTheRawPath()
    {
        Assert.Equal("/api/v1/orgs/{organizationId}/usage", RouteTemplateResolver.Resolve(EndpointFor("api/v1/orgs/{organizationId}/usage")));
    }

    [Fact]
    public void PreservesAnAlreadyRootedTemplate()
    {
        Assert.Equal("/internal/agent-runs", RouteTemplateResolver.Resolve(EndpointFor("/internal/agent-runs")));
    }

    [Fact]
    public void ReturnsTheUnmatchedSentinel_WhenNoRouteMatched()
    {
        Assert.Equal(RouteTemplateResolver.Unmatched, RouteTemplateResolver.Resolve((Endpoint?)null));
    }

    [Fact]
    public void UsesTheContextEndpoint()
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(EndpointFor("api/v1/statistics/api/requests"));

        Assert.Equal("/api/v1/statistics/api/requests", RouteTemplateResolver.Resolve(context));
    }
}
