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

    public async Task<Conversation?> GetVisibleToUserAsync(
        Guid orgId,
        Guid id,
        Guid userId,
        CancellationToken cancellationToken = default)
        => await _context.Conversations
            .FirstOrDefaultAsync(
                c => c.OrganizationId == orgId
                     && c.Id == id
                     && (c.OwnerUserId == null || c.OwnerUserId == userId),
                cancellationToken);

    public async Task<Conversation?> GetByThreadIdAsync(string threadId, CancellationToken cancellationToken = default)
        => await _context.Conversations
            .FirstOrDefaultAsync(c => c.ThreadId == threadId, cancellationToken);

    public async Task<(IReadOnlyList<ConversationListRow> Items, int Total)> ListAsync(
        Guid orgId,
        Guid userId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        // Organization-shared Salons (customer / channel threads) plus the caller's own general
        // Salon - never another user's (ADR-021).
        var query = _context.Conversations
            .Where(c => c.OrganizationId == orgId
                        && (c.OwnerUserId == null || c.OwnerUserId == userId));

        var total = await query.CountAsync(cancellationToken);

        // The id tiebreak makes the order total: without it, two conversations sharing an
        // effective timestamp can be ordered differently per query and a page boundary then
        // duplicates one row and skips another.
        var ordered = query
            .OrderByDescending(c => c.LastMessageAt ?? c.CreatedAt)
            .ThenByDescending(c => c.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        var items = await ProjectRows(ordered).ToListAsync(cancellationToken);

        return (items, total);
    }

    public Task<ConversationListRow?> GetRowAsync(
        Guid conversationId,
        CancellationToken cancellationToken = default)
        // The id is a globally unique key, so the realtime path can resolve the tile from the
        // conversation id alone. This is internal read model access, never a client-facing route.
        => ProjectRows(_context.Conversations.Where(c => c.Id == conversationId))
            .FirstOrDefaultAsync(cancellationToken);

    /// <summary>
    /// The list row projection: the conversation plus the client's name, the newest message and
    /// whether a SignOff is waiting. One expression so the list and the realtime tile cannot
    /// disagree about what a row carries.
    /// </summary>
    private IQueryable<ConversationListRow> ProjectRows(IQueryable<Conversation> query) =>
        query.Select(c => new ConversationListRow(
            c,
            _context.Customers
                .Where(customer => customer.Id == c.CustomerId)
                .Select(customer => customer.FullName)
                .FirstOrDefault(),
            _context.Messages
                .Where(message => message.ConversationId == c.Id)
                .OrderByDescending(message => message.CreatedAt)
                .ThenByDescending(message => message.Id)
                .FirstOrDefault(),
            _context.Messages.Any(message =>
                message.ConversationId == c.Id
                && message.Kind == MessageKind.SignOff
                && message.Status == MessageStatus.AwaitingSignOff)));

    public async Task<(Conversation Conversation, bool Created)> GetOrCreateSalonAsync(
        Guid orgId,
        Guid userId,
        Guid? customerId,
        string threadId,
        CancellationToken cancellationToken = default)
    {
        // The general Salon belongs to one user; a customer-bound Salon is organization-shared, so
        // it is matched without the user and keeps OwnerUserId null (ADR-021).
        var existing = customerId is null
            ? await _context.Conversations.FirstOrDefaultAsync(
                c => c.OrganizationId == orgId
                     && c.OwnerUserId == userId
                     && c.CustomerId == null
                     && c.Kind == ConversationKind.Salon,
                cancellationToken)
            : await _context.Conversations.FirstOrDefaultAsync(
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
            OwnerUserId = customerId is null ? userId : null,
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
        Guid? customerId,
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
            // A thread created before the phone was on file is upgraded in place the first time
            // the customer can be resolved, rather than staying context-less forever.
            if (customerId is not null && existing.CustomerId is null)
            {
                existing.CustomerId = customerId;
                await _context.SaveChangesAsync(cancellationToken);
            }

            return existing;
        }

        var created = new Conversation
        {
            OrganizationId = orgId,
            Kind = ConversationKind.Salon,
            CustomerId = customerId,
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
