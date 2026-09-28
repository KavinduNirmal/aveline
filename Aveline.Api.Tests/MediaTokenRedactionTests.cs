using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using Aveline.Api.Common.Exceptions;
using Aveline.Api.Configurations;
using FluentAssertions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpsPolicy;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U2.4 (lane L1) — the credential that lives in a path must never reach <em>any</em> observability
/// sink. U2.1 contained the bleed for <c>GET /api/v1/media/{token}</c> by rewriting
/// <see cref="HttpContext.Request.Path"/> in a route-specific middleware; this unit makes the
/// containment central: the authorization audit, the global exception handler and the OpenTelemetry
/// span pipeline all redact through one set of rules.
/// </summary>
/// <remarks>
/// <para>
/// The audit cases drive the real middleware over a real <see cref="HttpContext"/> so the fallback
/// branch — a refusal with <b>no resolved endpoint</b>, which no amount of route-template reading
/// covers — is exercised directly rather than asserted by inspection.
/// </para>
/// <para>
/// The trace case is the one that matters most: the ASP.NET Core instrumentation creates the
/// request activity <b>before any middleware runs</b>, so the token is already on the activity by
/// the time the pipeline executes. The assertion is made against a real exporter attached to the
/// application's own tracer provider, not against a redaction helper in isolation.
/// </para>
/// </remarks>
public class MediaTokenRedactionTests
{
    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string PublicBaseUrl = "https://api.aveline.test";

    /// <summary>The route template the audit line must carry instead of the credential.</summary>
    private const string MediaRouteTemplate = "/api/v1/media/{token}";

    /// <summary>
    /// A token shaped exactly like a real one — three base64url segments, the first a payload
    /// carrying <c>v/p/o/s/e</c> — so the redaction is proven against realistic input rather than a
    /// string that could never be a credential.
    /// </summary>
    private const string RealLookingToken =
        "eyJ2IjoxLCJwIjoiMDE5MjAwMDAtMDAwMC03MDAwLTgwMDAtMDAwMDAwMDAwMDAxIiwibyI6IjAxOTIwMDAwLTAwMDAtNzAwMC04MDAwLTAwMDAwMDAwMDAwMiIsInMiOiJhdHRhY2htZW50LnZpZXciLCJlIjoxNzAwMDAwNjAwfQ"
        + "."
        + "3Q0K1c9b2m4p6r8t0v2x4z6B8d0F2h4J6l8N0p2R4t6V8x0Z2b4D6f8H0j2L4n6P8r0T2v4X6z8A0c2E4g6I8k0M2o4Q6s8U0w2Y4a6c8e0g2i4k6m8o0q2s4u6w8y0";

    // =======================================================================================
    // The audit middleware: matched 401, matched 403, and the unresolved fallback
    // =======================================================================================

    [Fact]
    public async Task Audit_OnA401AtTheMediaRoute_LogsTheTemplateAndNeverTheToken()
    {
        var logs = await RunAuditAsync(
            path: $"/api/v1/media/{RealLookingToken}",
            statusCode: StatusCodes.Status401Unauthorized,
            routeTemplate: MediaRouteTemplate,
            userId: "user_123");

        AssertCredentialIsAbsent(logs);
        logs.Should().Contain(
            message => message.Contains(MediaRouteTemplate, StringComparison.Ordinal),
            "the audit line must name the route, not the credential");
        AssertAuditFieldsSurvive(logs, StatusCodes.Status401Unauthorized, "user_123");
    }

    [Fact]
    public async Task Audit_OnA403AtTheMediaRoute_LogsTheTemplateAndNeverTheToken()
    {
        var logs = await RunAuditAsync(
            path: $"/api/v1/media/{RealLookingToken}",
            statusCode: StatusCodes.Status403Forbidden,
            routeTemplate: MediaRouteTemplate,
            userId: "user_123");

        AssertCredentialIsAbsent(logs);
        logs.Should().Contain(message => message.Contains(MediaRouteTemplate, StringComparison.Ordinal));
        AssertAuditFieldsSurvive(logs, StatusCodes.Status403Forbidden, "user_123");
    }

    [Fact]
    public async Task Audit_WithNoResolvedEndpointOnAMediaShapedPath_RedactsTheCredentialSegment()
    {
        // A refusal can be produced before routing resolves an endpoint (a 404-shaped media path, a
        // pipeline middleware that answers early). The route template is unavailable then, so the
        // registered credential family is the only thing that can redact it.
        var logs = await RunAuditAsync(
            path: $"/api/v1/media/{RealLookingToken}",
            statusCode: StatusCodes.Status403Forbidden,
            routeTemplate: null,
            userId: null,
            noEndpoint: true);

        AssertCredentialIsAbsent(logs);
        logs.Should().Contain(
            message => message.Contains(MediaRouteTemplate, StringComparison.Ordinal),
            "with no endpoint the path is redacted by the credential-family rule, not the template");
    }

