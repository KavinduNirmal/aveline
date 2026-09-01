namespace Aveline.Api.Infrastructure.Integrations;

public interface IAgentServiceClient
{
    Task<HttpResponseMessage> PostAsync(
        string path,
        HttpContent content,
        CancellationToken cancellationToken = default);

    Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Typed HTTP client for the internal Python agent service.
/// The internal token header is added by <see cref="InternalServiceAuthHandler"/>.
/// </summary>
public sealed class AgentServiceClient : IAgentServiceClient
{
    private readonly HttpClient _httpClient;

    public AgentServiceClient(HttpClient httpClient) => _httpClient = httpClient;

    public Task<HttpResponseMessage> PostAsync(
        string path,
        HttpContent content,
        CancellationToken cancellationToken = default)
        => _httpClient.PostAsync(path, content, cancellationToken);

    public Task<HttpResponseMessage> GetAsync(
        string path,
        CancellationToken cancellationToken = default)
        => _httpClient.GetAsync(path, cancellationToken);
}
