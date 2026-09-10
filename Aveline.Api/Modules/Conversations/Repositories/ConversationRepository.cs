using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Conversations.Repositories;

public class ConversationRepository : IConversationRepository
{
    private readonly AppDbContext _context;

    public ConversationRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Conversation?> GetAsync(Guid orgId, Guid id, CancellationToken cancellationToken = default)
        => await _context.Conversations
            .FirstOrDefaultAsync(c => c.OrganizationId == orgId && c.Id == id, cancellationToken);

    public async Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken cancellationToken = default)
        => await _context.Conversations
            .FirstOrDefaultAsync(c => c.ThreadId == threadId, cancellationToken);

    public async Task<(IReadOnlyList<Conversation> Items, int Total)> ListAsync(
        Guid orgId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Conversations
            .Where(c => c.OrganizationId == orgId)
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt);

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }

    public async Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(
        Guid orgId,
        Guid? customerId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.Conversations
            .FirstOrDefaultAsync(
                c => c.OrganizationId == orgId
                     && c.CustomerId == customerId
                     && c.Kind == ConversationKind.Salon,
                cancellationToken);

        if (existing is not null)
        {
            return (existing, false);
        }

        var created = new Conversation
        {
            OrganizationId = orgId,
            Kind = ConversationKind.Salon,
            CustomerId = customerId,
            ThreadId = threadId,
            Status = ConversationStatus.Active,
        };
        _context.Conversations.Add(created);
        await _context.SaveChangesAsync(cancellationToken);
        return (created, true);
    }

    public async Task<Conversation> GetOrCreateSalonByExternalRefAsync(
        Guid orgId,
        string externalRef,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        var existing = await _context.Conversations
            .FirstOrDefaultAsync(
                c => c.OrganizationId == orgId
                     && c.ExternalRef == externalRef
                     && c.Kind == ConversationKind.Salon,
                cancellationToken);

        if (existing is not null)
        {
            return existing;
        }

        var created = new Conversation
        {
            OrganizationId = orgId,
            Kind = ConversationKind.Salon,
            ExternalRef = externalRef,
            ThreadId = threadId,
            Status = ConversationStatus.Active,
        };
        _context.Conversations.Add(created);
        await _context.SaveChangesAsync(cancellationToken);
        return created;
    }

    public async Task SaveAsync(Conversation conversation, CancellationToken cancellationToken = default)
    {
        conversation.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync(cancellationToken);
    }
}
