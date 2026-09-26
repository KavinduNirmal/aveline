using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Integrations.Metrics;
using Aveline.Api.Modules.Integrations.Models;
using Aveline.Api.Modules.Integrations.Services.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Integrations.Services;

/// <summary>
/// The WhatsApp delivery channel: per-organization credentials, a stable idempotency key, bounded
/// hand-rolled retry, and an outbound <see cref="InboundMessageLog"/> row on success (privacy plan
/// §6.2).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the retry lives here and not in the provider.</b> The idempotency key is the contract
/// the caller relies on, and it must mean the same thing across every attempt of one logical
/// message. Retrying inside the provider would make each attempt a fresh logical send as far as
/// the caller can see. The provider therefore does exactly one attempt and surfaces Meta's HTTP
/// status, and this layer decides whether to try again.
/// </para>
/// <para>
/// <b>The retry policy.</b> At most <see cref="DefaultMaxAttempts"/> attempts, with backoff
/// <c>250ms → 1s → 4s</c>, each delay jittered to avoid a synchronized retry wave when Meta has a
/// blip. Only a transport failure (no HTTP status) and 429/5xx are retried; any other 4xx is
/// terminal, because a 400 means the payload was wrong and will stay wrong. A total budget
/// (<see cref="DefaultMaxTotalDelay"/>) caps how long this can hold a caller open, so an inbound
/// webhook path is never left waiting on Meta's behalf. No Polly or resilience package is used:
/// the policy is four lines and does not justify a dependency (Rules2 §19-31).
/// </para>
/// <para>
/// <b>Never logs the message body.</b> Only the recipient's masked number, the provider message id
/// and Meta's error text are logged; <see cref="WhatsAppService"/> masks identically.
/// </para>
/// </remarks>
public sealed class WhatsAppOutboundChannel : IOutboundChannel
{
    /// <summary>The channel key, also the <c>channel</c> metric label value.</summary>
    public const string Key = "whatsapp";

    /// <summary>The <c>InboundMessageLog.Channel</c> value written for an outbound send.</summary>
    public const string LogChannel = "whatsapp";

    /// <summary>The <c>InboundMessageLog.Direction</c> value written for an outbound send.</summary>
    public const string OutboundDirection = "outbound";

    /// <summary>Three attempts: the initial send plus two retries.</summary>
    public const int DefaultMaxAttempts = 3;

