using System.Net;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Aveline.Api.Infrastructure.Data;
using Aveline.Api.Modules.Conversations.Models;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aveline.Api.Tests;

/// <summary>
/// U3.2 (lane L4) — the captured-payload round trip across the .NET→Python boundary
/// (strategy §7 check 7, §4 C26; Elle plan C1).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why the two existing <c>analyze-image</c> cases cannot be this gate.</b>
/// <c>VisualEndpointsIntegrationTests.cs:489-534</c> runs its host with <b>no</b> vision key, so
/// <c>VisionService</c> takes the deterministic fallback and its assertions
/// (<c>Category</c>/<c>PrimaryColor</c> non-blank, <c>ConfidenceScore &gt; 0</c>) pass whether or
/// not the field names are right. The gate has to be a <b>capturing</b> assertion on what the API
/// actually emits, never an outcome assertion (strategy §4 C26).
/// </para>
/// <para>
/// <b>What this fixture captures.</b> The real <see cref="IVisionService"/> runs: a vision key is
/// configured and <c>Vision:BaseUrl</c> points at <see cref="CapturingVisionProvider"/>, a real
/// HTTP server on the loopback interface. The reference arm therefore executes in full —
/// resolve → organisation check → analysability check → mint → hand the provider an absolute
/// tokenised URL → serialise the outbound request — and the provider request arrives at the stub
/// as <b>raw bytes</b> written by the real <c>HttpClient</c>, not as a reconstructed object. The
/// body the API returns to Python is captured the same way, as the raw response bytes.
/// </para>
/// <para>
/// <b>The fields asserted, and the Python source that reads each one.</b>
/// <list type="table">
/// <item><term><c>category</c></term><description><c>image_tools.py:56</c>, <c>ImageAttributes.category</c> (required)</description></item>
/// <item><term><c>primary_color</c></term><description><c>image_tools.py:57</c>, <c>ImageAttributes.primary_color</c> (required)</description></item>
/// <item><term><c>secondary_colors</c></term><description><c>image_tools.py:58</c>, <c>ImageAttributes.secondary_colors</c></description></item>
/// <item><term><c>fabric</c></term><description><c>image_tools.py:71</c> (falls back to <c>material</c>)</description></item>
/// <item><term><c>pattern</c></term><description><c>image_tools.py:72</c></description></item>
/// <item><term><c>garmentType</c>, <c>colorHex</c>, <c>suggestedItemName</c>, <c>stylingNotes</c>, <c>confidenceScore</c>, <c>suggestedKeywords</c></term><description>Elle plan §5.7's canonical dual-casing mapping (<c>garmentType ?? garment_type</c>, …); camelCase is the accepted spelling and is also what the web client reads</description></item>
/// </list>
/// </para>
/// <para>
/// <b><c>extra="forbid"</c> is not on this path.</b> <c>ImageAttributes</c> forbids extras, but the
/// raw payload is never constructed into it: <c>image_tools.py:54-75</c> maps a dictionary into the
/// selected fields, so an unrelated key is ignored rather than a 422. What is asserted instead is
/// that the payload carries no key the .NET DTO does not declare, so nothing stray leaks onto the
/// boundary.
/// </para>
/// </remarks>
public class AnalyzeImageCapturedPayloadTests : IAsyncLifetime
{
    private const string InternalKey = "test-internal-analyze-key";

    private const string SigningKey = "AAECAwQFBgcICQoLDA0ODxAREhMUFRYXGBkaGxwdHh8=";

    private const string PublicBaseUrl = "https://api.aveline.test";

    private const string VisionKey = "test-vision-key";

    private const string VisionModel = "deepseek-flash";

