using System.Diagnostics;
using Aveline.Api.Common.Middleware;
using Aveline.Api.Infrastructure.Integrations;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #176 — request correlation id (FR-6.10, FR-6.11, defect D-11).
/// </summary>
public class CorrelationIdMiddlewareTests
{
    private static CorrelationIdMiddleware CreateMiddleware(RequestDelegate next) =>
        new(next, NullLogger<CorrelationIdMiddleware>.Instance);

    [Fact]
    public async Task Invoke_WhenHeaderMissing_GeneratesRequestId_AndEchoesIt()
    {
        var context = new DefaultHttpContext();
        var nextCalled = false;

        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);

        Assert.True(nextCalled);
        var generated = context.Response.Headers[CorrelationIdMiddleware.RequestIdHeader].ToString();
        Assert.False(string.IsNullOrWhiteSpace(generated));
        Assert.True(CorrelationIdMiddleware.IsValidRequestId(generated));
        Assert.Equal(generated, context.GetRequestId());
    }

    [Fact]
    public async Task Invoke_WhenHeaderValid_EchoesClientValue()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.RequestIdHeader] = "6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60";

        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal(
            "6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60",
            context.Response.Headers[CorrelationIdMiddleware.RequestIdHeader].ToString());
        Assert.Equal("6f1c2a90-4b7e-4d21-9c3a-8e5f1b2d4c60", context.GetRequestId());
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("has/slash")]
    [InlineData("has;semicolon")]
    public async Task Invoke_WhenHeaderHasIllegalCharacters_Returns400_AndDoesNotCallNext(string invalid)
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.RequestIdHeader] = invalid;

        var nextCalled = false;
        var middleware = CreateMiddleware(_ => { nextCalled = true; return Task.CompletedTask; });
        await middleware.InvokeAsync(context);

        Assert.False(nextCalled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Fact]
    public async Task Invoke_WhenHeaderTooLong_Returns400()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.RequestIdHeader] = new string('a', 129);

        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("ABC-123._:")]
    [InlineData("0198f3c2-1a2b-7c3d-8e4f-5a6b7c8d9e0f")]
    public void IsValidRequestId_AcceptsAllowedCharacterSet(string value)
    {
        Assert.True(CorrelationIdMiddleware.IsValidRequestId(value));
    }

    [Fact]
    public async Task Invoke_WhenActivityPresent_EchoesTraceId()
    {
        using var activity = new Activity("test").Start();
        var context = new DefaultHttpContext();

        var middleware = CreateMiddleware(_ => Task.CompletedTask);
        await middleware.InvokeAsync(context);

        Assert.Equal(
            activity.Id,
            context.Response.Headers[CorrelationIdMiddleware.TraceIdHeader].ToString());
    }

    [Fact]
    public async Task DelegatingHandler_PropagatesRequestIdToOutboundRequest()
    {
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationIdMiddleware.RequestIdHeader] = "propagated-id";
        var accessor = new HttpContextAccessor { HttpContext = context };

        var recorder = new CapturingHandler();
        var handler = new CorrelationIdDelegatingHandler(accessor) { InnerHandler = recorder };

        var invoker = new HttpMessageInvoker(handler);
        await invoker.SendAsync(new HttpRequestMessage(HttpMethod.Get, "http://agent.test/agents/ping"), default);

        Assert.Equal("propagated-id", recorder.LastRequestId);
    }

    private sealed class CapturingHandler : HttpMessageHandler
    {
        public string? LastRequestId { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequestId = request.Headers.TryGetValues(
                CorrelationIdMiddleware.RequestIdHeader, out var values)
                ? values.FirstOrDefault()
                : null;
            return Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK));
        }
    }
}
