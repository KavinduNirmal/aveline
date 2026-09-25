using System.Security.Claims;
using Aveline.Api.Authorization;
using Aveline.Api.Configurations;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Aveline.Api.Modules.Organizations.Models;
using Aveline.Api.Modules.Organizations.Repositories;
using Aveline.Api.Modules.Shared.DTOs;
using Aveline.Api.Modules.Shared.Repositories;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Aveline.Api.Modules.VisualIntelligence.Repositories;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Tenant-scoped cross-entity search endpoint aggregating catalog inventory items,
/// customer profiles, and conversation threads.
/// </summary>
public static class SearchEndpoints
{
    public static IEndpointRouteBuilder MapSearchEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/orgs/{organizationId:guid}/search")
            .WithTags("Search")
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueMemberPolicy);

        group.MapGet(string.Empty, SearchAsync)
            .WithName("GlobalSearch")
            .WithSummary("Search across catalog pieces, customer book, and conversation threads.")
            .Produces<GlobalSearchResponseDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized)
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }

    private static async Task<IResult> SearchAsync(
        Guid organizationId,
        [FromQuery] string? q,
        [FromQuery] string? scope,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        ClaimsPrincipal principal,
        IUserRepository users,
        IOrganizationRepository orgs,
        IInventoryRepository inventory,
        ICustomerRepository customers,
        AppDbContext dbContext,
        CancellationToken ct)
    {
        var query = q?.Trim();
        if (string.IsNullOrWhiteSpace(query) || query.Length < 2)
        {
            return Results.BadRequest(new
            {
                message = "Search query must be at least 2 characters.",
            });
        }

        var clerkId = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                      ?? principal.FindFirstValue("sub");
        if (string.IsNullOrEmpty(clerkId))
        {
            return Results.Unauthorized();
        }

        var user = await users.GetByClerkIdAsync(clerkId, ct);
        if (user is null)
        {
            return Results.Unauthorized();
        }

        var membership = await orgs.GetMembershipAsync(organizationId, user.Id, ct);
        if (membership is null || membership.Status != MembershipStatus.Active)
        {
            return Results.Forbid();
        }

        var role = membership.BoutiqueRole;
        var canViewCatalog = Permissions.IsGranted(role, Permissions.CatalogView);
        var canViewCustomers = Permissions.IsGranted(role, Permissions.CustomersView);
        var canViewConversations = Permissions.IsGranted(role, Permissions.ConversationsView);

        var normScope = (scope ?? "all").Trim().ToLowerInvariant();
        var includeCatalog = canViewCatalog && (normScope is "all" or "catalog");
        var includeCustomers = canViewCustomers && (normScope is "all" or "customers");
        var includeConversations = canViewConversations && (normScope is "all" or "conversations");

        var pageNum = Math.Max(1, page ?? 1);
        var pageSizeNum = Math.Clamp(pageSize ?? 20, 1, 100);

        var allHits = new List<GlobalSearchResultItemDto>();
        var lowerQuery = query.ToLowerInvariant();

        // 1. Search Catalog Pieces
        if (includeCatalog)
        {
            var (items, _) = await inventory.QueryAsync(
                organizationId,
                new CatalogQueryRequest
                {
                    Search = query,
                    Page = 1,
                    PageSize = 50,
                },
                ct);

            foreach (var item in items)
            {
                var isExactSku = !string.IsNullOrWhiteSpace(item.Sku) &&
                                 string.Equals(item.Sku.Trim(), query, StringComparison.OrdinalIgnoreCase);
                var isNameMatch = item.ItemName.ToLowerInvariant().Contains(lowerQuery);
                var score = isExactSku ? 1.0 : (isNameMatch ? 0.9 : 0.75);

                var subtitleParts = new[]
                {
                    item.Sku,
                    item.Category,
                    item.Price > 0 ? $"Rs {item.Price:N0}" : null,
                }.Where(s => !string.IsNullOrWhiteSpace(s));

                allHits.Add(new GlobalSearchResultItemDto(
                    SearchEntityType.CatalogItem,
                    item.Id,
                    item.ItemName,
                    string.Join(" · ", subtitleParts),
                    item.ImageUrl,
                    $"/catalog/{item.Id}",
                    score));
            }
        }

        // 2. Search Customers
        if (includeCustomers)
        {
            var matchedCustomers = await dbContext.Customers
                .Where(c => c.OrganizationId == organizationId
                            && c.DeletedAt == null
                            && ((c.FullName != null && c.FullName.ToLower().Contains(lowerQuery))
                                || (c.PhoneNumber != null && c.PhoneNumber.Contains(query))
                                || (c.Email != null && c.Email.ToLower().Contains(lowerQuery))))
                .OrderBy(c => c.FullName)
                .Take(50)
                .ToListAsync(ct);

            foreach (var cust in matchedCustomers)
            {
                var isPhoneExact = !string.IsNullOrWhiteSpace(cust.PhoneNumber) &&
                                   cust.PhoneNumber.Contains(query, StringComparison.OrdinalIgnoreCase);
                var isNameMatch = !string.IsNullOrWhiteSpace(cust.FullName) &&
                                  cust.FullName.ToLowerInvariant().Contains(lowerQuery);
                var score = isPhoneExact ? 1.0 : (isNameMatch ? 0.9 : 0.8);

                var subtitleParts = new[]
                {
                    cust.PhoneNumber,
                    cust.Status,
                }.Where(s => !string.IsNullOrWhiteSpace(s));

                allHits.Add(new GlobalSearchResultItemDto(
                    SearchEntityType.Customer,
                    cust.Id,
                    cust.FullName ?? cust.PhoneNumber,
                    string.Join(" · ", subtitleParts),
                    null,
                    $"/customers/{cust.Id}",
                    score));
            }
        }

        // 3. Search Conversations
        if (includeConversations)
        {
            var matchingConvs = await (
                from c in dbContext.Conversations
                where c.OrganizationId == organizationId
                      && (c.OwnerUserId == null || c.OwnerUserId == user.Id)
                join cust in dbContext.Customers on c.CustomerId equals cust.Id into custGroup
                from cust in custGroup.DefaultIfEmpty()
                join msg in dbContext.Messages on c.Id equals msg.ConversationId into msgGroup
                from msg in msgGroup.OrderByDescending(m => m.CreatedAt).Take(1).DefaultIfEmpty()
                where (c.ExternalRef != null && c.ExternalRef.ToLower().Contains(lowerQuery))
                      || (cust != null && cust.FullName != null && cust.FullName.ToLower().Contains(lowerQuery))
                      || (msg != null && msg.ContentBlocksJson != null && msg.ContentBlocksJson.ToLower().Contains(lowerQuery))
                select new
                {
                    c.Id,
                    Title = cust != null && cust.FullName != null ? cust.FullName : (c.ExternalRef ?? "The Salon"),
                    LastMessage = msg != null ? msg.ContentBlocksJson : null,
                    c.LastMessageAt,
                    c.CreatedAt,
                }
            ).Take(50).ToListAsync(ct);

            foreach (var conv in matchingConvs)
            {
                allHits.Add(new GlobalSearchResultItemDto(
                    SearchEntityType.Conversation,
                    conv.Id,
                    conv.Title,
                    "Conversation thread",
                    null,
                    $"/conversations/thread/{conv.Id}",
                    0.75));
            }
        }

        // Rank and Paginate
        var total = allHits.Count;
        var pagedHits = allHits
            .OrderByDescending(h => h.Score)
            .ThenBy(h => h.Title)
            .ThenBy(h => h.Id)
            .Skip((pageNum - 1) * pageSizeNum)
            .Take(pageSizeNum)
            .ToList();

        return Results.Ok(new GlobalSearchResponseDto(pagedHits, total, pageNum, pageSizeNum));
    }
}
