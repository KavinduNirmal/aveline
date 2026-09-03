using Aveline.Api.Modules.Shared.Models;

namespace Aveline.Api.Modules.Shared.Repositories;

public interface IUserRepository
{
    Task<User?> GetByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default);
    Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task<User> CreateAsync(User user, CancellationToken cancellationToken = default);
    Task<User> UpdateAsync(User user, CancellationToken cancellationToken = default);
    Task<bool> ExistsByClerkIdAsync(string clerkId, CancellationToken cancellationToken = default);
}
