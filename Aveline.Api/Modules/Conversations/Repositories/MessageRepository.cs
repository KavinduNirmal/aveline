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

    public async Task<(IReadOnlyList<Message> Items, int Total)> ListAsync(
        Guid conversationId,
        int page,
        int pageSize,
        Guid? around = null,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Messages
            .Where(m => m.ConversationId == conversationId)
            .OrderBy(m => m.CreatedAt);

        var total = await query.CountAsync(cancellationToken);

        IQueryable<Message> pageQuery;
        if (around is not null)
        {
            // Deep-link: return a page that contains the target message. Compute the
            // target's index, then page around it so the caller can scroll to it.
            var target = await _context.Messages
                .Where(m => m.ConversationId == conversationId && m.Id == around)
                .Select(m => (int?)m.CreatedAt.Ticks)
                .FirstOrDefaultAsync(cancellationToken);

            if (target is null)
            {
                pageQuery = query.Take(pageSize);
            }
            else
            {
                var beforeCount = await _context.Messages
                    .CountAsync(m => m.ConversationId == conversationId && m.CreatedAt.Ticks < target.Value, cancellationToken);
                var start = Math.Max(0, beforeCount - pageSize / 2);
                pageQuery = query.Skip(start).Take(pageSize);
            }
        }
        else
        {
            pageQuery = query.Skip((page - 1) * pageSize).Take(pageSize);
        }

        var items = await pageQuery.ToListAsync(cancellationToken);
        return (items, total);
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
}
