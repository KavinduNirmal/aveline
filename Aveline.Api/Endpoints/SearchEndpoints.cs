using System.Security.Claims;
using Aveline.Api.Configurations;
using Aveline.Api.Modules.Conversations.Services;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Services;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Cross-entity search endpoint for the global SearchOverlay.
/// </summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/orgs/{organizationId:guid}/search")
            .WithTags("Search")
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueAccessPolicy);

        group.MapGet(string.Empty, async (
            Guid organizationId,
            [FromQuery] string? q,
            [FromQuery] string? scope,
            [FromQuery] int? page,
            [FromQuery] int? pageSize,
            ClaimsPrincipal principal,
            ICustomerTenantService customers,
            IVisualService visual,
            IConversationService conversations,
            IUserRepository users,
            CancellationToken ct) =>
        {
            var query = (q ?? string.Empty).Trim();
            if (query.Length < 2)
            {
                return Results.BadRequest(new
                {
                    message = "Query 'q' must be at least 2 characters.",
                });
            }

            var selectedScope = (scope ?? "all").Trim().ToLowerInvariant();
            var results = new List<SearchResultItemDto>();
            var p = Math.Max(1, page ?? 1);
            var ps = Math.Clamp(pageSize ?? 20, 1, 100);

            // 1. Catalog Search
            if (selectedScope is "all" or "catalog")
            {
                try
                {
                    var catalogHits = await visual.QueryCatalogAsync(
                        organizationId,
                        new CatalogQueryRequest
                        {
                            Search = query,
                            Page = 1,
                            PageSize = 20,
                        },
                        ct);

                    results.AddRange(catalogHits.Items.Select(c => new SearchResultItemDto(
                        "catalogItem",
                        c.Id,
                        c.ItemName,
                        $"SKU: {c.Sku} · {c.Price:N0} LKR",
                        1.0,
                        $"/catalog/{c.Id}")));
                }
                catch
                {
                    // Swallowed if catalog search fails
                }
            }

            // 2. Customers Search
            if (selectedScope is "all" or "customers")
            {
                try
                {
                    var customerHits = await customers.GetBookAsync(
                        organizationId,
                        search: query,
                        level: null,
                        page: 1,
                        pageSize: 20,
                        cancellationToken: ct);

                    results.AddRange(customerHits.Items.Select(c => new SearchResultItemDto(
                        "customer",
                        c.CustomerId,
                        c.FullName ?? "Client",
                        c.PhoneNumber ?? c.Level,
                        0.9,
                        $"/customers/{c.CustomerId}")));
                }
                catch
                {
                    // Swallowed if customer lookup fails
                }
            }

            // 3. Conversations Search
            if (selectedScope is "all" or "conversations")
            {
                try
                {
                    var userId = await ResolveUserIdAsync(principal, users, ct);
                    if (userId != Guid.Empty)
                    {
                        var (convList, _) = await conversations.ListAsync(
                            organizationId,
                            userId,
                            1,
                            50,
                            ct);

                        var matchingConvs = convList
                            .Where(cv => (cv.CustomerName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                                      || (cv.LastMessagePreview?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false))
                            .Take(20);

                        results.AddRange(matchingConvs.Select(cv => new SearchResultItemDto(
                            "conversation",
                            cv.Id,
                            cv.CustomerName ?? "Message Thread",
                            cv.LastMessagePreview,
                            0.8,
                            $"/conversations/thread/{cv.Id}")));
                    }
                }
                catch
                {
                    // Swallowed if conversations lookup fails
                }
            }

            var sorted = results.OrderByDescending(r => r.Score).ToList();
            var paged = sorted.Skip((p - 1) * ps).Take(ps).ToList();

            return Results.Ok(new SearchResultPageDto(paged, sorted.Count, p, ps));
        });

        return endpoints;
    }

    private static async Task<Guid> ResolveUserIdAsync(
        ClaimsPrincipal principal, IUserRepository users, CancellationToken ct)
    {
        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return Guid.Empty;
        }

        var user = await users.GetByClerkIdAsync(clerkId, ct);
        return user?.Id ?? Guid.Empty;
    }
}