    /// <summary>
    /// The provider's answer, distinguishable from the deterministic fallback on every field:
    /// the fallback would answer lowercase <c>emerald</c> at <c>0.95</c> for a saree URL, never
    /// <c>Emerald Green</c> at <c>0.91</c>. If the real call silently degraded, the value
    /// assertions below fail rather than pass.
    /// </summary>
    private const string ModelContent = """
        {"category":"saree","garment_type":"Kanjeevaram Silk Saree","primary_color":"Emerald Green",
         "color_hex":"#0F5132","secondary_colors":["champagne gold","ivory"],"fabric":"Chanderi Silk",
         "pattern":"Chikankari Motif","style":"Traditional Heirloom",
         "suggested_item_name":"Royal Emerald Zari Brocade Silk Saree","description":"Handwoven heirloom drape.",
         "styling_notes":"Pair with heirloom polki.","confidence_score":0.91,
         "suggested_keywords":["saree","emerald","jewel tones"]}
        """;

    private CapturingVisionProvider _provider = null!;

    private WebApplicationFactory<Program> _factory = null!;

    private HttpClient _client = null!;

    public async Task InitializeAsync()
    {
        _provider = new CapturingVisionProvider(ModelContent);

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalKey);
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", PublicBaseUrl);

                // The real VisionService must run: a key takes it past the deterministic fallback
                // and the base URL points its HttpClient at the capturing stub. Nothing here
                // replaces IVisionService, so the bytes captured are the API's own.
                builder.UseSetting("Vision:ApiKey", VisionKey);
                builder.UseSetting("Vision:Model", VisionModel);
                builder.UseSetting("Vision:BaseUrl", _provider.BaseUrl);
            });

        _client = _factory.CreateClient();
        await Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task AnalyzeImage_ReferenceArm_CapturesTheRealOutboundRequestAndLosesNoFieldOnTheWayToPython()
    {
        var attachment = await SeedAnalysableAttachmentAsync();

        var response = await AnalyzeAsync(new
        {
            organizationId = attachment.OrganizationId,
            imageRefKind = "attachment",
            imageRefId = attachment.Id,
        });

        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // ===================================================================================
        // 1. The real outbound request, as raw bytes received by a real HTTP server.
        // ===================================================================================

        _provider.RequestCount.Should().Be(1, "the reference arm reaches the provider through the real HttpClient");
        _provider.LastMethod.Should().Be("POST");
        _provider.LastPath.Should().Be("/v1/chat/completions");
        _provider.LastContentType.Should().StartWith("application/json");
        _provider.LastAuthorization.Should().Be($"Bearer {VisionKey}");

        var outboundText = _provider.LastRequestBodyText;
        outboundText.Should().NotBeNullOrWhiteSpace("the server received the serialised body, not a reconstructed DTO");

        using var outbound = JsonDocument.Parse(_provider.LastRequestBody!);
        outbound.RootElement.GetProperty("model").GetString().Should().Be(VisionModel);

        var content = outbound.RootElement.GetProperty("messages")[0].GetProperty("content");
        content.GetArrayLength().Should().Be(2);
        content[0].GetProperty("type").GetString().Should().Be("text");
        content[1].GetProperty("type").GetString().Should().Be("image_url");

        var providerUrl = content[1].GetProperty("image_url").GetProperty("url").GetString();

        // The URL the provider receives: absolute, https, under Media:PublicBaseUrl, < 8192 chars
        // (strategy §3.5, Elle plan §4.3's DeepSeek external-URL maximum).
        providerUrl.Should().NotBeNullOrWhiteSpace();
        Uri.TryCreate(providerUrl, UriKind.Absolute, out var uri).Should().BeTrue("the provider must be handed an absolute URL");
        uri!.Scheme.Should().Be(Uri.UriSchemeHttps);
        providerUrl.Should().StartWith($"{PublicBaseUrl}/api/v1/media/");
        providerUrl.Length.Should().BeLessThan(8192, "the provider's external-URL maximum is 8192 characters");

        // The captured bytes really carry that URL, byte for byte.
        outboundText.Should().Contain(providerUrl);

        // ===================================================================================
        // 2. The payload the API hands back to Python, as raw response bytes.
        // ===================================================================================

        var payloadBytes = await response.Content.ReadAsByteArrayAsync();
        var payloadText = Encoding.UTF8.GetString(payloadBytes);
        using var payload = JsonDocument.Parse(payloadBytes);
        var root = payload.RootElement;

        // The real path ran: this is the model's answer, never the deterministic fallback.
        root.GetProperty("isFallback").GetBoolean().Should().BeFalse("a configured provider must not silently degrade to the fallback");
        root.GetProperty("primary_color").GetString().Should().Be("Emerald Green");

        // --- Every field the shipped Python reader reads, under the exact name it reads. ---
        // image_tools.py:56-72 reads `category`, `primary_color`, `secondary_colors`, `fabric`,
        // `pattern` (plus `silhouette`/`occasion`/`aesthetic_tags`, which have no .NET producer).
        root.TryGetProperty("category", out _).Should().BeTrue("image_tools.py:56 reads `category`");
        root.TryGetProperty("primary_color", out _).Should().BeTrue("image_tools.py:57 reads `primary_color`");
        root.TryGetProperty("secondary_colors", out _).Should().BeTrue("image_tools.py:58 reads `secondary_colors`");
        root.TryGetProperty("fabric", out _).Should().BeTrue("image_tools.py:71 reads `fabric`");
        root.TryGetProperty("pattern", out _).Should().BeTrue("image_tools.py:72 reads `pattern`");

        // One spelling, not two: the camelCase names Python does not read must be gone
        // (U2.2 established this rule for primary_color; secondaryColors is the same defect).
        root.TryGetProperty("primaryColor", out _).Should().BeFalse("image_tools.py reads `primary_color`, not `primaryColor`");
        root.TryGetProperty("secondaryColors", out _).Should().BeFalse("image_tools.py reads `secondary_colors`, not `secondaryColors`");

        // --- The round trip through a Python-shaped model: no dropped, no renamed field. ---
        var python = PythonImageAttributes.Read(root);

        python.Category.Should().Be("saree", "ImageAttributes.category is required (visual_insight.py:14)");
        python.PrimaryColor.Should().Be("Emerald Green", "ImageAttributes.primary_color is required (visual_insight.py:16)");
        python.SecondaryColors.Should().BeEquivalentTo(["champagne gold", "ivory"]);
        python.Fabric.Should().Be("Chanderi Silk");
        python.Pattern.Should().Be("Chikankari Motif");

        // The required fields must be the model's values, never the reader's defensive default:
        // `Neutral` here would mean `primary_color` was lost on the wire.
        python.PrimaryColor.Should().NotBe("Neutral");
        python.Category.Should().NotBe("Garment");

        // --- The canonical mapping's fields (Elle plan §5.7) survive under an accepted spelling. ---
        root.GetProperty("garmentType").GetString().Should().Be("Kanjeevaram Silk Saree");
        root.GetProperty("colorHex").GetString().Should().Be("#0F5132");
        root.GetProperty("suggestedItemName").GetString().Should().Be("Royal Emerald Zari Brocade Silk Saree");
        root.GetProperty("stylingNotes").GetString().Should().Be("Pair with heirloom polki.");
        root.GetProperty("confidenceScore").GetDouble().Should().Be(0.91);
        root.GetProperty("suggestedKeywords").EnumerateArray().Select(k => k.GetString())
            .Should().Contain(["saree", "emerald", "jewel tones"]);

        // --- Nothing stray: the payload carries exactly the DTO's declared wire keys. ---
        var actualKeys = root.EnumerateObject().Select(p => p.Name).ToHashSet(StringComparer.Ordinal);
        actualKeys.Should().BeEquivalentTo(DeclaredWireKeys(), "no field is dropped and no undeclared field leaks onto the boundary");
    }

    [Fact]
    public async Task AnalyzeImage_WithoutAVisionKey_NeverReachesTheProviderAndIsMarkedAsTheFallback()
    {
        // The falsifier for C26: with no key the deterministic fallback answers and the provider
        // is never contacted, which is exactly why the two existing analyze-image cases cannot
        // tell a right contract from a wrong one.
        await using var factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Clerk:Authority", "http://localhost:0");
                builder.UseSetting("Clerk:RequireHttpsMetadata", "false");
                builder.UseSetting("AgentService:InternalToken", InternalKey);
                builder.UseSetting("Media:SigningKey", SigningKey);
                builder.UseSetting("Media:PublicBaseUrl", PublicBaseUrl);
                builder.UseSetting("Vision:ApiKey", string.Empty);
                builder.UseSetting("Vision:BaseUrl", _provider.BaseUrl);
            });

        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/visual/analyze-image")
        {
            Content = JsonContent.Create(new { organizationId = Guid.NewGuid(), imageUrl = "https://example.com/saree.jpg" }),
        };
        request.Headers.Add("X-Internal-Token", InternalKey);

        var before = _provider.RequestCount;
        var response = await client.SendAsync(request);
        var json = await response.Content.ReadAsStringAsync();

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        json.Should().Contain("\"isFallback\":true", "the no-key host takes the deterministic fallback");
        _provider.RequestCount.Should().Be(before, "the fallback path makes no outbound provider call");
    }

    // =======================================================================================
    // Seeding and helpers
    // =======================================================================================

    private async Task<HttpResponseMessage> AnalyzeAsync(object body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/visual/analyze-image")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Add("X-Internal-Token", InternalKey);
        return await _client.SendAsync(request);
    }

    private async Task<MessageAttachment> SeedAnalysableAttachmentAsync()
    {
        using var scope = _factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var attachment = new MessageAttachment
        {
            Id = Guid.CreateVersion7(),
            OrganizationId = Guid.NewGuid(),
            ConversationId = Guid.NewGuid(),
            StorageProvider = "database",
            StorageKey = null,
            ImageData = [0x89, 0x50, 0x4E, 0x47],
            ContentType = "image/png",
            FileName = "seeded.png",
            SizeBytes = 4,
            Url = "/api/v1/orgs/x/conversations/y/attachments/z",
            CreatedAtUtc = DateTime.UtcNow,
        };

        context.MessageAttachments.Add(attachment);
        await context.SaveChangesAsync();
        return attachment;
    }

    /// <summary>
    /// The JSON names the .NET DTO declares, honouring <see cref="JsonPropertyNameAttribute"/> and
    /// otherwise the ambient camelCase policy (<c>Program.cs</c> adds only a
    /// <c>JsonStringEnumConverter</c> and no naming policy, so an unattributed property is
    /// camelCase).
    /// </summary>
    private static IReadOnlySet<string> DeclaredWireKeys()
        => typeof(ImageAnalysisResultDto)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name
                         ?? JsonNamingPolicy.CamelCase.ConvertName(p.Name))
            .ToHashSet(StringComparer.Ordinal);

    /// <summary>
    /// A C# mirror of the shipped Python reader
    /// (<c>agnet-service/app/tools/inventory/image_tools.py:54-75</c>) and of
    /// <c>ImageAttributes</c> (<c>agnet-service/app/schemas/visual_insight.py:12-24</c>). The
    /// defaults are the Python defaults on purpose: a value that falls back here is a value the
    /// boundary lost.
    /// </summary>
    private sealed class PythonImageAttributes
    {
        public string Category { get; private init; } = "Garment";

        public string PrimaryColor { get; private init; } = "Neutral";

        public IReadOnlyList<string> SecondaryColors { get; private init; } = [];

        public string? Fabric { get; private init; }

        public string? Pattern { get; private init; }

        public static PythonImageAttributes Read(JsonElement payload) => new()
        {
            Category = Text(payload, "category") ?? "Garment",
            PrimaryColor = Text(payload, "primary_color") ?? Text(payload, "color") ?? "Neutral",
            SecondaryColors = List(payload, "secondary_colors"),
            Fabric = Text(payload, "fabric") ?? Text(payload, "material"),
            Pattern = Text(payload, "pattern"),
        };

        private static string? Text(JsonElement payload, string name)
            => payload.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;

        private static IReadOnlyList<string> List(JsonElement payload, string name)
        {
            if (!payload.TryGetProperty(name, out var value))
            {
                return [];
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return (value.GetString() ?? string.Empty)
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            }

            return value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(v => v.GetString() ?? string.Empty).ToArray()
                : [];
        }
    }
}

