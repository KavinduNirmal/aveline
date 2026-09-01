namespace Aveline.Api.Infrastructure.Integrations;

/// <summary>
/// Adds the internal service token header to outbound requests to internal
/// services (e.g. the agent service) so they can authenticate the caller.
/// </summary>
public sealed class InternalServiceAuthHandler : DelegatingHandler
{
    public const string HeaderName = "X-Internal-Token";

    private readonly string _token;

    public InternalServiceAuthHandler(IConfiguration configuration)
    {
        var token = configuration["AgentService:InternalToken"];
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("AgentService:InternalToken is not configured.");
        }

        _token = token;
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        request.Headers.TryAddWithoutValidation(HeaderName, _token);
        return base.SendAsync(request, cancellationToken);
    }
}
