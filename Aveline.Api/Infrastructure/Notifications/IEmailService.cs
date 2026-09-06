namespace Aveline.Api.Infrastructure.Notifications;

/// <summary>Context for a staff invitation email.</summary>
/// <param name="ToEmail">Recipient address.</param>
/// <param name="InvitationLink">Full invite URL (contains the one-time code query parameter).</param>
/// <param name="BoutiqueRole">Canonical boutique role granted on acceptance.</param>
public sealed record StaffInvitationEmail(string ToEmail, string InvitationLink, string BoutiqueRole);

/// <summary>
/// Outbound email delivery abstraction. A real SMTP/provider sender is out of scope for
/// this milestone; the demo implementation records the dispatch without leaking the
/// one-time invitation code into logs.
/// </summary>
public interface IEmailService
{
    Task SendStaffInvitationAsync(StaffInvitationEmail email, CancellationToken cancellationToken = default);
}