/// <summary>
/// A real HTTP server on the loopback interface that records the raw bytes of every request it
/// receives. This is the strongest available capture: the bytes are written by the production
/// <c>HttpClient</c> and the production serialiser, and read off a socket, so no object graph is
/// reconstructed and no test double can drift from what the API really sends.
/// </summary>
internal sealed class CapturingVisionProvider : IAsyncDisposable
{
    private readonly HttpListener _listener = new();

    private readonly CancellationTokenSource _cts = new();

    private readonly Task _acceptLoop;

    private readonly byte[] _responseBody;

    private volatile byte[]? _lastRequestBody;

    private volatile string? _lastMethod;

    private volatile string? _lastPath;

    private volatile string? _lastContentType;

    private volatile string? _lastAuthorization;

    private int _requestCount;

    public CapturingVisionProvider(string modelContent)
    {
        var port = FindFreeLoopbackPort();
        BaseUrl = $"http://127.0.0.1:{port}/";
        _listener.Prefixes.Add(BaseUrl);

        var envelope = JsonSerializer.Serialize(new
        {
            choices = new[] { new { message = new { content = modelContent } } },
            usage = new { prompt_tokens = 12, completion_tokens = 7, total_tokens = 19 },
        });
        _responseBody = Encoding.UTF8.GetBytes(envelope);

        _listener.Start();
        _acceptLoop = Task.Run(AcceptLoopAsync);
    }