    [Fact]
    public async Task Audit_OnANonCredentialPath_StillLogsItsPath()
    {
        var logs = await RunAuditAsync(
            path: "/metrics",
            statusCode: StatusCodes.Status401Unauthorized,
            routeTemplate: "/metrics",
            userId: null);

        logs.Should().Contain(
            message => message.Contains("/metrics", StringComparison.Ordinal),
            "ordinary paths keep their diagnostic value");
    }

    [Fact]
    public async Task Audit_OnAParameterisedNonCredentialRoute_LogsTheTemplateSoTheNextPathCredentialCannotLeak()
    {
        // The point of the central fix: a route that puts a credential in a path parameter and is
        // not registered anywhere still cannot leak it, because a resolved endpoint is logged by
        // its template. This is the "next credential-bearing path" the unit exists for.
        const string credential = "live_secret_4f8a2c";

        var logs = await RunAuditAsync(
            path: $"/api/v1/hooks/{credential}",
            statusCode: StatusCodes.Status401Unauthorized,
            routeTemplate: "/api/v1/hooks/{secret}",
            userId: null);

        logs.Should().NotContain(message => message.Contains(credential, StringComparison.Ordinal));
        logs.Should().Contain(
            message => message.Contains("/api/v1/hooks/{secret}", StringComparison.Ordinal));
    }

    // =======================================================================================
    // The exception handler: the second pre-existing sink that reads the path on the way out
    // =======================================================================================

    [Fact]
    public async Task ExceptionHandler_OnAMediaShapedPath_LogsTheTemplateAndNeverTheToken()
    {
        var logs = new LogCollector();
        using var loggerFactory = new LoggerFactory([logs]);
        var handler = new GlobalExceptionHandler(
            loggerFactory.CreateLogger<GlobalExceptionHandler>(),
            Options.Create(new HstsOptions()));

        var services = new ServiceCollection().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        context.Request.Method = "GET";
        context.Request.Path = $"/api/v1/media/{RealLookingToken}";
        context.SetEndpoint(EndpointFor(MediaRouteTemplate));

        await handler.TryHandleAsync(context, new InvalidOperationException("boom"), CancellationToken.None);

        AssertCredentialIsAbsent(logs.Messages);
        logs.Messages.Should().Contain(message => message.Contains(MediaRouteTemplate, StringComparison.Ordinal));
    }

    // =======================================================================================
    // The trace pipeline: the token is captured before any middleware, so only an exporter-side
    // rewrite can keep it out of Jaeger/OTLP
    // =======================================================================================

    [Fact]
    public async Task ExportedSpans_ForTheMediaTokenRoute_NeverCarryTheToken()
    {
        var exporter = new CapturingSpanExporter();
        var logs = new LogCollector();
        await using var factory = BuildHost(logs, exporter);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/media/{RealLookingToken}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        // The process hosts many ASP.NET Core applications (this test host, the stub identity
        // server, sibling test hosts) and every tracer provider observes the shared ActivitySource,
        // so this waits for *this* request's span — identified by its route template — rather than
        // for any span. The activity also stops just after the response, so a wait is required.
        var exported = await WaitUntilAsync(
            () => exporter.Spans.Any(span => span.Attribute("http.route") == MediaRouteTemplate),
            TimeSpan.FromSeconds(15));
        exported.Should().BeTrue(
            "the ASP.NET Core instrumentation must have produced the media server span this assertion is about");

        var spans = exporter.Spans;

        var offending = spans
            .SelectMany(span => span.Attributes.Select(attribute => (span.Name, attribute.Key, attribute.Value)))
            .Where(pair => pair.Value?.Contains(RealLookingToken, StringComparison.Ordinal) == true)
            .ToArray();
        offending.Should().BeEmpty(
            "no exported span attribute may contain the credential; the instrumentation runs before "
            + "any middleware, so this can only be fixed on the way out: "
            + string.Join(", ", offending.Select(o => $"{o.Name}/{o.Key}")));

        spans.Should().NotContain(span => span.Name.Contains(RealLookingToken, StringComparison.Ordinal));

        // The redaction must actually run: the media span's path attribute is present and carries
        // the template, not merely absent because the instrumentation never wrote it.
        var pathAttributes = spans
            .Where(span => span.Attribute("http.route") == MediaRouteTemplate)
            .SelectMany(span => span.Attributes)
            .Where(attribute => attribute.Key is "url.path" or "http.target")
            .ToArray();
        pathAttributes.Should().NotBeEmpty("the ASP.NET Core instrumentation writes the request path");
        pathAttributes.Should().OnlyContain(attribute =>
            attribute.Value == MediaRouteTemplate);
    }

    [Fact]
    public async Task RefusedMediaRequest_ThroughTheRealPipeline_NeverWritesTheTokenToALogLine()
    {
        var logs = new LogCollector();
        await using var factory = BuildHost(logs, exporter: null);
        using var client = factory.CreateClient();

        var response = await client.GetAsync($"/api/v1/media/{RealLookingToken}");

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        AssertCredentialIsAbsent(logs.Messages);
        logs.Messages.Should().Contain(message => message.Contains(MediaRouteTemplate, StringComparison.Ordinal));
    }

    // =======================================================================================
    // Helpers
    // =======================================================================================

