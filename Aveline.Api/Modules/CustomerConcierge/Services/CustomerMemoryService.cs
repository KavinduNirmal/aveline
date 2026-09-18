using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerMemoryService : ICustomerMemoryService
{
    private readonly ICustomerMemoryRepository _memories;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerEventRepository _events;
    private readonly ICustomerConsentRepository _consent;
    private readonly ICustomerTagRepository _tags;
    private readonly IEmbeddingService _embedding;

    public CustomerMemoryService(
        ICustomerMemoryRepository memories,
        ICustomerRepository customers,
        ICustomerEventRepository events,
        ICustomerConsentRepository consent,
        ICustomerTagRepository tags,
        IEmbeddingService embedding)
    {
        _memories = memories;
        _customers = customers;
        _events = events;
        _consent = consent;
        _tags = tags;
        _embedding = embedding;
    }

    public async Task<CustomerMemoryDto?> SaveMemoryAsync(
        Guid customerId,
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Do not store memories for customers who have opted out.
        var consent = await _consent.GetForCustomerAsync(request.OrganizationId, customerId, cancellationToken);
        if (consent?.ConsentStatus == "revoked")
        {
            return null;
        }

        var memory = await _memories.AddAsync(new CustomerMemory
        {
            OrganizationId = request.OrganizationId,
            CustomerId = customerId,
            Content = request.Content,
            Category = request.Category,
            Source = request.Source,
            IsExplicit = request.IsExplicit,
            Confidence = request.Confidence,
        }, cancellationToken);

        var embedding = request.Embedding
                        ?? await _embedding.GenerateAsync(request.Content, cancellationToken);
        await _memories.UpdateEmbeddingAsync(request.OrganizationId, memory.Id, embedding, cancellationToken);

        return CustomerMemoryDto.From(memory);
    }

    public async Task<IReadOnlyList<MemorySearchResultDto>> SearchAsync(
        MemorySearchRequest request,
        CancellationToken cancellationToken = default)
    {
        var queryEmbedding = await _embedding.GenerateAsync(request.Query, cancellationToken);
        var results = await _memories.SearchSemanticAsync(
            request.OrganizationId,
            request.CustomerId,
            queryEmbedding,
            request.TopK,
            cancellationToken);
        return results.Select(MemorySearchResultDto.From).ToList();
    }

    public async Task<InteractionBriefDto?> GenerateBriefAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        var customer = await _customers.GetAsync(orgId, customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var tags = await _tags.ListByCustomerAsync(orgId, customerId, cancellationToken);
        var consent = await _consent.GetForCustomerAsync(orgId, customerId, cancellationToken);
        var profile = CustomerProfileDto.From(customer, tags, consent?.ConsentStatus ?? "unknown");
        var events = (await _events.ListByCustomerAsync(orgId, customerId, cancellationToken))
            .Select(CustomerEventDto.From)
            .ToList();

        return InteractionBriefDto.From(profile, events);
    }
}
