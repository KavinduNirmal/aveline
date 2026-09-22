using Microsoft.Extensions.Logging;

namespace Aveline.Api.Infrastructure.Notifications;

/// <summary>
/// Demo/no-op email sender: logs that an invitation email would be dispatched. Delivery
/// provider integration is deferred. The <c>code</c> query parameter is never written to
/// logs — only the recipient, role, and origin/path of the link are recorded.
/// </summary>
public sealed class LoggingEmailService : IEmailService
{
    private readonly ILogger<LoggingEmailService> _logger;

    public LoggingEmailService(ILogger<LoggingEmailService> logger)
    {
        _logger = logger;
    }

    public Task SendStaffInvitationAsync(StaffInvitationEmail email, CancellationToken cancellationToken = default)
    {
        // Uri.GetLeftPart(Path) drops the query string, so the one-time code is not logged.
        var safeLink = Uri.TryCreate(email.InvitationLink, UriKind.Absolute, out var uri)
            ? uri.GetLeftPart(UriPartial.Path)
            : "invite";

        _logger.LogInformation(
            "Invitation email (demo delivery, provider not configured). To={ToEmail} Role={Role} Link={Link}",
            email.ToEmail, email.BoutiqueRole, safeLink);

        return Task.CompletedTask;
    }

    public Task SendInvitationSummaryAsync(
        InvitationSummaryEmail email, CancellationToken cancellationToken = default)
    {
        // No codes are logged, matching the rule the invitation sender above follows: a one-time
        // staff code is not an artefact that belongs in a log line.
        _logger.LogInformation(
            "Invitation summary email (demo delivery, provider not configured). To={ToEmail} "
            + "Organization={OrganizationName} Count={Count} Role={Role} FirstExpiresAt={FirstExpiresAt}",
            email.ToEmail, email.OrganizationName, email.Count, email.BoutiqueRole, email.FirstExpiresAt);

        return Task.CompletedTask;
    }
}
