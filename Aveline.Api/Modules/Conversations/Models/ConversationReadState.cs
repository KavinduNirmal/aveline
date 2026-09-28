using Aveline.Api.Common.MultiTenancy;

namespace Aveline.Api.Modules.Conversations.Models;

/// <summary>
/// How far one user has read one conversation.
/// </summary>
/// <remarks>
/// The first per-user conversation read model in the API (D5 = B). One row per
/// (organization, user, conversation); an absent row means <b>nothing has been read</b>, with
/// no coalesce to an epoch. The marker is monotonic: it is advanced by
/// <c>(CreatedAt, Id)</c> and never moves backwards, so a second device re-opening an older
/// position cannot un-read what the first device read.
///
/// The inbox still draws no badge. When it wants one it reads this table's aggregate
/// (<c>unread = messages after the marker</c>) rather than growing a second model.
/// </remarks>
public class ConversationReadState : ITenantEntity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public Guid OrganizationId { get; set; }

    /// <summary>The Aveline user whose reading this records.</summary>
    public Guid UserId { get; set; }

    public Guid ConversationId { get; set; }

    /// <summary>
    /// The newest message this user has seen. Null only before the first successful write,
    /// which is a state the row never persists: a row exists only once something is read.
    /// </summary>
    public Guid? LastReadMessageId { get; set; }

    public DateTime LastReadAtUtc { get; set; } = DateTime.UtcNow;
}
