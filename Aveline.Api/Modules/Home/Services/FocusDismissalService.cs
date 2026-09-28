using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Home.DTOs;
using Aveline.Api.Modules.Home.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Home.Services;

/// <summary>
/// Records the human decision behind a sign-off. The feed is derived; this is
/// the only part of Home's focus deck that is persisted.
/// </summary>
public interface IFocusDismissalService
{
    /// <summary>
    /// Records <paramref name="item"/>'s dismissal for <paramref name="userId"/>.
    ///
    /// Idempotent by nature: dismissing the same fact twice is the same end state,
    /// so the write is an upsert. The stored content hash is the server's, not the
    /// client's.
    /// </summary>
    Task<FocusDismissal> RecordAsync(
        Guid organizationId,
        Guid userId,
        HomeFeedItemDto item,
        string decision,
        string? note,
        CancellationToken cancellationToken = default);
}

public sealed class FocusDismissalService : IFocusDismissalService
{
    private static readonly HashSet<string> AllowedDecisions = new(StringComparer.Ordinal)
    {
        "signOff", "approve", "reject", "acknowledge", "markReady",
    };

    private readonly AppDbContext _context;

    public FocusDismissalService(AppDbContext context) => _context = context;

    public async Task<FocusDismissal> RecordAsync(
        Guid organizationId,
        Guid userId,
        HomeFeedItemDto item,
        string decision,
        string? note,
        CancellationToken cancellationToken = default)
    {
        var normalized = AllowedDecisions.Contains(decision) ? decision : "signOff";

        var existing = await _context.FocusDismissals.FirstOrDefaultAsync(
            dismissal => dismissal.OrganizationId == organizationId
                         && dismissal.UserId == userId
                         && dismissal.Domain == item.Domain
                         && dismissal.SourceKey == item.SourceKey,
            cancellationToken);

        if (existing is null)
        {
            existing = new FocusDismissal
            {
                OrganizationId = organizationId,
                UserId = userId,
                Domain = item.Domain,
                SourceKey = item.SourceKey,
                Decision = normalized,
                ContentHash = item.ContentHash,
                Note = note,
                DismissedAtUtc = DateTime.UtcNow,
            };
            _context.FocusDismissals.Add(existing);
        }
        else
        {
            // A re-decision refreshes the content binding: the human has now
            // approved this version of the fact.
            existing.Decision = normalized;
            existing.ContentHash = item.ContentHash;
            existing.Note = note;
            existing.DismissedAtUtc = DateTime.UtcNow;
        }

        await _context.SaveChangesAsync(cancellationToken);
        return existing;
    }
}
