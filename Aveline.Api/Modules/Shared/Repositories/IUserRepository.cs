using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Shared.Repositories;

public interface IUserRepository
{
    Task<User?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);
    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task<bool> ExistsByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Cross-organization paginated user search for Aveline support staff (FR-3.7).
    /// </summary>
    Task<(IReadOnlyList<User> Items, int Total)> SearchAsync(
        string? term,
        AccountState? state,
        int page,
        int pageSize,
        CancellationToken cancellationToken = default);
}
