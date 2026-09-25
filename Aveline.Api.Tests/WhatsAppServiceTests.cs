using System.Net;
using System.Text;
using System.Text.Json;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.Extensions.Logging.Abstractions;

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

    /// <summary>
    /// Builds a service that also captures the exact request body, so a payload shape can be
    /// asserted rather than inferred. The body is read inside the handler because the request is
    /// disposed as soon as <c>SendAsync</c> returns.
    /// </summary>
    private static (WhatsAppService Service, Func<string?> Body) BuildRecordingService(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
    {
        string? body = null;
        var http = new HttpClient(new StubHandler(request =>
        {
            body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            return handler(request);
        }))
        {
            BaseAddress = new Uri("https://graph.facebook.com/v21.0/"),
        };
        return (new WhatsAppService(http, NullLogger<WhatsAppService>.Instance), () => body);
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
        // A transport failure carries no status: that is what makes it retryable at the
        // outbound layer without guessing from the error text (plan §6.2).
        Assert.Null(result.HttpStatus);
    }

    [Fact]
    public async Task SendMessageAsync_Success_ReportsHttpStatus()
    {
        var service = BuildService(_ => Json(HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.ABC123\"}]}"));

        var result = await service.SendMessageAsync("token", "111", "+94771234567", "Hello");

        Assert.True(result.IsSuccess);
        Assert.Equal(200, result.HttpStatus);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, 429)]
    [InlineData(HttpStatusCode.InternalServerError, 500)]
    [InlineData(HttpStatusCode.BadRequest, 400)]
    public async Task SendMessageAsync_Failure_ReportsHttpStatusSoCallersCanClassify(
        HttpStatusCode status, int expected)
    {
        var service = BuildService(_ => Json(status, "{\"error\":{\"message\":\"nope\"}}"));

        var result = await service.SendMessageAsync("token", "111", "+94771234567", "Hello");

        Assert.False(result.IsSuccess);
        // The status code, not the error string, is what separates "retry 429/5xx" from
        // "a 400 will stay 400" (plan §6.2).
        Assert.Equal(expected, result.HttpStatus);
    }

    [Fact]
    public async Task SendTemplateAsync_Success_ReturnsMessageId()
    {
        var (service, _) = BuildRecordingService(_ => Json(HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.TPL1\"}]}"));

        var result = await service.SendTemplateAsync(
            "token", "111", "+94771234567", "appointment_reminder", "en", null);

        Assert.True(result.IsSuccess);
        Assert.Equal("wamid.TPL1", result.MessageId);
        Assert.Equal(200, result.HttpStatus);
    }

    [Fact]
    public async Task SendTemplateAsync_PostsTemplatePayloadShape()
    {
        var (service, body) = BuildRecordingService(_ => Json(HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.TPL1\"}]}"));

        await service.SendTemplateAsync(
            "token", "111", "+94771234567", "appointment_reminder", "en_US", null);

        using var doc = JsonDocument.Parse(body()!);
        var root = doc.RootElement;
        Assert.Equal("whatsapp", root.GetProperty("messaging_product").GetString());
        Assert.Equal("individual", root.GetProperty("recipient_type").GetString());
        Assert.Equal("+94771234567", root.GetProperty("to").GetString());
        Assert.Equal("template", root.GetProperty("type").GetString());
        var template = root.GetProperty("template");
        Assert.Equal("appointment_reminder", template.GetProperty("name").GetString());
        Assert.Equal("en_US", template.GetProperty("language").GetProperty("code").GetString());
        // No components means the key is omitted rather than sent as an empty array: Meta
        // rejects an empty `components`, so an absent value must stay absent.
        Assert.False(template.TryGetProperty("components", out _));
    }

    [Fact]
    public async Task SendTemplateAsync_WithComponents_PostsComponentsArray()
    {
        var (service, body) = BuildRecordingService(_ => Json(HttpStatusCode.OK,
            "{\"messages\":[{\"id\":\"wamid.TPL1\"}]}"));

        var components = new object[]
        {
            new { type = "body", parameters = new object[] { new { type = "text", text = "Sarah" } } },
        };

        await service.SendTemplateAsync(
            "token", "111", "+94771234567", "appointment_reminder", "en", components);

        using var doc = JsonDocument.Parse(body()!);
        var sent = doc.RootElement.GetProperty("template").GetProperty("components");
        Assert.Equal(JsonValueKind.Array, sent.ValueKind);
        Assert.Equal(1, sent.GetArrayLength());
        Assert.Equal("body", sent[0].GetProperty("type").GetString());
    }

    [Fact]
    public async Task SendTemplateAsync_Failure_ReturnsErrorWithStatus()
    {
        var (service, _) = BuildRecordingService(_ => Json(HttpStatusCode.BadRequest,
            "{\"error\":{\"message\":\"template name does not exist\"}}"));

        var result = await service.SendTemplateAsync(
            "token", "111", "+94771234567", "missing_template", "en", null);

        Assert.False(result.IsSuccess);
        Assert.Equal(400, result.HttpStatus);
        Assert.Contains("template name does not exist", result.Error);
    }

    [Fact]
    public async Task SendTemplateAsync_Throws_ReturnsErrorWithoutStatus()
    {
        var service = BuildService(_ => throw new HttpRequestException("dns failure"));

        var result = await service.SendTemplateAsync(
            "token", "111", "+94771234567", "appointment_reminder", "en", null);

        Assert.False(result.IsSuccess);
        Assert.Null(result.HttpStatus);
        Assert.Contains("dns failure", result.Error);
    }

    [Fact]
    public async Task SendTemplateAsync_RejectsMissingTemplateNameOrLanguage()
    {
        var service = BuildService(_ => Json(HttpStatusCode.OK, "{}"));

        await Assert.ThrowsAsync<ArgumentException>(() => service.SendTemplateAsync(
            "token", "111", "+94771234567", " ", "en", null));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SendTemplateAsync(
            "token", "111", "+94771234567", "appointment_reminder", "", null));
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
