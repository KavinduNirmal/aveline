using Aveline.Api.Configurations;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace Aveline.Api.Endpoints;

/// <summary>
/// Internal (service-to-service) endpoints for the Customer Memory Agent (Slice 1).
/// All routes require the <c>X-Internal-Token</c> header (ADR-009, "InternalServicePolicy") and
/// are consumed by the Python agent service only — never exposed to boutique end-users. The
/// org is carried explicitly in each request/route for tenant scoping.
/// </summary>
public static class CustomerConciergeEndpoints
{
    public static IEndpointRouteBuilder MapCustomerConciergeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/internal/customers")
            .WithTags("Customer Concierge (Internal)")
            .RequireAuthorization(AuthorizationConfiguration.InternalServicePolicy);

        group.MapPost("/identify", IdentifyAsync)
            .WithName("IdentifyCustomer")
            .WithSummary("Look up a customer by phone, creating a 'new' profile when absent.")
            .Produces<CustomerProfileDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{customerId:guid}/profile", GetProfileAsync)
            .WithName("GetCustomerProfile")
            .WithSummary("Get a customer's full profile (preferences, tags, consent).")
            .Produces<CustomerProfileDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/lookup", LookupAsync)
            .WithName("LookupCustomers")
            .WithSummary("Read-only customer lookup by name and/or phone (no auto-create).")
            .Produces<CustomerLookupResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/book-summary", GetBookSummaryAsync)
            .WithName("GetCustomerBookSummary")
            .WithSummary(
                "How many clients this boutique has, and the few most recently active (ADR-026).")
            .Produces<CustomerBookSummaryDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPatch("/{customerId:guid}", UpdateCustomerAsync)
            .WithName("UpdateCustomerInternal")
            .WithSummary("Apply an explicit staff instruction to a customer's name or phone.")
            .Produces<TenantCustomerDetailDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{customerId:guid}/memories", SaveMemoryAsync)
            .WithName("SaveCustomerMemory")
            .WithSummary("Persist a semantic memory for a customer (embeds content).")
            .Produces<CustomerMemoryDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/memories/search", SearchMemoriesAsync)
            .WithName("SearchCustomerMemories")
            .WithSummary("Semantically search a customer's memories by free-text query.")
            .Produces<IReadOnlyList<MemorySearchResultDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{customerId:guid}/brief", GenerateBriefAsync)
            .WithName("GenerateCustomerBrief")
            .WithSummary("Generate a staff-facing interaction brief for a customer.")
            .Produces<InteractionBriefDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{customerId:guid}/interactions", RecordInteractionAsync)
            .WithName("RecordCustomerInteraction")
            .WithSummary("Record an inbound/outbound customer interaction.")
            .Produces<CustomerInteractionDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{customerId:guid}/consent", GetConsentAsync)
            .WithName("GetCustomerConsent")
            .WithSummary("Get a customer's consent status.")
            .Produces<CustomerConsentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{customerId:guid}/consent", UpdateConsentAsync)
            .WithName("UpdateCustomerConsent")
            .WithSummary("Set a customer's consent status (pending | granted | revoked).")
            .Produces<CustomerConsentDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{customerId:guid}/events", AddEventAsync)
            .WithName("AddCustomerEvent")
            .WithSummary("Add a customer event (wedding, birthday, ...).")
            .Produces<CustomerEventDto>(StatusCodes.Status201Created)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapGet("/{customerId:guid}/events", ListEventsAsync)
            .WithName("ListCustomerEvents")
            .WithSummary("List a customer's active events, upcoming first.")
            .Produces<IReadOnlyList<CustomerEventDto>>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status401Unauthorized);

        group.MapPost("/{customerId:guid}/status", RecomputeStatusAsync)
            .WithName("RecomputeCustomerStatus")
            .WithSummary("Recompute a customer's loyalty tier from spend/visits, or override it.")
            .Produces<CustomerStatusDto>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status404NotFound)
            .Produces(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> IdentifyAsync(
        IdentifyCustomerRequest request,
        ICustomerService customers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.PhoneNumber))
        {
            return Results.BadRequest(new { message = "PhoneNumber is required." });
        }

        var profile = await customers.IdentifyOrCreateAsync(
            request.OrganizationId, request.PhoneNumber, request.FullName, cancellationToken);
        return Results.Ok(profile);
    }

    /// <summary>
    /// The book at a glance, for Aveline (ADR-026). Read by the agent service only, and only when
    /// the request that reached it declared itself staff, so a customer message never causes this
    /// route to be called.
    /// </summary>
    /// <remarks>
    /// <paramref name="limit"/> bounds the named highlights rather than the book: this is a chat
    /// answer, not a listing surface, and the Customers screen already pages the whole book. The
    /// service clamps it, so an over-large value cannot turn one question into an enumeration.
    /// </remarks>
    private static async Task<IResult> GetBookSummaryAsync(
        [FromQuery] Guid organizationId,
        [FromQuery] int? limit,
        ICustomerTenantService customers,
        CancellationToken cancellationToken)
    {
        var summary = await customers.GetBookSummaryAsync(
            organizationId, limit ?? 5, activitySince: null, cancellationToken);
        return Results.Ok(summary);
    }

    private static async Task<IResult> GetProfileAsync(
        Guid customerId,
        [FromQuery] Guid organizationId,
        ICustomerService customers,
        CancellationToken cancellationToken)
    {
        var profile = await customers.GetProfileAsync(organizationId, customerId, cancellationToken);
        return profile is null
            ? Results.NotFound(new { message = "Customer not found." })
            : Results.Ok(profile);
    }

    private static async Task<IResult> LookupAsync(
        CustomerLookupRequest request,
        ICustomerService customers,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name) && string.IsNullOrWhiteSpace(request.PhoneNumber)
            && string.IsNullOrWhiteSpace(request.Email))
        {
            return Results.BadRequest(new { message = "Provide a name, phone number and/or email." });
        }

        var result = await customers.LookupAsync(request, cancellationToken);
        return Results.Ok(result);
    }

    /// <summary>
    /// Applies an explicit staff instruction to a customer's name or phone (ADR-023 follow-up).
    /// </summary>
    /// <remarks>
    /// Reuses <see cref="ICustomerTenantService.UpdateAsync"/> rather than re-implementing the
    /// write, so the agent inherits the same validation, phone normalization and duplicate-phone
    /// detection the staff API enforces. The agent is a reasoning engine and must not own business
    /// rules (SYSTEM_PROMPT "Tools only").
    ///
    /// A duplicate phone is a <c>409</c> rather than a silent overwrite: two customers sharing a
    /// number is a data-integrity problem the caller has to resolve, not one to swallow.
    /// </remarks>
    private static async Task<IResult> UpdateCustomerAsync(
        Guid customerId,
        [FromQuery] Guid organizationId,
        UpdateCustomerRequest request,
        ICustomerTenantService customers,
        CancellationToken cancellationToken)
    {
        if (organizationId == Guid.Empty)
        {
            return Results.BadRequest(new { message = "organizationId is required." });
        }

        if (request.FullName is null && request.PhoneNumber is null)
        {
            return Results.BadRequest(new { message = "Provide a name and/or phone number to update." });
        }

        try
        {
            var updated = await customers.UpdateAsync(organizationId, customerId, request, cancellationToken);

            return updated is null
                ? Results.NotFound(new { message = "That customer is not in this boutique." })
                : Results.Ok(updated);
        }
        catch (CustomerPhoneConflictException)
        {
            return Results.Conflict(new
            {
                code = "customer-phone-conflict",
                message = "Another customer in this boutique already has that phone number.",
            });
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> SaveMemoryAsync(
        Guid customerId,
        SaveMemoryRequest request,
        ICustomerMemoryService memories,
        CancellationToken cancellationToken)
    {
        var saved = await memories.SaveMemoryAsync(customerId, request, cancellationToken);
        return saved is null
            ? Results.BadRequest(new { message = "Consent revoked; memory not stored." })
            : Results.Created($"/internal/customers/{customerId}/memories/{saved.Id}", saved);
    }

    private static async Task<IResult> SearchMemoriesAsync(
        MemorySearchRequest request,
        ICustomerMemoryService memories,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Query))
        {
            return Results.BadRequest(new { message = "Query is required." });
        }

        var results = await memories.SearchAsync(request, cancellationToken);
        return Results.Ok(results);
    }

    private static async Task<IResult> GenerateBriefAsync(
        Guid customerId,
        [FromQuery] Guid organizationId,
        ICustomerMemoryService memories,
        CancellationToken cancellationToken)
    {
        var brief = await memories.GenerateBriefAsync(organizationId, customerId, cancellationToken);
        return brief is null
            ? Results.NotFound(new { message = "No brief available (customer not found, or consent revoked)." })
            : Results.Ok(brief);
    }

    private static async Task<IResult> RecordInteractionAsync(
        Guid customerId,
        RecordInteractionRequest request,
        ICustomerInteractionService interactions,
        CancellationToken cancellationToken)
    {
        var interaction = await interactions.RecordAsync(
            request.OrganizationId,
            customerId,
            request.Channel,
            request.Direction,
            request.MessageContent,
            request.ParsedIntentJson,
            request.StaffMemberId,
            cancellationToken);
        // §5.5: a revoked customer's interaction is not written. Mirrors the memory endpoint's
        // documented 400 rather than silently returning a row that does not exist.
        return interaction is null
            ? Results.BadRequest(new { message = "Consent revoked; interaction not recorded." })
            : Results.Created($"/internal/customers/{customerId}/interactions/{interaction.Id}", interaction);
    }

    private static async Task<IResult> GetConsentAsync(
        Guid customerId,
        [FromQuery] Guid organizationId,
        ICustomerConsentService consent,
        CancellationToken cancellationToken)
    {
        var dto = await consent.GetAsync(organizationId, customerId, cancellationToken);
        return Results.Ok(dto);
    }

    private static async Task<IResult> UpdateConsentAsync(
        Guid customerId,
        UpdateConsentRequest request,
        ICustomerConsentService consent,
        CancellationToken cancellationToken)
    {
        try
        {
            var dto = await consent.UpdateAsync(request.OrganizationId, customerId, request.ConsentStatus, cancellationToken);
            return Results.Ok(dto);
        }
        catch (InvalidConsentStatusException ex)
        {
            // D-3: a typed domain error, mapped deliberately to the documented 400 rather than
            // reaching the global handler as a 500.
            return Results.BadRequest(new { code = ex.Code, message = ex.Message });
        }
    }

    private static async Task<IResult> AddEventAsync(
        Guid customerId,
        AddEventRequest request,
        ICustomerEventService events,
        CancellationToken cancellationToken)
    {
        var customerEvent = await events.AddAsync(customerId, request, cancellationToken);
        // §5.5: a revoked customer's event is not written (see RecordInteractionAsync).
        return customerEvent is null
            ? Results.BadRequest(new { message = "Consent revoked; event not stored." })
            : Results.Created($"/internal/customers/{customerId}/events/{customerEvent.Id}", customerEvent);
    }

    private static async Task<IResult> ListEventsAsync(
        Guid customerId,
        [FromQuery] Guid organizationId,
        ICustomerEventService events,
        CancellationToken cancellationToken)
    {
        var list = await events.ListAsync(organizationId, customerId, cancellationToken);
        return Results.Ok(list);
    }

    private static async Task<IResult> RecomputeStatusAsync(
        Guid customerId,
        RecomputeStatusRequest request,
        ICustomerLoyaltyService loyalty,
        CancellationToken cancellationToken)
    {
        try
        {
            var status = await loyalty.RecomputeAsync(
                request.OrganizationId, customerId, request.Status, cancellationToken);
            return status is null
                ? Results.NotFound(new { message = "Customer not found." })
                : Results.Ok(status);
        }
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }
}
