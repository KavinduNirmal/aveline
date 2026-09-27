using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.CustomerConcierge.DTOs;
using Aveline.Api.Modules.CustomerConcierge.Models;
using Aveline.Api.Modules.CustomerConcierge.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.CustomerConcierge.Services;

public class CustomerMemoryService : ICustomerMemoryService
{
    private readonly AppDbContext _context;
    private readonly ICustomerMemoryRepository _memories;
    private readonly ICustomerRepository _customers;
    private readonly ICustomerEventRepository _events;
    private readonly IConsentGateService _consentGate;
    private readonly ICustomerTagRepository _tags;
    private readonly IEmbeddingService _embedding;

    public CustomerMemoryService(
        AppDbContext context,
        ICustomerMemoryRepository memories,
        ICustomerRepository customers,
        ICustomerEventRepository events,
        IConsentGateService consentGate,
        ICustomerTagRepository tags,
        IEmbeddingService embedding)
    {
        _context = context;
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

        // A restatement of a note the customer already has is that note, not a second one. The
        // store had no defence here, so one fact could sit in it several times and the read path
        // was left to hide the repetition (gap A3). Answering with the existing row also means the
        // duplicate is never embedded, so the collapse costs no provider call.
        var contentKey = MemoryContentKey.From(request.Content);
        var existing = await _memories.GetByContentKeyAsync(
            request.OrganizationId, customerId, contentKey, cancellationToken);
        if (existing is not null)
        {
            return CustomerMemoryDto.From(existing);
        }

        var memory = await _memories.AddAsync(new CustomerMemory
        {
            OrganizationId = request.OrganizationId,
            CustomerId = customerId,
            Content = request.Content,
            ContentKey = contentKey,
            Category = request.Category,
            Source = request.Source,
            IsExplicit = request.IsExplicit,
            Confidence = request.Confidence,
            MetadataJson = string.IsNullOrWhiteSpace(request.MetadataJson) ? "{}" : request.MetadataJson,
            ExpiresAt = request.ExpiresAt,
        }, cancellationToken);

        var embedding = request.Embedding
                        ?? await _embedding.GenerateAsync(request.Content, cancellationToken);
        await _memories.UpdateEmbeddingAsync(request.OrganizationId, memory.Id, embedding, cancellationToken);

        return CustomerMemoryDto.From(memory);
    }

    public async Task<CustomerPreferenceDto?> SavePreferenceAsync(
        Guid customerId,
        SavePreferenceRequest request,
        CancellationToken cancellationToken = default)
    {
        var decision = await _consentGate.CheckAsync(request.OrganizationId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return null;
        }

        var key = request.PreferenceKey.Trim();
        var value = request.PreferenceValue.Trim();
        if (key.Length == 0 || value.Length == 0)
        {
            return null;
        }

        // Upsert on the value within the key, so "prefers silk" stated twice is one preference and
        // "prefers silk" then "prefers linen" is two. A restatement is not a second preference, but
        // it is new evidence, so the confidence and timestamp move.
        var existing = await _context.CustomerPreferences
            .FirstOrDefaultAsync(
                preference => preference.OrganizationId == request.OrganizationId
                              && preference.CustomerId == customerId
                              && preference.PreferenceKey == key
                              && preference.PreferenceValue == value,
                cancellationToken);

        if (existing is not null)
        {
            existing.IsExplicit = request.IsExplicit;
            existing.Confidence = request.Confidence;
            existing.Source = request.Source;
            existing.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync(cancellationToken);
            return CustomerPreferenceDto.From(existing);
        }

        var preference = new CustomerPreference
        {
            OrganizationId = request.OrganizationId,
            CustomerId = customerId,
            PreferenceKey = key,
            PreferenceValue = value,
            Source = request.Source,
            IsExplicit = request.IsExplicit,
            Confidence = request.Confidence,
        };
        _context.CustomerPreferences.Add(preference);
        await _context.SaveChangesAsync(cancellationToken);
        return CustomerPreferenceDto.From(preference);
    }

    public async Task<CustomerMemoryDto?> CorrectMemoryAsync(
        Guid orgId,
        Guid customerId,
        Guid memoryId,
        CorrectMemoryRequest request,
        CancellationToken cancellationToken = default)
    {
        var memory = await _memories.GetAsync(orgId, memoryId, cancellationToken);
        if (memory is null || memory.CustomerId != customerId)
        {
            return null;
        }

        var decision = await _consentGate.CheckAsync(orgId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return null;
        }

        var contentKey = MemoryContentKey.From(request.Content);
        // A correction that lands on another live note must not silently drop a row: the two notes
        // would become one and the caller would never know which statement survived.
        var collision = await _memories.GetByContentKeyAsync(orgId, customerId, contentKey, cancellationToken);
        if (collision is not null && collision.Id != memory.Id)
        {
            return null;
        }

        memory.Content = request.Content;
        memory.ContentKey = contentKey;
        memory.UpdatedAt = DateTime.UtcNow;
        await _memories.SaveAsync(memory, cancellationToken);

        // The embedding describes the old words; leaving it would make the note surface for the
        // query it no longer answers and hide it from the one it does.
        var embedding = await _embedding.GenerateAsync(request.Content, cancellationToken);
        await _memories.UpdateEmbeddingAsync(orgId, memory.Id, embedding, cancellationToken);

        return CustomerMemoryDto.From(memory);
    }

    public async Task<bool> RemoveMemoryAsync(
        Guid orgId,
        Guid customerId,
        Guid memoryId,
        CancellationToken cancellationToken = default)
    {
        var memory = await _memories.GetAsync(orgId, memoryId, cancellationToken);
        if (memory is null || memory.CustomerId != customerId)
        {
            return false;
        }

        var decision = await _consentGate.CheckAsync(orgId, customerId, cancellationToken);
        if (!decision.ShouldProcess)
        {
            return false;
        }

        // The soft-delete column was declared and read through a query filter but never assigned
        // (gap A6). It is the tombstone for a withdrawn note: the read paths already respect it, and
        // the row survives so the GDPR erase remains the only thing that destroys the text.
        memory.DeletedAt = DateTime.UtcNow;
        memory.UpdatedAt = memory.DeletedAt.Value;
        await _memories.SaveAsync(memory, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<CustomerMemoryDto>> ListMemoriesAsync(
        Guid orgId,
        Guid customerId,
        CancellationToken cancellationToken = default)
    {
        // Deliberately ungated. The list is what the customer's own memory panel shows, and the
        // staff route that backs it has never been consent-gated; adding a gate here would empty the
        // panel for a revoked customer whose notes are already on screen. The gate belongs on the
        // paths that read or write personal data the caller did not already have (search, brief,
        // save, correct, remove) - see `IConsentGateService`.
        var memories = await _memories.ListByCustomerAsync(orgId, customerId, cancellationToken);
        return memories.Select(CustomerMemoryDto.From).ToList();
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
            request.MinSimilarity,
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
