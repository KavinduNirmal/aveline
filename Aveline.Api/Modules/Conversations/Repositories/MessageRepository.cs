using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Conversations.Repositories;

public class MessageRepository : IMessageRepository
{
    private readonly AppDbContext _context;

    public MessageRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<Message?> GetAsync(Guid conversationId, Guid messageId, CancellationToken cancellationToken = default)
        => await _context.Messages
            .FirstOrDefaultAsync(m => m.ConversationId == conversationId && m.Id == messageId, cancellationToken);

    public async Task<Message?> GetByClientMessageIdAsync(
        Guid conversationId,
        Guid clientMessageId,
        CancellationToken cancellationToken = default)
        => await _context.Messages
            .FirstOrDefaultAsync(
                m => m.ConversationId == conversationId && m.ClientMessageId == clientMessageId,
                cancellationToken);

    public async Task<(IReadOnlyList<Message> Items, int Total, int Page)> ListAsync(
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Messages
            .Where(m => m.ConversationId == conversationId)
            // The id breaks a tie on CreatedAt. Guid.CreateVersion7 is monotone, so the pair is
            // a total order and a page seam cannot duplicate or skip a row.
            .OrderBy(m => m.CreatedAt)
            .ThenBy(m => m.Id);

        var total = await query.CountAsync(cancellationToken);

        // Deep-link: the anchor decides which page is served, and the page **that page** is
        // echoed. The window is therefore page-aligned, which is what lets a client compute
        // `hasEarlier` (page > 1) and `hasMore` (page * pageSize < total) from the response
        // alone; an off-grid half-page window could not be expressed in this envelope.
        var effectivePage = Math.Max(page, 1);
        if (around is not null)
        {
            var target = await _context.Messages
                .Where(m => m.ConversationId == conversationId && m.Id == around)
                .Select(m => new { m.CreatedAt, m.Id })
                .FirstOrDefaultAsync(cancellationToken);

            if (target is not null)
            {
                // Rows strictly before the anchor under the same total order, so a tied
                // CreatedAt cannot move the anchor to the wrong page.
                var beforeCount = await _context.Messages.CountAsync(
                    m => m.ConversationId == conversationId
                         && (m.CreatedAt < target.CreatedAt
                             || (m.CreatedAt == target.CreatedAt && m.Id.CompareTo(target.Id) < 0)),
                    cancellationToken);
                effectivePage = (beforeCount / pageSize) + 1;
            }
        }

        var start = (effectivePage - 1) * pageSize;
        var items = await query.Skip(start).Take(pageSize).ToListAsync(cancellationToken);
        return (items, total, effectivePage);
    }

    public async Task SaveAsync(Message message, CancellationToken cancellationToken = default)
    {
        if (message.Id == default)
        {
            message.Id = Guid.CreateVersion7();
        }
        message.CreatedAt = DateTime.UtcNow;
        _context.Messages.Add(message);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateAsync(Message message, CancellationToken cancellationToken = default)
    {
        _context.Messages.Update(message);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