    /// <summary>The un-jittered backoff sequence between attempts: 250ms, then 1s.</summary>
    public static readonly TimeSpan[] DefaultBackoff = [TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1)];

    /// <summary>
    /// The cap on jittered delay this layer will spend in total. A caller that exhausts it gets the
    /// last failure rather than a held-open request.
    /// </summary>
    public static readonly TimeSpan DefaultMaxTotalDelay = TimeSpan.FromSeconds(8);

    private static readonly string MissingAccessToken =
        "The WhatsApp integration is missing accessToken.";

    private static readonly string MissingPhoneNumberId =
        "The WhatsApp integration is missing phoneNumberId.";

    private readonly IIntegrationService _integrations;
    private readonly IWhatsAppService _whatsApp;
    private readonly AppDbContext _db;
    private readonly ILogger<WhatsAppOutboundChannel> _logger;
    private readonly OutboundMetrics _metrics;
    private readonly Func<TimeSpan, CancellationToken, Task> _delay;
    private readonly int _maxAttempts;
    private readonly TimeSpan _maxTotalDelay;
    private readonly Random _jitter = new();

    public WhatsAppOutboundChannel(
        IIntegrationService integrations,
        IWhatsAppService whatsApp,
        AppDbContext db,
        ILogger<WhatsAppOutboundChannel> logger,
        OutboundMetrics? metrics = null,
        TimeSpan? maxTotalDelay = null,
        Func<TimeSpan, CancellationToken, Task>? delay = null,
        int maxAttempts = DefaultMaxAttempts)
    {
        ArgumentNullException.ThrowIfNull(integrations);
        ArgumentNullException.ThrowIfNull(whatsApp);
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(logger);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);

        _integrations = integrations;
        _whatsApp = whatsApp;
        _db = db;
        _logger = logger;
        _metrics = metrics ?? new OutboundMetrics();
        _maxTotalDelay = maxTotalDelay ?? DefaultMaxTotalDelay;
        _delay = delay ?? Task.Delay;
        _maxAttempts = maxAttempts;
    }

    /// <inheritdoc/>
    public string ChannelKey => Key;

    /// <inheritdoc/>
    public async Task<bool> IsConfiguredAsync(
        Guid organizationId, CancellationToken cancellationToken = default)
    {
        var credentials = await TryGetCredentialsAsync(organizationId, cancellationToken);
        return credentials is not null
               && HasSendCredentials(credentials, out _);
    }

    /// <inheritdoc/>
    public async Task<OutboundMessageResult> SendTextAsync(
        Guid organizationId,
        string toE164,
        string text,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toE164);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        // 1-2. Resolve this organization's own credentials. An absent or half-filled integration is
        // a returned skip, not an exception and not a send attempt.
        var credentials = await TryGetCredentialsAsync(organizationId, cancellationToken);
        if (credentials is null)
        {
            _logger.LogWarning(
                "Outbound WhatsApp skipped: the integration is not configured. organizationId={OrganizationId} to={To}",
                organizationId, Mask(toE164));
            return Record(OutboundMessageResult.NotConfigured(
                "The WhatsApp integration is not configured for this organization."));
        }

        if (!HasSendCredentials(credentials, out var missing))
        {
            _logger.LogWarning(
                "Outbound WhatsApp skipped: {Missing} organizationId={OrganizationId} to={To}",
                missing, organizationId, Mask(toE164));
            return Record(OutboundMessageResult.NotConfigured(missing));
        }

        // 3. Idempotency pre-check: a prior success for this key is replayed without touching Meta.
        var prior = await FindPriorSuccessAsync(organizationId, idempotencyKey, cancellationToken);
        if (prior is not null)
        {
            _logger.LogInformation(
                "Outbound WhatsApp replayed from the recorded log. organizationId={OrganizationId} to={To} messageId={MessageId}",
                organizationId, Mask(toE164), prior);
            return OutboundMessageResult.Sent(prior);
        }

        // 4. One logical send, with bounded retries that keep the same key.
        var attempt = await SendWithRetryAsync(
            organizationId, toE164, cancellationToken,
            (token) => _whatsApp.SendMessageAsync(
                credentials["accessToken"], credentials["phoneNumberId"], toE164, text, token));

        if (!attempt.Result.IsSuccess)
        {
            // 6. A provider failure is returned, never thrown, and logged with the number masked.
            _logger.LogWarning(
                "Outbound WhatsApp send failed after {Attempts} attempt(s). organizationId={OrganizationId} to={To} status={Status} error={Error}",
                attempt.Attempts, organizationId, Mask(toE164), attempt.Result.HttpStatus, attempt.Result.Error);
            return Record(OutboundMessageResult.Failed(attempt.Result.Error ?? "The provider refused the message."));
        }

        // 5. Record the send. The unique filtered index on (OrganizationId, ExternalId) is the
        // backstop for a concurrent duplicate, so losing that race is "already recorded".
        await RecordOutboundAsync(
            organizationId, toE164, text, idempotencyKey, attempt.Result.MessageId, cancellationToken);

        return Record(OutboundMessageResult.Sent(attempt.Result.MessageId));
    }

    /// <summary>
    /// The template path, gated on policy question Q-2. See
    /// <see cref="IWhatsAppService.SendTemplateAsync"/>: this method exists so proactive messaging
    /// is implementable and tested, and <b>no proactive sender may be wired to it</b> until Meta's
    /// template rules are confirmed.
    /// </summary>
    public async Task<OutboundMessageResult> SendTemplateAsync(
        Guid organizationId,
        string toE164,
        string templateName,
        string languageCode,
        IReadOnlyList<object>? components,
        string idempotencyKey,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toE164);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateName);
        ArgumentException.ThrowIfNullOrWhiteSpace(languageCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(idempotencyKey);

        var credentials = await TryGetCredentialsAsync(organizationId, cancellationToken);
        string? reason = null;
        if (credentials is null)
        {
            reason = "The WhatsApp integration is not configured for this organization.";
        }
        else if (!HasSendCredentials(credentials, out var missing))
        {
            reason = missing;
        }

        if (reason is not null)
        {
            _logger.LogWarning(
                "Outbound WhatsApp template skipped: {Reason} organizationId={OrganizationId} to={To}",
                reason, organizationId, Mask(toE164));
            return Record(OutboundMessageResult.NotConfigured(reason));
        }

        var prior = await FindPriorSuccessAsync(organizationId, idempotencyKey, cancellationToken);
        if (prior is not null)
        {
            return OutboundMessageResult.Sent(prior);
        }

        var attempt = await SendWithRetryAsync(
            organizationId, toE164, cancellationToken,
            (token) => _whatsApp.SendTemplateAsync(
                credentials["accessToken"], credentials["phoneNumberId"], toE164,
                templateName, languageCode, components, token));

        if (!attempt.Result.IsSuccess)
        {
            _logger.LogWarning(
                "Outbound WhatsApp template failed after {Attempts} attempt(s). organizationId={OrganizationId} to={To} template={Template} status={Status} error={Error}",
                attempt.Attempts, organizationId, Mask(toE164), templateName,
                attempt.Result.HttpStatus, attempt.Result.Error);
            return Record(OutboundMessageResult.Failed(
                attempt.Result.Error ?? "The provider refused the template."));
        }

        // The logged content is the template name, not the rendered body: the component
        // parameters are message content and must not be persisted as free text here.
        await RecordOutboundAsync(
            organizationId, toE164, templateName, idempotencyKey, attempt.Result.MessageId, cancellationToken);

        return Record(OutboundMessageResult.Sent(attempt.Result.MessageId));
    }

    /// <summary>
    /// Runs one logical send with bounded, jittered, exponential backoff. Retryable outcomes are a
    /// transport failure (no HTTP status) and 429/5xx; every other 4xx is terminal.
    /// </summary>
    private async Task<AttemptOutcome> SendWithRetryAsync(
        Guid organizationId,
        string toE164,
        CancellationToken cancellationToken,
        Func<CancellationToken, Task<WhatsAppSendResult>> send)
    {
        var spent = TimeSpan.Zero;
        WhatsAppSendResult? last = null;

        for (var attempt = 1; attempt <= _maxAttempts; attempt++)
        {
            last = await AttemptAsync(send, cancellationToken);

            if (last.IsSuccess || attempt == _maxAttempts || !IsRetryable(last))
            {
                return new AttemptOutcome(last, attempt);
            }

            var backoff = DefaultBackoff[Math.Min(attempt - 1, DefaultBackoff.Length - 1)];
            var jittered = Jitter(backoff);

            // The budget is checked before sleeping, so a caller is never held open past it: the
            // last failure is returned instead.
            if (spent + jittered > _maxTotalDelay)
            {
                _logger.LogWarning(
                    "Outbound WhatsApp retry budget exhausted; giving up. organizationId={OrganizationId} to={To} attempts={Attempts} status={Status}",
                    organizationId, Mask(toE164), attempt, last.HttpStatus);
                return new AttemptOutcome(last, attempt);
            }

            spent += jittered;
            _logger.LogWarning(
                "Outbound WhatsApp retrying after a retryable failure. organizationId={OrganizationId} to={To} attempt={Attempt} status={Status} delayMs={DelayMs}",
                organizationId, Mask(toE164), attempt, last.HttpStatus, (int)jittered.TotalMilliseconds);

            await _delay(jittered, cancellationToken);
        }

        return new AttemptOutcome(last!, _maxAttempts);
    }

    /// <summary>
    /// One provider attempt. The provider already converts transport failures into a failed result;
    /// the catch here covers a provider that throws instead, so this layer's "never throws"
    /// contract holds even for a future channel implementation.
    /// </summary>
    private async Task<WhatsAppSendResult> AttemptAsync(
        Func<CancellationToken, Task<WhatsAppSendResult>> send, CancellationToken cancellationToken)
    {
        try
        {
            return await send(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new WhatsAppSendResult(IsSuccess: false, Error: ex.Message);
        }
    }

    /// <summary>
    /// Retryable means "the same request might succeed later": 429 (throttled), any 5xx (Meta's
    /// fault) or no status at all (the call never completed). A 400/401/403/404 is the payload or
    /// the credentials being wrong, and will stay wrong.
    /// </summary>
    private static bool IsRetryable(WhatsAppSendResult result) =>
        result.HttpStatus is null or 429 || result.HttpStatus is >= 500 and <= 599;

    /// <summary>Applies ±25% jitter so concurrent retries do not synchronise on Meta.</summary>
    private TimeSpan Jitter(TimeSpan backoff)
    {
        var factor = 0.75 + (_jitter.NextDouble() * 0.5);
        return TimeSpan.FromMilliseconds(backoff.TotalMilliseconds * factor);
    }

    /// <summary>
    /// Reads the organization's decrypted WhatsApp credentials, or <c>null</c> when the integration
    /// is not configured. An <c>IntegrationNotConfiguredException</c> is a normal answer here, not
    /// an error.
    /// </summary>
    private async Task<IDictionary<string, string>?> TryGetCredentialsAsync(
        Guid organizationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _integrations.GetCredentialsAsync(
                organizationId, IntegrationType.WhatsApp, cancellationToken);
        }
        catch (IntegrationNotConfiguredException)
        {
            return null;
        }
    }

    /// <summary>The two keys a send actually needs, checked as a pair rather than one at a time.</summary>
    private static bool HasSendCredentials(IDictionary<string, string> credentials, out string missing)
    {
        if (!credentials.TryGetValue("accessToken", out var token) || string.IsNullOrWhiteSpace(token))
        {
            missing = MissingAccessToken;
            return false;
        }

        if (!credentials.TryGetValue("phoneNumberId", out var phoneNumberId)
            || string.IsNullOrWhiteSpace(phoneNumberId))
        {
            missing = MissingPhoneNumberId;
            return false;
        }

        missing = string.Empty;
        return true;
    }

    /// <summary>
    /// The prior provider message id for this key, when one was already recorded. This is the
    /// pre-check that makes an at-least-once caller safe.
    /// </summary>
    /// <remarks>
    /// It looks the key up on <c>ExternalId</c>, because that is the column the unique filtered
    /// index protects — which is what makes "the same key twice" a database-enforced guarantee and
    /// not merely a hopeful query. See <see cref="RecordOutboundAsync"/> for why the key, and not
    /// the provider's <c>wamid</c>, is the value stored there.
    /// </remarks>
    private async Task<string?> FindPriorSuccessAsync(
        Guid organizationId, string idempotencyKey, CancellationToken cancellationToken)
    {
        var existing = await _db.InboundMessageLogs
            .AsNoTracking()
            .FirstOrDefaultAsync(
                log => log.OrganizationId == organizationId && log.ExternalId == idempotencyKey,
                cancellationToken);

        return existing?.From;
    }

    /// <summary>
    /// Writes the outbound audit row after the provider accepts. The unique filtered index on
    /// (OrganizationId, ExternalId) makes a double-write a database-level refusal, which is treated
    /// as "already recorded" rather than an error: the message did go out, and the caller should
    /// not retry it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>A deliberate reading of plan §6.2, which is internally inconsistent here.</b> The plan's
    /// pre-check (step 3) and its unique filtered index both key on <c>ExternalId</c>, while its
    /// write step (step 5) says to store the provider's <c>wamid</c> there. Those cannot both hold:
    /// the caller does not know the <c>wamid</c> before the send, so a <c>wamid</c> in
    /// <c>ExternalId</c> leaves the pre-check with nothing to match and turns the index into a
    /// guard against nothing. The key is therefore stored in <c>ExternalId</c> (the dedup key the
    /// pre-check and the index agree on) and Meta's <c>wamid</c> is stored in <c>From</c> — for an
    /// outbound row the sender is us, so <c>From</c> is the right column for our own message id,
    /// and it keeps the id visible in <c>GET /orgs/&#123;id&#125;/integrations/messages</c>.
    /// </para>
    /// <para>
    /// <c>Content</c> holds the message text, which is what the customer received and what a
    /// data-subject request must be able to erase. No idempotency key is smuggled into it.
    /// </para>
    /// </remarks>
    private async Task RecordOutboundAsync(
        Guid organizationId,
        string toE164,
        string content,
        string idempotencyKey,
        string? providerMessageId,
        CancellationToken cancellationToken)
    {
        _db.InboundMessageLogs.Add(new InboundMessageLog
        {
            OrganizationId = organizationId,
            Channel = LogChannel,
            Direction = OutboundDirection,
            // The caller-supplied key is the dedup key; the unique filtered index protects it.
            ExternalId = idempotencyKey,
            // Our own provider message id, when Meta returned one.
            From = string.IsNullOrWhiteSpace(providerMessageId) ? null : providerMessageId,
            To = toE164,
            Content = content,
            ReceivedAt = DateTime.UtcNow,
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Already recorded: the same key was written by a concurrent attempt. The customer has
            // the message either way, so this is a logged no-op rather than a failure to retry.
            _logger.LogInformation(
                ex,
                "Outbound WhatsApp log row already recorded. organizationId={OrganizationId} to={To} messageId={MessageId}",
                organizationId, Mask(toE164), providerMessageId);
        }
    }

    /// <summary>Records the metric label for an outcome and returns it unchanged.</summary>
    private OutboundMessageResult Record(OutboundMessageResult result)
    {
        _metrics.RecordResult(Key, result.IsSuccess
            ? OutboundOutcomes.Sent
            : result.Skipped ? OutboundOutcomes.Skipped : OutboundOutcomes.Failed);
        return result;
    }

    /// <summary>Masks a phone number for safe logging, e.g. <c>+94****4567</c>.</summary>
    private static string Mask(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return "****";
        }

        return value.Length <= 8 ? "****" : value[..3] + "****" + value[^4..];
    }

    /// <summary>The last provider result and how many attempts it took to get there.</summary>
    private sealed record AttemptOutcome(WhatsAppSendResult Result, int Attempts);
}
