using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Conversations.Repositories;

public class SignOffDecisionRepository : ISignOffDecisionRepository
{
    private readonly AppDbContext _context;

    public SignOffDecisionRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<SignOffDecision?> GetByMessageIdAsync(Guid messageId, CancellationToken cancellationToken = default)
        => await _context.SignOffDecisions
            .Where(d => d.MessageId == messageId)
            .OrderByDescending(d => d.DecidedAt)
            .FirstOrDefaultAsync(cancellationToken);

    public async Task SaveAsync(SignOffDecision decision, CancellationToken cancellationToken = default)
    {
        if (decision.Id == default)
        {
            decision.Id = Guid.CreateVersion7();
        }
        _context.SignOffDecisions.Add(decision);
        await _context.SaveChangesAsync(cancellationToken);
    }
}
