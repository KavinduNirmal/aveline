using Aveline.Api.Modules.Home.DTOs;

namespace Aveline.Api.Modules.Home.Services;

/// <summary>
/// Derives Home's focus feed. There is no task table: every docket points at a
/// row that already exists (low stock, customer events, paused agent runs), and
/// the caller's dismissals are the only persisted part.
/// </summary>
public interface IFocusFeedService
{
    /// <summary>
    /// Builds the caller's feed for the organization-local day.
    /// </summary>
    /// <param name="canReadAgentApprovals">
    /// Whether the caller's role holds <c>stats:view:agent</c>. The commerce domain
    /// is availability-dependent: a supervisor legitimately sees fewer dockets than
    /// an owner, and the <c>dataQuality</c> block says why.
    /// </param>
    Task<HomeFeedDto> GetFeedAsync(
        Guid organizationId,
        Guid userId,
        bool canReadAgentApprovals,
        CancellationToken cancellationToken = default);
}
