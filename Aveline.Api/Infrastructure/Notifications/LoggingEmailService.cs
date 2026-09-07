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
}
