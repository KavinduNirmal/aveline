using Aveline.Api.Infrastructure.Notifications;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aveline.Api.Tests;

public class EmailServiceTests
{
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<string> Messages { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Messages.Add(formatter(state, exception));
        }
    }

    [Fact]
    public async Task SendStaffInvitationAsync_RecordsDispatch_WithoutLeakingCode()
    {
        var logger = new CapturingLogger<LoggingEmailService>();
        var service = new LoggingEmailService(logger);
        const string code = "ABCDEFGHJKLM";

        await service.SendStaffInvitationAsync(
            new StaffInvitationEmail("staff@aveline.lk", $"https://app.aveline.lk/invite?code={code}", "org:boutique_staff"));

        var log = Assert.Single(logger.Messages);
        Assert.Contains("staff@aveline.lk", log);
        Assert.Contains("https://app.aveline.lk/invite", log);
        // The one-time code must never appear in the log.
        Assert.DoesNotContain(code, log);
    }

    [Fact]
    public async Task SendStaffInvitationAsync_WithMalformedLink_StillRecords()
    {
        var logger = new CapturingLogger<LoggingEmailService>();
        var service = new LoggingEmailService(logger);

        await service.SendStaffInvitationAsync(
            new StaffInvitationEmail("a@b.c", "not-a-url", "org:boutique_manager"));

        var log = Assert.Single(logger.Messages);
        Assert.Contains("a@b.c", log);
    }
}
