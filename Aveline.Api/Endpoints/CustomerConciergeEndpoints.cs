using Aveline.Api.Configurations;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
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
            .WithSummary("Set a customer's consent status (granted | revoked).")
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
            ? Results.NotFound(new { message = "Customer not found." })
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
        return Results.Created($"/internal/customers/{customerId}/interactions/{interaction.Id}", interaction);
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
        catch (ArgumentException ex)
        {
            return Results.BadRequest(new { message = ex.Message });
        }
    }

    private static async Task<IResult> AddEventAsync(
        Guid customerId,
        AddEventRequest request,
        ICustomerEventService events,
        CancellationToken cancellationToken)
    {
        var customerEvent = await events.AddAsync(customerId, request, cancellationToken);
        return Results.Created($"/internal/customers/{customerId}/events/{customerEvent.Id}", customerEvent);
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
}
