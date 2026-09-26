using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.Media;

/// <summary>
/// The hand-rolled retry policy for a Cloudinary API call. <c>Aveline.Api</c> carries no
/// resilience library and this unit adds exactly one package (<c>CloudinaryDotNet</c>), so the
/// backoff is written here rather than delegated to Polly (risk R22).
/// </summary>
/// <remarks>
/// <para>
/// The retryable statuses are <c>420</c> and the 5xx family. <c>429</c> is not a Cloudinary
/// status and is deliberately not retried; a <c>400</c>, <c>401</c>, <c>403</c>, <c>404</c> or
/// <c>413</c> is the provider's final answer and is surfaced immediately.
/// </para>
/// <para>
/// The delay is exponential with jitter and bounded: <c>min(250 ms · 2^(n-1), 5 s)</c>, scaled by
/// a factor in <c>[0.5, 1.0]</c>. Both the growth and the bound are asserted by
/// <see cref="ComputeBackoff"/>'s own tests.
/// </para>
/// <para>
/// Every message that reaches the logger — or the thrown exception — passes through the redactor
/// first, because the provider's error text is the most likely place for an account secret to
/// surface (risk R24).
/// </para>
/// </remarks>
internal sealed class CloudinaryRetryPolicy
{
    internal const int BaseDelayMilliseconds = 250;
    internal const int MaxDelayMilliseconds = 5000;

    private const string Redacted = "[redacted]";

    private readonly CloudinaryOptions _options;
    private readonly ILogger _logger;
    private readonly IMediaRetryDelay _delay;

