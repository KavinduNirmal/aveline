using Aveline.Api.Common.Middleware;

namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>
/// Propagates the inbound request correlation id to outbound internal-service calls so a
/// workflow can be traced from the API request through the agent service (FR-6.10).
/// </summary>
public sealed class CorrelationIdDelegatingHandler : DelegatingHandler
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CorrelationIdDelegatingHandler(IHttpContextAccessor httpContextAccessor)
        => _httpContextAccessor = httpContextAccessor;

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        var requestId = _httpContextAccessor.HttpContext?.GetRequestId();
        if (!string.IsNullOrEmpty(requestId)
            && !request.Headers.Contains(CorrelationIdMiddleware.RequestIdHeader))
        {
            request.Headers.TryAddWithoutValidation(CorrelationIdMiddleware.RequestIdHeader, requestId);
        }

        return base.SendAsync(request, cancellationToken);
    }
}
