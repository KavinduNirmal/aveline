using System.Net;
using System.Text;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Aveline.Api.Tests;

public class WhatsAppServiceTests
{
    private static WhatsAppService BuildService(Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        var http = new HttpClient(new StubHandler(handler))
        {
            BaseAddress = new Uri("https://graph.facebook.com/v21.0/"),
        };
        return new WhatsAppService(http, NullLogger<WhatsAppService>.Instance);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    [Fact]
    public async Task TestConnectionAsync_Success_ReturnsValid()
    {
        var service = BuildService(_ => Json(HttpStatusCode.OK, "{\"id\":\"111\"}"));

        var result = await service.TestConnectionAsync("token", "111");

        Assert.True(result.IsValid);
        Assert.Null(result.Error);
    }

    [Fact]
    public async Task TestConnectionAsync_Failure_ReturnsInvalidWithError()
    {
        var service = BuildService(_ => Json(HttpStatusCode.Unauthorized,
            "{\"error\":{\"message\":\"Invalid OAuth access token\"}}"));

        var result = await service.TestConnectionAsync("bad-token", "111");

        Assert.False(result.IsValid);
        Assert.Contains("Invalid OAuth access token", result.Error);
    }

    [Fact]
    public async Task TestConnectionAsync_Throws_ReturnsInvalid()
    {
        var service = BuildService(_ => throw new HttpRequestException("network down"));

        var result = await service.TestConnectionAsync("token", "111");

        Assert.False(result.IsValid);
        Assert.Contains("network down", result.Error);
    }

    [Fact]
    public async Task SendMessageAsync_Success_ReturnsMessageId()
    {
        var service = BuildService(_ => Json(HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.ABC123\"}]}"));

        var result = await service.SendMessageAsync("token", "111", "+94771234567", "Hello");

        Assert.True(result.IsSuccess);
        Assert.Equal("wamid.ABC123", result.MessageId);
    }

    [Fact]
    public async Task SendMessageAsync_Failure_ReturnsError()
    {
        var service = BuildService(_ => Json(HttpStatusCode.Forbidden,
            "{\"error\":{\"message\":\"(#131030) Recipient phone number not in allowed list\"}}"));

        var result = await service.SendMessageAsync("token", "111", "+94771234567", "Hello");

        Assert.False(result.IsSuccess);
        Assert.Contains("not in allowed list", result.Error);
    }

    [Fact]
    public async Task SendMessageAsync_Throws_ReturnsError()
    {
        var service = BuildService(_ => throw new HttpRequestException("timeout"));

        var result = await service.SendMessageAsync("token", "111", "+94771234567", "Hello");

        Assert.False(result.IsSuccess);
        Assert.Contains("timeout", result.Error);
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}