    public CloudinaryRetryPolicy(
        CloudinaryOptions options,
        ILogger logger,
        IMediaRetryDelay? delay = null)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _delay = delay ?? new TaskDelayMediaRetryDelay();
    }

    /// <summary>
    /// The backoff for retry <paramref name="retryNumber"/> (1-based), scaled by
    /// <paramref name="jitter01"/>. Pure, so the growth and the ceiling are falsifiable.
    /// </summary>
    public static TimeSpan ComputeBackoff(int retryNumber, double jitter01)
    {
        if (retryNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(retryNumber), retryNumber, "The retry number is 1-based.");
        }

        // The exponent is capped before the power so a large retry count cannot overflow.
        var exponent = Math.Min(retryNumber - 1, 16);
        var exponential = Math.Min(BaseDelayMilliseconds * Math.Pow(2, exponent), MaxDelayMilliseconds);
        var factor = 0.5 + (0.5 * Math.Clamp(jitter01, 0d, 1d));

        return TimeSpan.FromMilliseconds(exponential * factor);
    }

    /// <summary>
    /// Runs <paramref name="call"/>, retrying a <c>420</c>/5xx answer or a transient transport
    /// failure up to <c>Cloudinary:UploadRetryAttempts</c> times, and returns the first success.
    /// A non-retryable answer throws <see cref="MediaStorageException"/> without a second call.
    /// </summary>
    public async Task<CloudinaryAttemptResult> ExecuteAsync(
        string operation,
        Func<CancellationToken, Task<CloudinaryAttemptResult>> call,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(call);

        var maxRetries = Math.Max(0, _options.UploadRetryAttempts);
        var attempt = 0;

        while (true)
        {
            CloudinaryAttemptResult result;
            try
            {
                result = await call(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                var message = Redact(ex.Message) ?? ex.GetType().Name;
                if (attempt >= maxRetries)
                {
                    _logger.LogError(
                        "Cloudinary {Operation} failed after {Attempts} attempts: {Message}",
                        operation, attempt + 1, message);
                    throw Failure(0, message, ex);
                }

                attempt++;
                _logger.LogWarning(
                    "Cloudinary {Operation} hit a transient failure; retrying (attempt {Attempt} of {MaxRetries}): {Message}",
                    operation, attempt, maxRetries, message);
                await _delay.DelayAsync(ComputeBackoff(attempt, Random.Shared.NextDouble()), ct)
                    .ConfigureAwait(false);
                continue;
            }
            catch (Exception ex)
            {
                // A non-transient SDK failure (for example a response that could not be
                // deserialised). Retrying would answer the same way, so it is surfaced — redacted.
                var message = Redact(ex.Message) ?? ex.GetType().Name;
                _logger.LogError("Cloudinary {Operation} failed: {Message}", operation, message);
                throw Failure(0, message, ex);
            }

            if (result.IsSuccess)
            {
                return result;
            }

            var failure = Redact(result.ErrorMessage) ?? $"Cloudinary returned {result.StatusCode}.";

            if (!CloudinaryAttemptResult.IsRetryableStatus(result.StatusCode))
            {
                _logger.LogError(
                    "Cloudinary {Operation} failed with status {StatusCode}: {Message}",
                    operation, result.StatusCode, failure);
                throw new MediaStorageException(result.StatusCode, failure);
            }

            if (attempt >= maxRetries)
            {
                _logger.LogError(
                    "Cloudinary {Operation} exhausted its retries after {Attempts} attempts; last status {StatusCode}: {Message}",
                    operation, attempt + 1, result.StatusCode, failure);
                throw new MediaStorageException(result.StatusCode, failure);
            }

            attempt++;
            _logger.LogWarning(
                "Cloudinary {Operation} returned retryable status {StatusCode}; retrying (attempt {Attempt} of {MaxRetries}): {Message}",
                operation, result.StatusCode, attempt, maxRetries, failure);
            await _delay.DelayAsync(ComputeBackoff(attempt, Random.Shared.NextDouble()), ct)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Runs a provider <em>fetch</em>, retrying a Cloudinary <c>420</c>/5xx answer or a transient
    /// transport failure with the same bounded backoff <see cref="ExecuteAsync"/> uses. The proxy
    /// path is the one read that pays egress, so a <c>420</c> must not surface as a hard failure
    /// until the budget is spent (migration plan §7.7).
    /// </summary>
    /// <remarks>
    /// The first non-retryable provider answer is surfaced unchanged, so the caller still sees the
    /// provider's own status (<c>401</c>/<c>403</c> → <c>502</c> at the route). A transient
    /// transport failure is surfaced as a status-less <see cref="MediaStorageException"/> carrying
    /// the cause, which is how a timeout stays distinguishable.
    /// </remarks>
    public async Task<Stream> ExecuteFetchAsync(
        string operation,
        Func<CancellationToken, Task<Stream>> call,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentNullException.ThrowIfNull(call);

        var maxRetries = Math.Max(0, _options.UploadRetryAttempts);
        var attempt = 0;

        while (true)
        {
            try
            {
                return await call(ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (MediaStorageException ex)
                when (ex.StatusCode is int status && CloudinaryAttemptResult.IsRetryableStatus(status))
            {
                var message = Redact(ex.Message) ?? $"Cloudinary returned {ex.StatusCode}.";

                if (attempt >= maxRetries)
                {
                    _logger.LogError(
                        "Cloudinary {Operation} exhausted its retries after {Attempts} attempts; last status {StatusCode}: {Message}",
                        operation, attempt + 1, ex.StatusCode, message);
                    throw;
                }

                attempt++;
                _logger.LogWarning(
                    "Cloudinary {Operation} returned retryable status {StatusCode}; retrying (attempt {Attempt} of {MaxRetries}): {Message}",
                    operation, ex.StatusCode, attempt, maxRetries, message);
                await _delay.DelayAsync(ComputeBackoff(attempt, Random.Shared.NextDouble()), ct)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (IsTransient(ex))
            {
                var message = Redact(ex.Message) ?? ex.GetType().Name;

                if (attempt >= maxRetries)
                {
                    _logger.LogError(
                        "Cloudinary {Operation} failed after {Attempts} attempts: {Message}",
                        operation, attempt + 1, message);
                    throw Failure(0, message, ex);
                }

                attempt++;
                _logger.LogWarning(
                    "Cloudinary {Operation} hit a transient failure; retrying (attempt {Attempt} of {MaxRetries}): {Message}",
                    operation, attempt, maxRetries, message);
                await _delay.DelayAsync(ComputeBackoff(attempt, Random.Shared.NextDouble()), ct)
                    .ConfigureAwait(false);
            }
            catch (MediaStorageException)
            {
                // A provider answer that is not worth retrying (401/403/404/…) is surfaced
                // unchanged: the route maps 401/403 to 502 and everything else to 502, but the
                // status must survive so the failure is classified and logged as what it is.
                throw;
            }
            catch (Exception ex)
            {
                var message = Redact(ex.Message) ?? ex.GetType().Name;
                _logger.LogError("Cloudinary {Operation} failed: {Message}", operation, message);
                throw Failure(0, message, ex);
            }
        }
    }

    /// <summary>
    /// Whether an exception is a transport or timeout failure worth another attempt. A
    /// deserialisation failure is not: it will answer the same way every time.
    /// </summary>
    private static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException or TimeoutException or TaskCanceledException;

    /// <summary>
    /// Builds the surfaced failure. The original exception is carried only when redaction did not
    /// change its message: <see cref="Exception.ToString"/> prints the whole inner chain, so a
    /// secret inside a wrapped SDK exception would otherwise leak through any logger that prints
    /// the exception rather than the message (risk R24).
    /// </summary>
    private static MediaStorageException Failure(int statusCode, string message, Exception cause)
    {
        var safeCause = string.Equals(cause.Message, message, StringComparison.Ordinal)
            ? cause
            : new Exception(message);

        return new MediaStorageException(statusCode, message, safeCause);
    }

    /// <summary>Replaces the account's key and secret wherever the provider's text carried them.</summary>
    private string? Redact(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var redacted = text;
        foreach (var credential in new[] { _options.ApiSecret, _options.ApiKey })
        {
            if (!string.IsNullOrEmpty(credential))
            {
                redacted = redacted.Replace(credential, Redacted, StringComparison.Ordinal);
            }
        }

        return redacted;
    }
}
