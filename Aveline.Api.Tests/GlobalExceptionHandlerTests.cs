using System.Text;
using System.Text.Json;
using Aveline.Api.Common.Exceptions;
using Aveline.Api.Configurations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #242 / M-7 — a single global exception handler returns a stable 500 envelope and
/// never leaks the exception message, type or stack trace to the caller.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, string Body, HttpResponse Response)> HandleAsync(
        Exception exception, bool https = false, string host = "aveline.test")
    {
        var handler = new GlobalExceptionHandler(
            NullLogger<GlobalExceptionHandler>.Instance,
            Options.Create(new HstsOptions()));
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/boom";
        if (https)
        {
            context.Request.Scheme = "https";
            context.Request.Host = new HostString(host);
        }

        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return (context.Response.StatusCode, await reader.ReadToEndAsync(), context.Response);
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsStable500Envelope()
    {
        var (status, body, _) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);

        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;
        Assert.Equal(StatusCodes.Status500InternalServerError, root.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(root.GetProperty("message").GetString()));
        Assert.True(root.TryGetProperty("traceId", out _));
    }

    [Fact]
    public async Task TryHandleAsync_DoesNotLeakExceptionDetail()
    {
        var exception = new InvalidOperationException(
            "Host=secret-host;Password=hunter2");

        var (_, body, _) = await HandleAsync(exception);
        var withoutStackFrames = string.Join(
            "\n",
            body.Split('\n').Where(line => !line.Contains("   at ", StringComparison.Ordinal)));

        Assert.DoesNotContain("hunter2", withoutStackFrames);
        Assert.DoesNotContain("secret-host", withoutStackFrames);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }

    [Fact]
    public async Task TryHandleAsync_ReappliesHardeningHeadersAfterTheFrameworkClearsThem()
    {
        var (status, _, response) = await HandleAsync(new InvalidOperationException("boom"));

        Assert.Equal(StatusCodes.Status500InternalServerError, status);
        Assert.Equal("nosniff", response.Headers["X-Content-Type-Options"].ToString());
        Assert.Equal("DENY", response.Headers["X-Frame-Options"].ToString());
        Assert.Equal("no-referrer", response.Headers["Referrer-Policy"].ToString());
        Assert.Contains("default-src 'none'", response.Headers["Content-Security-Policy"].ToString());
        Assert.Contains("camera=()", response.Headers["Permissions-Policy"].ToString());
    }

    [Fact]
    public async Task TryHandleAsync_AddsHstsToHttpsErrorResponses()
    {
        var (_, _, response) = await HandleAsync(
            new InvalidOperationException("boom"), https: true);

        Assert.Contains("max-age=", response.Headers.StrictTransportSecurity.ToString());
    }

    [Fact]
    public async Task TryHandleAsync_OmitsHstsForExcludedHosts()
    {
        var (_, _, response) = await HandleAsync(
            new InvalidOperationException("boom"), https: true, host: "localhost");

        Assert.True(string.IsNullOrEmpty(response.Headers.StrictTransportSecurity.ToString()));
    }
}
