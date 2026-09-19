using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Conversations.Repositories;

/// <summary>Queries over the attachment rows. The bytes themselves live behind the store.</summary>
public interface IMessageAttachmentRepository
{
    Task<MessageAttachment?> GetAsync(
        Guid organizationId,
        Guid conversationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The rows a send may bind: all of <paramref name="attachmentIds"/> must belong to this
    /// conversation and still be unbound. Returns fewer rows than asked for when any id fails,
    /// so the caller can refuse the whole send rather than half-bind it.
    /// </summary>
    Task<IReadOnlyList<MessageAttachment>> GetBindableAsync(
        Guid organizationId,
        Guid conversationId,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default);

    Task SaveAsync(MessageAttachment attachment, CancellationToken cancellationToken = default);

    /// <summary>Rows never bound to a message before <paramref name="cutoff"/>.</summary>
    Task<IReadOnlyList<MessageAttachment>> ListOrphansAsync(
        DateTime cutoff,
        CancellationToken cancellationToken = default);

    Task<int> DeleteOrphansAsync(DateTime cutoff, CancellationToken cancellationToken = default);
}

public class MessageAttachmentRepository : IMessageAttachmentRepository
{
    private readonly AppDbContext _context;

    public MessageAttachmentRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<MessageAttachment?> GetAsync(
        Guid organizationId,
        Guid conversationId,
        Guid attachmentId,
        CancellationToken cancellationToken = default)
        => await _context.MessageAttachments.FirstOrDefaultAsync(
            a => a.OrganizationId == organizationId
                 && a.ConversationId == conversationId
                 && a.Id == attachmentId,
            cancellationToken);

    public async Task<IReadOnlyList<MessageAttachment>> GetBindableAsync(
        Guid organizationId,
        Guid conversationId,
        IReadOnlyCollection<Guid> attachmentIds,
        CancellationToken cancellationToken = default)
        => await _context.MessageAttachments
            .Where(a => a.OrganizationId == organizationId
                        && a.ConversationId == conversationId
                        && a.MessageId == null
                        && attachmentIds.Contains(a.Id))
            .ToListAsync(cancellationToken);

    public async Task SaveAsync(MessageAttachment attachment, CancellationToken cancellationToken = default)
    {
        _context.MessageAttachments.Update(attachment);
        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MessageAttachment>> ListOrphansAsync(
        DateTime cutoff,
        CancellationToken cancellationToken = default)
        => await _context.MessageAttachments
            .Where(a => a.MessageId == null && a.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

    public async Task<int> DeleteOrphansAsync(DateTime cutoff, CancellationToken cancellationToken = default)
    {
        var orphans = await _context.MessageAttachments
            .Where(a => a.MessageId == null && a.CreatedAtUtc < cutoff)
            .ToListAsync(cancellationToken);

        if (orphans.Count == 0)
        {
            return 0;
        }

        _context.MessageAttachments.RemoveRange(orphans);
        await _context.SaveChangesAsync(cancellationToken);
        return orphans.Count;
    }
}
