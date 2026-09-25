using System.Net;
using System.Text;

namespace Aveline.Api.Tests;

/// <summary>
/// A hand-written <see cref="HttpMessageHandler"/> that stands in for OnePay's HTTP surface
/// (plan Phase 8). It records every request it receives and answers from a path-keyed script, so an
/// adapter test can assert the request shape and drive a status without a network or a sandbox
/// account.
/// </summary>
/// <remarks>
/// <para>
/// There is no sandbox account in this environment, so this is the only thing that can drive the
/// adapter's HTTP layer. It deliberately does not model behaviour: the adapter, not the stub, decides
/// what a status means. A path with no script answers <c>404</c>, which is what a request the test
/// did not expect should look like.
/// </para>
/// <para>
/// Requests are captured as <see cref="Captured"/>, a materialised record rather than the live
/// <see cref="HttpRequestMessage"/>: the message is disposed with its response, so a test that read
/// its body later would be reading a disposed stream.
/// </para>
/// </remarks>
internal sealed class OnePayHttpStub : HttpMessageHandler
{
    /// <summary>The base address every scripted response is served from.</summary>
    public static readonly Uri BaseAddress = new("https://sandbox.onepay.lk/");

    private readonly List<Func<Captured, HttpResponseMessage?>> _scripts = [];
    private readonly HashSet<string> _knownTransactionIds = new(StringComparer.Ordinal);

    private Exception? _sendFailure;
    private HttpStatusCode? _unmatchedStatus;

    /// <summary>Every request the adapter sent, oldest first, in dispatch order.</summary>
    public List<Captured> Requests { get; } = [];

    /// <summary>
    /// The transaction ids a scripted create has handed out. A status read for an id in here is a
    /// known transaction; any other id is the provider's 404, which is what makes the adapter's
    /// unknown-id behaviour testable without the test naming a fabricated id.
    /// </summary>
    public IReadOnlyCollection<string> KnownTransactionIds => _knownTransactionIds;

    /// <summary>
    /// The status an unmatched request gets. Defaults to <c>404</c>, which is what a request no test
    /// expected should look like; a test that must fail loudly can raise it.
    /// </summary>
    public OnePayHttpStub UnmatchedStatus(HttpStatusCode status)
    {
        _unmatchedStatus = status;
        return this;
    }

    /// <summary>
    /// Makes every send throw, standing in for a transport failure (connection refused, DNS, TLS).
    /// </summary>
    public OnePayHttpStub WhenSendThrows(Exception failure)
    {
        _sendFailure = failure;
        return this;
    }

    /// <summary>Scripts a response for a request the given predicate accepts.</summary>
    public OnePayHttpStub When(
        Func<Captured, bool> predicate, Func<Captured, HttpResponseMessage> script)
    {
        ArgumentNullException.ThrowIfNull(predicate);
        ArgumentNullException.ThrowIfNull(script);

        _scripts.Add(captured => predicate(captured) ? script(captured) : null);

        return this;
    }

    /// <summary>Scripts a response for every request whose path contains <paramref name="pathFragment"/>.</summary>
    public OnePayHttpStub WhenPathContains(
        string pathFragment, Func<Captured, HttpResponseMessage> script)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(pathFragment);
        ArgumentNullException.ThrowIfNull(script);

        _scripts.Add(captured => captured.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase)
            ? script(captured)
            : null);

        return this;
    }

    /// <summary>The most recent request whose path contains <paramref name="pathFragment"/>.</summary>
    public Captured Request(string pathFragment) =>
        Requests.LastOrDefault(r => r.Path.Contains(pathFragment, StringComparison.OrdinalIgnoreCase))
        ?? throw new InvalidOperationException(
            $"OnePayHttpStub received no request whose path contains '{pathFragment}'. "
            + $"Recorded paths: {string.Join(", ", Requests.Select(r => r.Path))}");

    /// <summary>The only request recorded; fails when there is not exactly one.</summary>
    public Captured SingleRequest() =>
        Requests.Count == 1
            ? Requests[0]
            : throw new InvalidOperationException(
                $"OnePayHttpStub expected exactly one request but recorded {Requests.Count}.");

    public static HttpResponseMessage Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        new(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };

    public static HttpResponseMessage Status(HttpStatusCode status) => new(status);

    /// <summary>True when the request's path names a transaction id a scripted create handed out.</summary>
    public bool IsKnownTransaction(Captured request) =>
        _knownTransactionIds.Any(id => request.Path.Contains(id, StringComparison.Ordinal));

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (_sendFailure is not null)
        {
            throw _sendFailure;
        }

        var captured = new Captured(
            Method: request.Method,
            Path: request.RequestUri?.AbsolutePath ?? string.Empty,
            Query: request.RequestUri?.Query ?? string.Empty,
            Body: request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false),
            Headers: request.Headers.ToDictionary(
                header => header.Key,
                header => string.Join(",", header.Value),
                StringComparer.OrdinalIgnoreCase));

        Requests.Add(captured);

        foreach (var script in _scripts)
        {
            var response = script(captured);
            if (response is not null)
            {
                RememberTransaction(captured, response);
                return response;
            }
        }

        return Status(_unmatchedStatus ?? HttpStatusCode.NotFound);
    }

    /// <summary>
    /// Records the transaction id a scripted create handed out, so a later status read for that id
    /// is "known" and any other id is a provider 404.
    /// </summary>
    private void RememberTransaction(Captured request, HttpResponseMessage response)
    {
        if (!request.Is("POST") || response.Content is null || !response.IsSuccessStatusCode)
        {
            return;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(
                response.Content.ReadAsStringAsync().GetAwaiter().GetResult());

            if (document.RootElement.TryGetProperty("data", out var data)
                && data.TryGetProperty("transaction_id", out var id)
                && id.ValueKind == System.Text.Json.JsonValueKind.String
                && id.GetString() is { Length: > 0 } transactionId)
            {
                _knownTransactionIds.Add(transactionId);
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // Not a JSON body: nothing to remember. The scripts decide what a caller sees.
        }
    }

    /// <summary>One request as it went on the wire.</summary>
    public sealed record Captured(
        HttpMethod Method,
        string Path,
        string Query,
        string Body,
        IReadOnlyDictionary<string, string> Headers)
    {
        /// <summary>True when the request method is the given verb, case-insensitively.</summary>
        public bool Is(string method) => Method.Method.Equals(method, StringComparison.OrdinalIgnoreCase);

        /// <summary>The request's body as UTF-8 text; empty when there was none.</summary>
        public string Text() => Body;
    }
}
