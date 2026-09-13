using System.Text;
using System.Text.Json;
using Aveline.Api.Common.Exceptions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace Aveline.Api.Tests;

/// <summary>
/// Issue #242 / M-7 — a single global exception handler returns a stable 500 envelope and
/// never leaks the exception message, type or stack trace to the caller.
/// </summary>
public class GlobalExceptionHandlerTests
{
    private static async Task<(int Status, string Body)> HandleAsync(Exception exception)
    {
        var handler = new GlobalExceptionHandler(NullLogger<GlobalExceptionHandler>.Instance);
        var context = new DefaultHttpContext();
        context.Request.Method = "POST";
        context.Request.Path = "/api/v1/boom";
        context.Response.Body = new MemoryStream();

        var handled = await handler.TryHandleAsync(context, exception, CancellationToken.None);

        Assert.True(handled);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        using var reader = new StreamReader(context.Response.Body, Encoding.UTF8);
        return (context.Response.StatusCode, await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task TryHandleAsync_ReturnsStable500Envelope()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("boom"));

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

        var (_, body) = await HandleAsync(exception);
        var withoutStackFrames = string.Join(
            "\n",
            body.Split('\n').Where(line => !line.Contains("   at ", StringComparison.Ordinal)));

        Assert.DoesNotContain("hunter2", withoutStackFrames);
        Assert.DoesNotContain("secret-host", withoutStackFrames);
        Assert.DoesNotContain("InvalidOperationException", body);
        Assert.DoesNotContain("   at ", body);
    }
}
