using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.DTOs;
using Aveline.Api.Modules.Integrations.Metrics;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// The outbound WhatsApp path (privacy plan §6.2, Phase 2). These are the tests that make the
/// welcome disclosure, the OTP delivery and the opt-out acknowledgement buildable: an
/// at-least-once caller gets one provider send per idempotency key, a missing integration is a
/// returned skip rather than a throw, and neither the message body nor an unmasked number
/// reaches the log.
/// </summary>
public class OutboundMessagingServiceTests
{
    private const string To = "+94771234567";
    private const string IdempotencyKey = "disclosure:org:customer:1";
    private const string Body = "This is the disclosure body that must never be logged.";

    private readonly AppDbContext _context;
    private readonly StubIntegrationService _integrations = new();
    private readonly StubWhatsAppService _whatsApp = new();
    private readonly RecordingLogger _logger = new();
    private readonly OutboundMetrics _metrics = new();
    private readonly WhatsAppOutboundChannel _channel;

    public OutboundMessagingServiceTests()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: $"OutboundMessaging_{Guid.NewGuid()}")
            .Options;
        _context = new AppDbContext(options);

        _channel = new WhatsAppOutboundChannel(
            _integrations,
            _whatsApp,
            _context,
            _logger,
            _metrics,
            delay: (_, _) => Task.CompletedTask);
    }

    private static IDictionary<string, string> WhatsAppCredentials(
        string accessToken = "wa-access-token", string phoneNumberId = "111") =>
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["accessToken"] = accessToken,
            ["phoneNumberId"] = phoneNumberId,
            ["appSecret"] = "app-secret",
            ["webhookVerifyToken"] = "verify-token",
        };

    [Fact]
    public async Task SendText_WithNoIntegration_ReturnsSkippedAndMakesNoHttpCall()
    {
        _integrations.Configured = false;

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.True(result.Skipped);
        Assert.Equal(0, _whatsApp.SendCalls);
        // A skip is not a send attempt, so nothing is recorded as having gone out.
        Assert.Empty(_context.InboundMessageLogs);
    }

    [Fact]
    public async Task SendText_WithMissingAccessToken_IsAConfigurationFailureNotASend()
    {
        _integrations.Credentials = WhatsAppCredentials(accessToken: " ");

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.True(result.Skipped);
        Assert.Equal(0, _whatsApp.SendCalls);
        Assert.Empty(_context.InboundMessageLogs);
    }

    [Fact]
    public async Task SendText_WithMissingPhoneNumberId_IsAConfigurationFailureNotASend()
    {
        _integrations.Credentials = WhatsAppCredentials(phoneNumberId: "");

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.True(result.Skipped);
        Assert.Equal(0, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task SendText_Success_RecordsExactlyOneOutboundLogWithoutLosingTheBody()
    {
        var orgId = Guid.NewGuid();
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.OUT1", HttpStatus: 200);

        var result = await _channel.SendTextAsync(orgId, To, Body, IdempotencyKey);

        Assert.True(result.IsSuccess);
        Assert.Equal("wamid.OUT1", result.ProviderMessageId);
        Assert.False(result.Skipped);

        var row = Assert.Single(_context.InboundMessageLogs);
        Assert.Equal(orgId, row.OrganizationId);
        Assert.Equal("whatsapp", row.Channel);
        Assert.Equal("outbound", row.Direction);
        // The dedup key the pre-check and the unique filtered index agree on.
        Assert.Equal(IdempotencyKey, row.ExternalId);
        // Meta's own id, kept visible for support.
        Assert.Equal("wamid.OUT1", row.From);
        Assert.Equal(To, row.To);
        Assert.Equal(Body, row.Content);
    }

    [Fact]
    public async Task SendText_SameIdempotencyKeyTwice_CallsTheProviderOnce()
    {
        var orgId = Guid.NewGuid();
        // A provider that mints a *new* wamid per call is the point of the test: if the pre-check
        // were matching on the provider id instead of the caller's key, this second call would go
        // out and the customer would be messaged twice.
        _whatsApp.MintFreshMessageIds = true;

        var first = await _channel.SendTextAsync(orgId, To, Body, IdempotencyKey);
        var second = await _channel.SendTextAsync(orgId, To, Body, IdempotencyKey);

        Assert.True(first.IsSuccess);
        Assert.True(second.IsSuccess);
        // The prior success is replayed, so the customer is not messaged twice.
        Assert.Equal(first.ProviderMessageId, second.ProviderMessageId);
        Assert.Equal(1, _whatsApp.SendCalls);
        Assert.Single(_context.InboundMessageLogs);
    }

    [Fact]
    public async Task SendText_IsScopedPerOrganization_SoOneOrgCannotSuppressAnothersSend()
    {
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.OUT1", HttpStatus: 200);

        await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);
        await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.Equal(2, _whatsApp.SendCalls);
        Assert.Equal(2, await _context.InboundMessageLogs.CountAsync());
    }

    [Fact]
    public async Task SendText_On429_RetriesUntilItSucceeds()
    {
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: false, Error: "rate limited", HttpStatus: 429));
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: false, Error: "rate limited", HttpStatus: 429));
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.RETRY", HttpStatus: 200));

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.True(result.IsSuccess);
        Assert.Equal("wamid.RETRY", result.ProviderMessageId);
        Assert.Equal(3, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task SendText_On500_Retries()
    {
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: false, Error: "server error", HttpStatus: 500));
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.RETRY", HttpStatus: 200));

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _whatsApp.SendCalls);
    }

    [Fact]
    public async Task SendText_On400_DoesNotRetry()
    {
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: false, Error: "bad payload", HttpStatus: 400);

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.False(result.Skipped);
        Assert.Equal(1, _whatsApp.SendCalls);
        Assert.Empty(_context.InboundMessageLogs);
    }

    [Fact]
    public async Task SendText_OnTransportFailure_RetriesAndThenReportsFailureWithoutThrowing()
    {
        // No status is the transport-failure shape: the retry layer must classify it as
        // retryable from the status alone, never by pattern-matching the error text.
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: false, Error: "connection reset", HttpStatus: null);

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.Equal(3, _whatsApp.SendCalls);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SendText_MetaFailure_DoesNotThrow()
    {
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: false, Error: "recipient not in allowed list", HttpStatus: 403);

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.Equal("recipient not in allowed list", result.Error);
    }

    [Fact]
    public async Task SendText_WhenTheProviderThrows_ReturnsFailureRatherThanPropagating()
    {
        _whatsApp.Throw = new HttpRequestException("socket closed");

        var result = await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.Error);
    }

    [Fact]
    public async Task SendText_RetryKeepsTheSameProviderCallArguments()
    {
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: false, Error: "server error", HttpStatus: 503));
        _whatsApp.Results.Enqueue(new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.RETRY", HttpStatus: 200));

        await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.Equal(2, _whatsApp.Attempts.Count);
        // A fresh key per attempt would defeat the idempotency pre-check; the payload is the
        // same on every attempt.
        Assert.All(_whatsApp.Attempts, attempt =>
        {
            Assert.Equal("wa-access-token", attempt.AccessToken);
            Assert.Equal("111", attempt.PhoneNumberId);
            Assert.Equal(To, attempt.To);
            Assert.Equal(Body, attempt.Text);
        });
    }

    [Fact]
    public async Task SendText_NeverWritesTheMessageBodyOrTheUnmaskedNumberToTheLog()
    {
        var orgId = Guid.NewGuid();
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: false, Error: "bad payload", HttpStatus: 400);

        await _channel.SendTextAsync(orgId, To, Body, IdempotencyKey);
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.OUT1", HttpStatus: 200);
        await _channel.SendTextAsync(orgId, To, Body, "a-second-key");

        var logged = string.Join("\n", _logger.All());
        Assert.DoesNotContain(Body, logged, StringComparison.Ordinal);
        Assert.DoesNotContain(To, logged, StringComparison.Ordinal);
        // The masked form proves the failure was still diagnosable without the number.
        Assert.Contains("+94****4567", logged, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendText_RecordsTheOutboundResultInTheMetricsFamily()
    {
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.OUT1", HttpStatus: 200);

        await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.Equal(("whatsapp", OutboundOutcomes.Sent), _metrics.LastResult);
    }

    [Fact]
    public async Task SendText_RecordsASkippedOutcomeWhenTheIntegrationIsAbsent()
    {
        _integrations.Configured = false;

        await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.Equal(("whatsapp", OutboundOutcomes.Skipped), _metrics.LastResult);
    }

    [Fact]
    public async Task SendText_OnTerminalFailure_RecordsAFailedOutcome()
    {
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: false, Error: "bad payload", HttpStatus: 400);

        await _channel.SendTextAsync(Guid.NewGuid(), To, Body, IdempotencyKey);

        Assert.Equal(("whatsapp", OutboundOutcomes.Failed), _metrics.LastResult);
    }

    [Fact]
    public async Task SendTemplate_GatedOnPolicy_GoesOutAsATemplateAndRecordsNothing()
    {
        // Item 2.3: the template path exists so proactive messaging is implementable, but Q-2
        // has not been confirmed, so no production sender may call it (see the class remarks).
        _whatsApp.Next = new WhatsAppSendResult(IsSuccess: true, MessageId: "wamid.TPL1", HttpStatus: 200);

        var result = await _channel.SendTemplateAsync(
            Guid.NewGuid(), To, "appointment_reminder", "en", null, "tpl-key");

        Assert.True(result.IsSuccess);
        Assert.Single(_whatsApp.Attempts);
        Assert.Equal("appointment_reminder", _whatsApp.Attempts[0].TemplateName);
        Assert.Equal("en", _whatsApp.Attempts[0].LanguageCode);
    }

    private sealed class RecordingLogger : ILogger<WhatsAppOutboundChannel>
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> All() => _messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            lock (_messages)
            {
                _messages.Add(formatter(state, exception));
            }
        }
    }

    private sealed class StubIntegrationService : IIntegrationService
    {
        public bool Configured { get; set; } = true;

        public IDictionary<string, string> Credentials { get; set; } = WhatsAppCredentials();

        public Task<IDictionary<string, string>> GetCredentialsAsync(
            Guid organizationId, IntegrationType type, CancellationToken cancellationToken = default)
            => Configured
                ? Task.FromResult(Credentials)
                : throw new IntegrationNotConfiguredException(type);

        public Task<IntegrationStatusDto> SaveAsync(
            Guid organizationId, IntegrationType type, SaveIntegrationRequest request,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<IntegrationStatusDto>> ListStatusAsync(
            Guid organizationId, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IntegrationStatusDto> MarkConnectedAsync(
            Guid organizationId, IntegrationType type, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IntegrationStatusDto> MarkFailedAsync(
            Guid organizationId, IntegrationType type, string error,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IntegrationStatusDto> MarkExpiredAsync(
            Guid organizationId, IntegrationType type, string error,
            CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task<IntegrationTestResultDto> TestConnectionAsync(
            Guid organizationId, IntegrationType type, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();

        public Task DeleteAsync(
            Guid organizationId, IntegrationType type, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }

    private sealed class StubWhatsAppService : IWhatsAppService
    {
        private int _minted;

        public int SendCalls { get; private set; }

        public WhatsAppSendResult? Next { get; set; }

        public Queue<WhatsAppSendResult> Results { get; } = new();

        public Exception? Throw { get; set; }

        public List<Attempt> Attempts { get; } = [];

        /// <summary>When true, every success returns a distinct provider message id.</summary>
        public bool MintFreshMessageIds { get; set; }

        public Task<WhatsAppTestResult> TestConnectionAsync(
            string accessToken, string phoneNumberId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppTestResult(IsValid: true));

        public Task<WhatsAppMediaResult> GetMediaAsync(
            string accessToken, string mediaId, CancellationToken cancellationToken = default)
            => Task.FromResult(new WhatsAppMediaResult(IsSuccess: false, Error: "not used"));

        public Task<WhatsAppSendResult> SendMessageAsync(
            string accessToken, string phoneNumberId, string to, string text,
            CancellationToken cancellationToken = default)
            => SendAsync(new Attempt(accessToken, phoneNumberId, to, text, null, null));

        public Task<WhatsAppSendResult> SendTemplateAsync(
            string accessToken, string phoneNumberId, string to, string templateName,
            string languageCode, IReadOnlyList<object>? components,
            CancellationToken cancellationToken = default)
            => SendAsync(new Attempt(accessToken, phoneNumberId, to, null, templateName, languageCode));

        private Task<WhatsAppSendResult> SendAsync(Attempt attempt)
        {
            SendCalls++;
            Attempts.Add(attempt);

            if (Throw is not null)
            {
                return Task.FromException<WhatsAppSendResult>(Throw);
            }

            if (Results.Count > 0)
            {
                return Task.FromResult(Results.Dequeue());
            }

            if (Next is not null)
            {
                return Task.FromResult(Next);
            }

            var id = MintFreshMessageIds ? $"wamid.OUT{++_minted}" : "wamid.OUT1";
            return Task.FromResult(new WhatsAppSendResult(IsSuccess: true, MessageId: id, HttpStatus: 200));
        }
    }

    private sealed record Attempt(
        string AccessToken, string PhoneNumberId, string To, string? Text,
        string? TemplateName, string? LanguageCode);
}
