namespace Aveline.Api.Infrastructure.Notifications;

/// <summary>Context for a staff invitation email.</summary>
/// <param name="ToEmail">Recipient address.</param>
/// <param name="InvitationLink">Full invite URL (contains the one-time code query parameter).</param>
/// <param name="BoutiqueRole">Canonical boutique role granted on acceptance.</param>
public sealed record StaffInvitationEmail(string ToEmail, string InvitationLink, string BoutiqueRole);

/// <summary>
/// Context for the optional summary notice an owner may request after creating invitations.
/// </summary>
/// <param name="ToEmail">The acting owner's address.</param>
/// <param name="OrganizationName">The boutique the codes belong to.</param>
/// <param name="Count">How many codes were minted.</param>
/// <param name="BoutiqueRole">The role every code grants.</param>
/// <param name="FirstExpiresAt">When the earliest of the codes expires.</param>
/// <remarks>
/// The notice deliberately carries **no codes**. An email is a durable, forwarded artefact; a
/// one-time staff code belongs in the panel's clipboard, not in a mailbox that could be shared, and
/// the recipient can read the codes from the Team section at any time.
/// </remarks>
public sealed record InvitationSummaryEmail(
    string ToEmail,
    string OrganizationName,
    int Count,
    string BoutiqueRole,
    DateTime FirstExpiresAt);

/// <summary>
/// Outbound email delivery abstraction. A real SMTP/provider sender is out of scope for
/// this milestone; the demo implementation records the dispatch without leaking the
/// one-time invitation code into logs.
/// </summary>
public interface IEmailService
{
    Task SendStaffInvitationAsync(StaffInvitationEmail email, CancellationToken cancellationToken = default);

    /// <summary>
    /// Dispatches the optional summary notice to the acting owner. The caller reports the outcome
    /// honestly; a missing address is never silently treated as a delivery.
    /// </summary>
    Task SendInvitationSummaryAsync(
        InvitationSummaryEmail email, CancellationToken cancellationToken = default);
}