    private static void AssertCredentialIsAbsent(IReadOnlyCollection<string> messages)
    {
        messages.Should().NotContain(
            message => message.Contains(RealLookingToken, StringComparison.Ordinal),
            "the bearer credential must never reach a log line");
    }

    private static void AssertAuditFieldsSurvive(
        IReadOnlyCollection<string> messages, int statusCode, string userId)
    {
        messages.Should().Contain(message =>
            message.Contains($"userId={userId}", StringComparison.Ordinal)
            && message.Contains($"statusCode={statusCode}", StringComparison.Ordinal)
            && message.Contains("method=GET", StringComparison.Ordinal));
    }

    /// <summary>
    /// Runs the real <see cref="LoggingConfiguration.UseAvelineAuthAudit"/> middleware inside a
    /// real routing pipeline and returns every captured log line.
    /// </summary>
    private static async Task<IReadOnlyList<string>> RunAuditAsync(
        string path, int statusCode, string? routeTemplate, string? userId, bool noEndpoint = false)
    {
        var logs = new LogCollector();
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddRouting();
        // Warning and above only: the middleware under test logs at Warning, while the host's own
        // Information-level "Request starting/finished" lines are not this unit's subject (and are
        // filtered the same way in the real app, which is why the end-to-end case below observes no
        // credential).
        builder.Logging.AddProvider(logs);
        builder.Logging.SetMinimumLevel(LogLevel.Warning);

        var app = builder.Build();
        app.UseRouting();

        if (userId is not null)
        {
            app.Use(async (context, next) =>
            {
                context.User = new System.Security.Claims.ClaimsPrincipal(
                    new System.Security.Claims.ClaimsIdentity(
                        [new System.Security.Claims.Claim("sub", userId)], "test"));
                await next(context);
            });
        }

        app.UseAvelineAuthAudit();

        if (noEndpoint)
        {
            // A refusal produced before any endpoint resolves: the route template is not available,
            // so only the credential-family rule can redact the path.
            app.Use(async (context, next) =>
            {
                if (context.GetEndpoint() is null)
                {
                    context.Response.StatusCode = statusCode;
                    return;
                }

                await next(context);
            });
        }

        if (routeTemplate is not null)
        {
            app.MapGet(routeTemplate, context =>
            {
                context.Response.StatusCode = statusCode;
                return Task.CompletedTask;
            });
        }

        await app.StartAsync();
        try
        {
            using var client = app.GetTestClient();
            using var response = await client.GetAsync(path);
            return logs.Messages;
        }
        finally
        {
            await app.StopAsync();
            await app.DisposeAsync();
        }
    }

    private static Endpoint EndpointFor(string routeTemplate) => new RouteEndpoint(
        _ => Task.CompletedTask,
        RoutePatternFactory.Parse(routeTemplate),
        order: 0,
        EndpointMetadataCollection.Empty,
        displayName: null);

    private static async Task<bool> WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition())
            {
                return true;
            }

            await Task.Delay(25);
        }

        return condition();
    }

    private static WebApplicationFactory<Program> BuildHost(
        LogCollector logs, CapturingSpanExporter? exporter)
    {
        return new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", PublicBaseUrl);
                builder.ConfigureLogging(logging => logging.AddProvider(logs));

                if (exporter is not null)
                {
                    builder.ConfigureServices(services => services
                        .AddOpenTelemetry()
                        .WithTracing(tracing => tracing.AddProcessor(
                            new SimpleActivityExportProcessor(exporter))));
                }
            });
    }

    /// <summary>Captures formatted log messages, whatever the category or level.</summary>
    private sealed class LogCollector : ILoggerProvider
    {
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_messages)
                {
                    return [.. _messages];
                }
            }
        }

        public ILogger CreateLogger(string categoryName) => new Collector(this);

        public void Dispose()
        {
        }

        private sealed class Collector(LogCollector owner) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                lock (owner._messages)
                {
                    owner._messages.Add(formatter(state, exception));
                }
            }
        }
    }

    /// <summary>
    /// A real <see cref="BaseExporter{T}"/> attached to the application's tracer provider, so the
    /// assertion sees exactly what OTLP/Jaeger would have received.
    /// </summary>
    private sealed class CapturingSpanExporter : BaseExporter<Activity>
    {
        private readonly ConcurrentQueue<CapturedSpan> _spans = new();

        public IReadOnlyList<CapturedSpan> Spans => [.. _spans];

        public override ExportResult Export(in Batch<Activity> batch)
        {
            foreach (var activity in batch)
            {
                _spans.Enqueue(new CapturedSpan(
                    activity.DisplayName,
                    [.. activity.TagObjects.Select(tag =>
                        new KeyValuePair<string, string?>(tag.Key, tag.Value?.ToString()))]));
            }

            return ExportResult.Success;
        }
    }

    private sealed record CapturedSpan(string Name, IReadOnlyList<KeyValuePair<string, string?>> Attributes)
    {
        public string? Attribute(string key)
            => Attributes.FirstOrDefault(attribute => attribute.Key == key).Value;
    }
}
