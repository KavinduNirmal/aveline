using System.Net.Http.Json;
using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Integrations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Endpoints that proxy calls to the internal Python agent service, attaching the
/// internal service token (see <see cref="InternalServiceAuthHandler"/>).
/// </summary>
public static class AgentEndpoints
{
    public static IEndpointRouteBuilder MapAgentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/agents/ping", async (
            ClaimsPrincipal user,
            IAgentServiceClient agentClient,
            CancellationToken cancellationToken) =>
        {
            var payload = new
            {
                userId = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub"),
                roles = user.FindAll(ClaimTypes.Role).Select(c => c.Value).ToArray(),
            };

            using var content = JsonContent.Create(payload);
            var response = await agentClient.PostAsync("/agents/ping", content, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            return Results.Text(body, "application/json", statusCode: (int)response.StatusCode);
        })
        .RequireAuthorization(AuthorizationConfiguration.AssociatesPolicy);

        return endpoints;
    }
}