    public string BaseUrl { get; }

    public int RequestCount => Volatile.Read(ref _requestCount);

    public string? LastMethod => _lastMethod;

    public string? LastPath => _lastPath;

    public string? LastContentType => _lastContentType;

    public string? LastAuthorization => _lastAuthorization;

    public byte[]? LastRequestBody => _lastRequestBody;

    public string LastRequestBodyText => _lastRequestBody is null ? string.Empty : Encoding.UTF8.GetString(_lastRequestBody);

    public async ValueTask DisposeAsync()
    {
        await _cts.CancelAsync();
        _listener.Stop();
        _listener.Close();

        try
        {
            await _acceptLoop;
        }
        catch (Exception)
        {
            // The accept loop is expected to unwind when the listener closes.
        }

        _cts.Dispose();
    }

    private static int FindFreeLoopbackPort()
    {
        using var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync();
            }
            catch (Exception)
            {
                return;
            }

            try
            {
                using var buffer = new MemoryStream();
                await context.Request.InputStream.CopyToAsync(buffer);

                _lastRequestBody = buffer.ToArray();
                _lastMethod = context.Request.HttpMethod;
                _lastPath = context.Request.Url?.AbsolutePath;
                _lastContentType = context.Request.ContentType;
                _lastAuthorization = context.Request.Headers["Authorization"];
                Interlocked.Increment(ref _requestCount);

                context.Response.StatusCode = 200;
                context.Response.ContentType = "application/json";
                context.Response.ContentLength64 = _responseBody.Length;
                await context.Response.OutputStream.WriteAsync(_responseBody);
            }
            catch (Exception)
            {
                // A malformed request is the test's problem, not the stub's; the assertions fail.
            }
            finally
            {
                context.Response.OutputStream.Close();
            }
        }
    }
}
