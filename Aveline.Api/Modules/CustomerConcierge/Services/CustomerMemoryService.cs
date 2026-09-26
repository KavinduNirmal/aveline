using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerMemoryService : ICustomerMemoryService
{
    private readonly ICustomerMemoryRepository _memories;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerEventRepository _events;
    private readonly IConsentGateService _consentGate;
    private readonly ICustomerTagRepository _tags;
    private readonly IEmbeddingService _embedding;

    public CustomerMemoryService(
        ICustomerMemoryRepository memories,
        ICustomerRepository customers,
        ICustomerEventRepository events,
        IConsentGateService consentGate,
        ICustomerTagRepository tags,
        IEmbeddingService embedding)
    {
        _memories = memories;
        _customers = customers;
        _events = events;
        _consentGate = consentGate;
        _tags = tags;
        _embedding = embedding;
    }

    public async Task<CustomerMemoryDto?> SaveMemoryAsync(
        Guid customerId,
        SaveMemoryRequest request,
        CancellationToken cancellationToken = default)
    {
        // Do not store memories for customers who have opted out, or whose consent could not be
        // read (the gate fails closed - plan §8.3).
        var decision = await _consentGate.CheckAsync(request.OrganizationId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
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
        // §5.5: a revoked customer's memories must not be read back, whichever agent asks. The
        // gate is checked before the embedding call so a blocked read costs nothing and never
        // reaches the store.
        var decision = await _consentGate.CheckAsync(
            request.OrganizationId, request.CustomerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return [];
        }

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
        // §5.5: brief generation reads the profile, tags and events, so it is personalization and
        // is refused outright for a revoked customer (or an unreadable consent store). The caller
        // gets null, exactly as it does for an unknown customer: no data is returned either way.
        var decision = await _consentGate.CheckAsync(orgId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return null;
        }

        var customer = await _customers.GetAsync(orgId, customerId, cancellationToken);
        if (customer is null)
        {
            return null;
        }

        var tags = await _tags.ListByCustomerAsync(orgId, customerId, cancellationToken);
        // D-2: the absent row has one value across every call site; the gate is now the one reader.
        var profile = CustomerProfileDto.From(customer, tags, decision.Status);
        var events = (await _events.ListByCustomerAsync(orgId, customerId, cancellationToken))
            .Select(CustomerEventDto.From)
            .ToList();

        return InteractionBriefDto.From(profile, events);
    }
}
