using System.Net;

namespace Aveline.Api.Tests;

/// <summary>
/// Test double that records the last request it receives and returns a 200 OK.
/// </summary>
internal sealed class CapturingHttpMessageHandler : HttpMessageHandler
{
    public HttpRequestMessage? LastRequest { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        LastRequest = request;
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));
    }
}
