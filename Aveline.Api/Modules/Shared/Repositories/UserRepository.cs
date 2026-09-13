using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Shared.Models;
using Microsoft.EntityFrameworkCore;

namespace Aveline.Api.Modules.Shared.Repositories;

public class UserRepository : IUserRepository
{
    private readonly AppDbContext _context;

    public UserRepository(AppDbContext context)
    {
        _context = context;
    }

    public async Task<User?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.ClerkId == clerkId, cancellationToken);
    }

    public async Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);
    }

    public async Task<User> CreateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.CreatedAt = DateTime.UtcNow;
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Add(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default)
    {
        user.UpdatedAt = DateTime.UtcNow;
        _context.Users.Update(user);
        await _context.SaveChangesAsync(cancellationToken);
        return user;
    }

    public async Task<bool> ExistsByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default)
    {
        return await _context.Users
            .AnyAsync(u => u.ClerkId == clerkId, cancellationToken);
    }

    public async Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(
        string? term,
        AccountState? state,
        Guid? organizationId,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default)
    {
        var query = _context.Users.AsNoTracking().AsQueryable();

        if (!string.IsNullOrWhiteSpace(term))
        {
            var trimmed = term.Trim();
            query = query.Where(u =>
                u.Email.Contains(trimmed)
                || u.FirstName.Contains(trimmed)
                || u.LastName.Contains(trimmed)
                || u.Username.Contains(trimmed)
                || u.ClerkId.Contains(trimmed));
        }

        if (state is not null)
        {
            query = query.Where(u => u.AccountState == state);
        }

        if (organizationId is not null)
        {
            // Membership is the canonical tenant link; User.OrganizationId is a legacy
            // Clerk string and must not be used as the filter key (H-2).
            query = query.Where(u =>
                _context.OrganizationMemberships.Any(m =>
                    m.UserId == u.Id && m.OrganizationId == organizationId));
        }

        var total = await query.CountAsync(cancellationToken);
        var items = await query
            .OrderByDescending(u => u.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return (items, total);
    }
}
