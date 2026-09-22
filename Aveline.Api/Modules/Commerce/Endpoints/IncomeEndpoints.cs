using Aveline.Api.Configurations;
using Aveline.Api.Modules.Commerce.DTOs;
using Aveline.Api.Modules.Commerce.Services;

namespace Aveline.Api.Modules.Commerce.Endpoints;

/// <summary>
/// The tenant income surface: the register (E-4) and the per-kind breakdown (E-5).
/// </summary>
/// <remarks>
/// Both routes are org-scoped by <c>{organizationId:guid}</c> under
/// <see cref="AuthorizationConfiguration.BoutiqueReportsViewPolicy"/> (<c>reports:view</c>), which
/// is the permission the grant map already gave manager, supervisor and owner before any route
/// enforced it. The **reduced** takings read that every role may call is a separate route (E-13) with
/// its own member-level policy — this group deliberately exposes the ledger rows and the margin
/// split, which are the owner's view.
///
/// The response is labelled **Income** on the wire because that is the owner-facing word for "what
/// we took". The entity underneath is a `BoutiqueSaleEntry`, named so that nobody merges it with
/// Aveline's own revenue journal.
/// </remarks>
public static class IncomeEndpoints
{
    public const string Tag = "Income";

    public static IEndpointRouteBuilder MapIncomeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/orgs/{organizationId:guid}/income")
            .WithTags(Tag)
            .RequireAuthorization(AuthorizationConfiguration.BoutiqueReportsViewPolicy);

        group.MapGet("/ledger", async (
            Guid organizationId,
            DateTime? from,
            DateTime? to,
            string? kind,
            string? basis,
            string? q,
            int? page,
            int? pageSize,
            IBoutiqueIncomeReadService reads,
            CancellationToken ct) =>
        {
            var result = await reads.GetLedgerAsync(
                organizationId,
                new BoutiqueIncomeLedgerQuery(
                    From: from,
                    To: to,
                    Kind: kind,
                    Basis: basis,
                    Query: q,
                    Page: page ?? 1,
                    PageSize: pageSize ?? BoutiqueIncomeReadService.DefaultPageSize),
                ct);

            // The register is uncached and carries the reconciliation banner, so it is never stale
            // in a browser or proxy.
            return result.InvalidReason is { } reason
                ? Results.BadRequest(new { message = reason })
                : Results.Ok(result);
        })
        .WithName("getBoutiqueIncomeLedger")
        .WithSummary("Read the boutique's income register with its window totals")
        .WithDescription(
            "S-60. Every entry in the window, with the window's totals and the two-basis "
            + "reconciliation attached. `derivedTotal` is billed value with no evidence of "
            + "collection and `verifiedTotal` is money taken; they are reported separately and never "
            + "summed into one unlabelled figure. `unverifiedGap` is the distance between them and "
            + "is the surface's most important number, not an error. Requires `reports:view`.")
        .Produces<BoutiqueIncomeLedgerPageDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        group.MapGet("/accounts", async (
            Guid organizationId,
            DateTime? from,
            DateTime? to,
            IBoutiqueIncomeReadService reads,
            CancellationToken ct) =>
        {
            var result = await reads.GetAccountsAsync(
                organizationId, new BoutiqueIncomeAccountsQuery(From: from, To: to), ct);

            return result.InvalidReason is { } reason
                ? Results.BadRequest(new { message = reason })
                : Results.Ok(result);
        })
        .WithName("getBoutiqueIncomeAccounts")
        .WithSummary("Read the boutique's takings broken down by kind")
        .WithDescription(
            "S-61. Every kind reported in its own total — a refund is never netted into a sale "
            + "figure without a label — plus a payment-method split read from the payment rows "
            + "rather than inferred. Requires `reports:view`.")
        .Produces<BoutiqueIncomeAccountsDto>(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status400BadRequest)
        .Produces(StatusCodes.Status401Unauthorized)
        .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
