using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Conversations.Repositories;

/// <summary>
/// One user's read markers, keyed by (organization, user, conversation).
/// </summary>
public interface IConversationReadStateRepository
{
    /// <summary>
    /// The caller's marker for one conversation, or <c>null</c> when nothing has been read.
    /// </summary>
    Task<ConversationReadState?> GetAsync(
        Guid organizationId,
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates one marker row (the unique index makes re-reading an upsert).</summary>
    Task SaveAsync(ConversationReadState state, CancellationToken cancellationToken = default);
}

public class ConversationReadStateRepository : IConversationReadStateRepository
{
    private readonly AppDbContext _context;

    public ConversationReadStateRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<ConversationReadState?> GetAsync(
        Guid organizationId,
        Guid userId,
        Guid conversationId,
        CancellationToken cancellationToken = default)
        => await _context.ConversationReadStates.FirstOrDefaultAsync(
            s => s.OrganizationId == organizationId
                 && s.UserId == userId
                 && s.ConversationId == conversationId,
            cancellationToken);

    public async Task SaveAsync(ConversationReadState state, CancellationToken cancellationToken = default)
    {
        // Upsert by the natural key rather than by id: a caller that builds a fresh row for a
        // key that already has one must update it, not collide with the unique index.
        var existing = await _context.ConversationReadStates.FirstOrDefaultAsync(
            s => s.OrganizationId == state.OrganizationId
                 && s.UserId == state.UserId
                 && s.ConversationId == state.ConversationId,
            cancellationToken);

        if (existing is null)
        {
            if (state.Id == default)
            {
                state.Id = Guid.CreateVersion7();
            }
            _context.ConversationReadStates.Add(state);
        }
        else
        {
            existing.LastReadMessageId = state.LastReadMessageId;
            existing.LastReadAtUtc = state.LastReadAtUtc;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }
}
