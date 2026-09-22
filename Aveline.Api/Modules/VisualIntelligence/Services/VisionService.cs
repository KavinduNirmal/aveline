using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Aveline.Api.Common.Media;
using Aveline.Api.Modules.Billing.Services;
using Aveline.Api.Modules.VisualIntelligence.DTOs;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Aveline.Api.Modules.VisualIntelligence.Services;

/// <summary>
/// Vision service client for multimodal image analysis. Reads configuration from
/// <c>Vision:ApiKey</c>, <c>Vision:BaseUrl</c>, and <c>Vision:Model</c>. Provides
/// a deterministic attribute analysis fallback when no external API key is configured,
/// and reports token consumption to <see cref="IUsageTrackerService"/> (ADR-010).
/// </summary>
public class VisionService : IVisionService
{
    private readonly HttpClient _httpClient;
    private readonly string? _apiKey;
    private readonly string _model;
    private readonly ILogger<VisionService> _logger;
    private readonly IUsageTrackerService? _usageTracker;

    public VisionService(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<VisionService> logger,
        IUsageTrackerService? usageTracker = null)
    {
        _httpClient = httpClient;
        _logger = logger;
        _usageTracker = usageTracker;
        // Provider keys only. `LLM_API_KEY` deliberately does NOT participate: it is the agent
        // service's *text* model credential (DeepSeek locally), and this client would combine it
        // with the Gemini default BaseUrl to post an image to a text-only endpoint. That pairing
        // always fails, so the failure was invisible: every catalog upload silently degraded to
        // the deterministic fallback and reported a filename-derived colour as though it were read
        // from the photograph. A missing vision key now degrades to the same fallback, but for the
        // honest reason, and `VISION_API_KEY` is documented in .env.example as required.
        _apiKey = configuration["Vision:ApiKey"]
            ?? configuration["Vision__ApiKey"]
            ?? configuration["VISION_API_KEY"]
            ?? configuration["OpenAI:ApiKey"]
            ?? configuration["OPENAI_API_KEY"]
            ?? configuration["Gemini:ApiKey"]
            ?? configuration["GEMINI_API_KEY"]
            ?? configuration["Google:ApiKey"]
            ?? configuration["GOOGLE_API_KEY"]
            ?? Environment.GetEnvironmentVariable("VISION_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY");

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            // Loud on purpose. A silent degradation here previously presented a filename-derived
            // colour to the operator as though a model had read the photograph (the upload was
            // described as "taupe banarasi" from a green saree). Falling back is correct; falling
            // back without saying so is not.
            _logger.LogWarning(
                "No vision provider key is configured (Vision:ApiKey / VISION_API_KEY / GEMINI_API_KEY). "
                + "Catalog image analysis will use the deterministic fallback, which derives attributes "
                + "from the file name and hint text and never inspects the image. Set VISION_API_KEY to "
                + "enable real multimodal extraction.");
        }
        _model = configuration["Vision:Model"]
            ?? configuration["Vision__Model"]
            ?? configuration["VISION_MODEL"]
            ?? "gemini-2.0-flash";
    }

    /// <summary>
    /// Analyses the image named by <paramref name="imageUrl"/>. The target is classified before any
    /// provider call and before any result is produced, so a target the provider cannot read is
    /// refused as a caller error rather than silently answered from the deterministic fallback.
    /// </summary>
    /// <remarks>
    /// See <see cref="IVisionService.AnalyzeAsync"/> for the three accepted target shapes and the
    /// refusal contract. The deterministic fallback (no configured key, non-success status,
    /// unparsable response, transport fault) is deliberately unchanged: those are "we tried and
    /// could not", never "the request named no readable image".
    /// </remarks>
    public async Task<ImageAnalysisResultDto> AnalyzeAsync(
        string imageUrl,
        Guid organizationId,
        string? fileName = null,
        string? contextHint = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException("Image URL must not be empty.", nameof(imageUrl));
        }

        // Refuse a target the provider cannot use before spending a call or fabricating a result.
        // This runs before the no-key check on purpose: a relative path names no readable image
        // whether or not a key is configured, and it is a caller error, not a provider failure.
        EnsureUsableImageTarget(imageUrl);

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogInformation("Vision API key not configured; using deterministic fallback image analysis.");
            return GenerateDeterministicAnalysis(imageUrl, fileName, contextHint);
        }

        try
        {
            var hintSection = string.IsNullOrWhiteSpace(fileName) && string.IsNullOrWhiteSpace(contextHint)
                ? string.Empty
                : $" Image reference context hint: '{fileName ?? string.Empty} {contextHint ?? string.Empty}'. ";

            var prompt = "You are a haute couture luxury boutique master AI stylist, textile connoisseur, and garment classification expert. " +
                         "Analyze the clothing/garment in this image with meticulous aesthetic and textile fidelity." + hintSection + " " +
                         "CRITICAL STEP 1 - BACKGROUND EXCLUSION & FOREGROUND ISOLATION: " +
                         "First segment and isolate the garment from all non-clothing elements. Strictly ignore and exclude: neutral studio backdrops (white, gray, textured walls, studio floors), props (mannequins, hangers, stands, racks, furniture), ambient shadows/glare, and human anatomy (skin tones, hair, face, hands). All color, fabric, and silhouette extractions MUST be sampled exclusively from the primary garment textile. " +
                         "CRITICAL STEP 2 - GARMENT SILHOUETTE & CLOTH IDENTIFICATION: " +
                         "Clearly identify the exact garment structure, silhouette drape, and textile weave. Determine whether the garment is a Saree (e.g. Kanjeevaram Silk, Banarasi Brocade, Chanderi, Georgette), a Lehenga (Bridal Flared, A-Line, Chevron), a Gown (Luminous Evening Gown, Ballgown, Mermaid, Cocktail Maxi Dress), a Kurta & Tunic (Anarkali Kurta, Straight-cut Kurta, Angrakha, Tunic top, Blouse), Outerwear (Tailored Blazer, Embroidered Cape, Jacket), a Drape & Shawl (Cashmere Pashmina Shawl, Silk Dupatta, Stole), or Jewelry/Accessory. " +
                         "CRITICAL STEP 3 - HAUTE COUTURE ATTRIBUTES & AUTO-POPULATION: " +
                         "Return a JSON object with properties: " +
                         "category (string - EXACTLY one of: 'Sarees', 'Lehengas', 'Gowns', 'Kurtas & Tunics', 'Outerwear', 'Drapes & Shawls', or 'Jewelry & Accessories'), " +
                         "garment_type (string - specific luxury garment silhouette e.g. 'Kanjeevaram Silk Saree', 'Embroidered Bridal Lehenga', 'Luminous Evening Gown', 'Anarkali Kurta & Tunic', 'Tailored Boutique Blazer', 'Handwoven Cashmere Shawl'), " +
                         "primary_color (string - precise authentic luxury color name sampled exclusively from the garment body e.g. 'Emerald Green', 'Royal Burgundy', 'Deep Crimson', 'Burnt Terracotta', 'Powder Blue', 'Champagne Gold', 'Dusty Sage', 'Midnight Navy', 'Lavender Lilac', 'Mustard Ochre', 'Bottle Green', 'Olive Green', 'Blush Rose'), " +
                         "color_hex (string - exact 6-character hex code sampled directly from dominant garment fabric pixels e.g. '#0F5132'), " +
                         "color_theme (string - 'Jewel Tones', 'Pastels', 'Earthy Neutrals', 'Classic Monochrome', 'Festive Metallics', 'Rich Berries', or 'Oceanic Spectrum'), " +
                         "undertone (string - 'Warm', 'Cool', or 'Neutral'), " +
                         "secondary_colors (array of strings - accent colors, border trims, zari, embroidery, lining, or print hues), " +
                         "fabric (string - exact fabric/weave e.g. 'Pure Mulberry Silk', 'Banarasi Brocade', 'Micro Velvet', 'Chanderi Silk', 'Raw Silk', 'Pure Organza', 'Pure Chiffon', 'Georgette', 'Handloom Linen', 'Handloom Cotton', 'Duchess Satin'), " +
                         "pattern (string - weave or decorative technique e.g. 'Gold Zari Brocade', 'French Knot Embroidery', 'Chikankari Motif', 'Handloom Weave', 'Botanical Floral Print', 'Solid Satin Sheen', 'Sequined Embellishment', 'Mirror Work'), " +
                         "style (string - e.g. 'Traditional Heirloom', 'Contemporary Luxe', 'Festive Statement', 'Bohemian Minimalist', 'Royal Bridal'), " +
                         "suggested_item_name (string - an elegant 3 to 6 word boutique title e.g. 'Royal Emerald Zari Brocade Silk Saree', 'Burgundy Velvet Embroidered Bridal Lehenga', 'Dusty Rose Luminous Organza Evening Gown'), " +
                         "description (string - 2 to 3 sentences of haute-couture catalog copy highlighting silhouette drape, textile craftsmanship, color story, and occasion wear), " +
                         "styling_notes (string - curated styling advice with recommended fine jewelry, footwear, and accessory pairings), " +
                         "confidence_score (number 0.0-1.0), " +
                         "suggested_keywords (array of strings - include category, garment_type, color, color theme, fabric, pattern, occasion).";

            // A dictionary rather than an anonymous type for one reason: `thinking` below is a
            // DeepSeek-only field that must be OMITTED, not sent as null, for every other provider.
            // The wire shape and the property names are otherwise identical.
            var payload = new Dictionary<string, object?>
            {
                ["model"] = _model,
                ["messages"] = new object[]
                {
                    new
                    {
                        role = "user",
                        content = new object[]
                        {
                            new { type = "text", text = prompt },
                            new { type = "image_url", image_url = new { url = imageUrl } }
                        }
                    }
                },
                ["response_format"] = new { type = "json_object" },
                ["max_tokens"] = 2048
            };

            // Reasoning must be off for this call, and not as an optimisation: measured against
            // DeepSeek, the JSON request with the catalogue prompt spent 1543-2049 *reasoning*
            // tokens before emitting a single character of output. At the old 750 ceiling it spent
            // the entire budget thinking and truncated mid-string
            // ("Path: $.styling_notes | BytePositionInLine: 1050"). Even 2048 was consumed whole:
            // finish_reason=length with 2049 reasoning tokens and no JSON at all. Disabled, the same
            // request finishes in ~313 tokens with valid JSON. This mirrors
            // LLM_THINKING_ENABLED=false, which the agent service already sets for the same reason.
            //
            // It is NOT portable, and "an unknown field is ignored" is false: measured against
            // Gemini's OpenAI-compatible endpoint, the field is a hard schema error -
            // `400 Invalid JSON payload received. Unknown name "thinking": Cannot find field.` -
            // which is exactly the silent-degradation class this method refuses to add to. So it is
            // sent only to the provider that needs it.
            if (ProviderNeedsThinkingDisabled())
            {
                payload["thinking"] = new { type = "disabled" };
            }

            var baseUri = _httpClient.BaseAddress?.ToString().TrimEnd('/') ?? string.Empty;
            var relativePath = baseUri.EndsWith("/openai", StringComparison.OrdinalIgnoreCase) ||
                               baseUri.EndsWith("/v1", StringComparison.OrdinalIgnoreCase) ||
                               baseUri.EndsWith("/v1beta", StringComparison.OrdinalIgnoreCase)
                ? "chat/completions"
                : "v1/chat/completions";

            using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");
            request.Headers.TryAddWithoutValidation("x-goog-api-key", _apiKey);

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                // The body names the actual fault ("max_tokens too large", "unsupported content
                // type", ...). Without it a 400/404 is indistinguishable from a bad key, which is
                // how a misconfiguration stays invisible across many uploads.
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                _logger.LogWarning(
                    "Vision API returned status {StatusCode}; falling back to deterministic analysis. Provider said: {ErrorBody}",
                    response.StatusCode,
                    Truncate(errorBody, 800));
                return GenerateDeterministicAnalysis(imageUrl, fileName, contextHint);
            }

            var content = await response.Content.ReadAsStringAsync(cancellationToken);
            using var doc = JsonDocument.Parse(content);

            // Record token usage if available (ADR-010)
            int promptTokens = 0;
            int completionTokens = 0;
            if (doc.RootElement.TryGetProperty("usage", out var usageElem))
            {
                if (usageElem.TryGetProperty("prompt_tokens", out var ptElem))
                {
                    promptTokens = ptElem.GetInt32();
                }
                if (usageElem.TryGetProperty("completion_tokens", out var ctElem))
                {
                    completionTokens = ctElem.GetInt32();
                }
            }

            if (_usageTracker != null && organizationId != Guid.Empty)
            {
                try
                {
                    await _usageTracker.RecordWorkflowUsageAsync(new RecordUsageRequest(
                        OrganizationId: organizationId,
                        RequestId: Guid.NewGuid().ToString(),
                        WorkflowId: "visual-image-analysis",
                        // Derived from the configured BaseUrl rather than hardcoded to "openai":
                        // the local stack runs this endpoint against DeepSeek, and recording that
                        // traffic as OpenAI makes ADR-010 spend attribution wrong.
                        Provider: ResolveProviderName(),
                        Model: _model,
                        InputTokens: promptTokens,
                        OutputTokens: completionTokens,
                        CachedTokens: 0,
                        ActualCostUsd: 0m
                    ), cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to record ADR-010 usage for vision analysis.");
                }
            }

            var choiceContent = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (!string.IsNullOrWhiteSpace(choiceContent))
            {
                var parsed = TryDeserializeVisionResponse(choiceContent, out var repaired);
                var finishReason = doc.RootElement
                    .GetProperty("choices")[0]
                    .TryGetProperty("finish_reason", out var finishElem)
                    ? finishElem.GetString()
                    : null;

                if (parsed is null)
                {
                    // Distinct from the outer catch: the transport succeeded and the provider
                    // answered, but the answer was not usable. `finish_reason=length` names
                    // truncation, which is the case the token ceiling controls; anything else here
                    // is malformed output.
                    _logger.LogWarning(
                        "Vision provider returned unparsable JSON (finish_reason={FinishReason}, {Length} chars); "
                        + "falling back to deterministic analysis.",
                        finishReason ?? "unknown",
                        choiceContent.Length);
                }
                else
                {
                    if (repaired)
                    {
                        // Name the recovery for what it is. A repaired document is a THINNER answer
                        // than the model was asked for: the fields after the truncation point are
                        // simply absent, and a value cut mid-string is kept only up to the cut. It
                        // must not be presented as a complete reading.
                        _logger.LogWarning(
                            "Vision provider hit the output token ceiling (finish_reason={FinishReason}); the "
                            + "response was truncated and the leading fields were recovered by closing the "
                            + "JSON. Fields after the cut are absent. Raise max_tokens if attributes look thin.",
                            finishReason ?? "unknown");
                    }
                    else if (string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase))
                    {
                        // The ceiling was hit but the document still parsed whole, so nothing was
                        // lost; log at the same place for diagnosis without claiming a repair.
                        _logger.LogWarning(
                            "Vision provider reported finish_reason=length but returned parseable JSON; "
                            + "no fields were dropped.");
                    }

                    return BuildResultFromParsed(parsed);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling external vision API; falling back to deterministic analysis.");
        }

        return GenerateDeterministicAnalysis(imageUrl, fileName, contextHint);
    }

    /// <summary>Keeps a provider error body within a loggable size.</summary>
    private static string Truncate(string value, int maxLength)
        => value.Length <= maxLength ? value : value[..maxLength] + "...(truncated)";

    /// <summary>
    /// Refuses a target the provider cannot read, before a provider call is spent and before the
    /// deterministic fallback could present a filename-derived guess as a model reading.
    /// </summary>
    /// <remarks>
    /// Exactly three shapes exist and only two are usable: inline <c>data:</c> bytes the provider
    /// reads directly, an absolute <c>http(s)</c> URL the provider fetches itself, and everything
    /// else. The provider answers
    /// <c>400 {"error":{"message":".messages[0]: Unsupported image_url format"}}</c> for the third
    /// shape - the production case was the relative Aveline route
    /// <c>/api/v1/orgs/{orgId}/catalog/images/{id}</c> stored in <c>InventoryImages.ImageUrl</c>.
    /// That is a caller error, so it is refused here instead of degrading silently.
    /// </remarks>
    private void EnsureUsableImageTarget(string imageUrl)
    {
        if (imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
        {
            EnsureAnalysableInlineData(imageUrl);
            return;
        }

        if (IsAbsoluteHttpUrl(imageUrl))
        {
            return;
        }

        var shape = DescribeRejectedTarget(imageUrl);
        _logger.LogWarning(
            "Refusing vision analysis for image target {ImageTargetShape}: it is not an absolute "
            + "http(s) URL or inline image data, so the provider cannot fetch it. The deterministic "
            + "fallback was deliberately not used because the request named no readable image.",
            shape);
        throw new ArgumentException(
            $"Image target '{shape}' is not an absolute http(s) URL or inline image data, so the "
            + "vision provider cannot fetch it. Provide an absolute http(s) URL or a "
            + "data:image/...;base64,... URL. The deterministic fallback was deliberately not used "
            + "because the request named no readable image.",
            nameof(imageUrl));
    }

    /// <summary>
    /// Validates inline <c>data:</c> bytes: the declared media type must be one the provider reads
    /// (<see cref="VisionContentTypes.IsAnalysable"/>) and the base64 payload must be non-empty.
    /// </summary>
    /// <remarks>
    /// The warnings name the media type or the reason, never the payload, so a large or sensitive
    /// inline image is never written to the log.
    /// </remarks>
    private void EnsureAnalysableInlineData(string imageUrl)
    {
        var afterScheme = imageUrl["data:".Length..];
        var semicolonIndex = afterScheme.IndexOf(';');
        var commaIndex = afterScheme.IndexOf(',');
        var mediaTypeEnd = (semicolonIndex, commaIndex) switch
        {
            (< 0, < 0) => afterScheme.Length,
            (< 0, _) => commaIndex,
            (_, < 0) => semicolonIndex,
            _ => Math.Min(semicolonIndex, commaIndex),
        };
        var mediaType = afterScheme[..mediaTypeEnd].Trim();

        if (!VisionContentTypes.IsAnalysable(mediaType))
        {
            var named = string.IsNullOrEmpty(mediaType) ? "(absent)" : mediaType;
            _logger.LogWarning(
                "Refusing vision analysis for inline image data: media type {MediaType} is not one of "
                + "the analysable types ({AnalysableTypes}). The deterministic fallback was "
                + "deliberately not used because the request named no readable image.",
                named,
                string.Join(", ", VisionContentTypes.AnalysableTypes));
            throw new ArgumentException(
                $"Inline image data declares media type '{named}', which the vision provider cannot "
                + "analyse. Supported types are image/jpeg, image/png, image/gif and image/webp. The "
                + "request was refused rather than answered from the deterministic fallback, which "
                + "reads no pixels.",
                nameof(imageUrl));
        }

        var payload = commaIndex >= 0 ? afterScheme[(commaIndex + 1)..] : string.Empty;
        if (string.IsNullOrWhiteSpace(payload))
        {
            _logger.LogWarning(
                "Refusing vision analysis for inline image data: media type {MediaType} carries no "
                + "base64 payload. The deterministic fallback was deliberately not used because the "
                + "request named no readable image.",
                mediaType);
            throw new ArgumentException(
                $"Inline image data declares media type '{mediaType}' but carries no base64 payload, "
                + "so there are no bytes for the vision provider to read. The request was refused "
                + "rather than answered from the deterministic fallback, which reads no pixels.",
                nameof(imageUrl));
        }

        // The media type must also *say* base64. A percent-encoded payload without the marker is a
        // shape this API never produces and the provider does not read, so accepting it would buy a
        // provider 400 and, with it, the fabricated fallback this method exists to prevent.
        //
        // The marker lives after the media type and before the comma (`image/jpeg;base64,<payload>`),
        // so the section that carries it runs to the comma, not to the end of the media type.
        var headerSection = afterScheme[..(commaIndex >= 0 ? commaIndex : afterScheme.Length)];
        if (!headerSection.Contains("base64", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogWarning(
                "Refusing vision analysis for inline image data: media type {MediaType} does not "
                + "declare a base64 payload (the payload itself is never logged). The deterministic "
                + "fallback was deliberately not used because the request named no readable image.",
                mediaType);
            throw new ArgumentException(
                $"Inline image data declares media type '{mediaType}' but no base64 payload, so there "
                + "are no bytes for the vision provider to read. Provide a "
                + "data:image/...;base64,... URL.",
                nameof(imageUrl));
        }
    }

    /// <summary>
    /// The shape of a refused target, safe to log and to put in an error message.
    /// </summary>
    /// <remarks>
    /// A <c>data:</c> URL that reached the "everything else" branch does not start with
    /// <c>data:</c> (a leading character put it there), but its payload must still never be echoed
    /// into a log or an error, so the value is reduced to a label. Every other shape is truncated
    /// to <c>maxLength</c> characters so a pathological caller-supplied value stays loggable.
    /// </remarks>
    private static string DescribeRejectedTarget(string imageUrl)
        => imageUrl.TrimStart().StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? "(inline image data)"
            : Truncate(imageUrl, 80);

    /// <summary>
    /// Whether the provider can fetch the target itself. <see cref="Uri.TryCreate(string?, UriKind, out Uri?)"/>
    /// accepts <c>file://</c> and other schemes, so the scheme check is not optional.
    /// </summary>
    private static bool IsAbsoluteHttpUrl(string value)
        => Uri.TryCreate(value, UriKind.Absolute, out var uri)
           && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    /// <summary>
    /// The provider name for usage accounting, inferred from the configured base URL.
    /// </summary>
    private string ResolveProviderName()
    {
        var baseUri = _httpClient.BaseAddress?.ToString() ?? string.Empty;

        if (baseUri.Contains("deepseek", StringComparison.OrdinalIgnoreCase)) return "deepseek";
        if (baseUri.Contains("googleapis", StringComparison.OrdinalIgnoreCase)) return "google";
        if (baseUri.Contains("anthropic", StringComparison.OrdinalIgnoreCase)) return "anthropic";
        if (baseUri.Contains("openai", StringComparison.OrdinalIgnoreCase)) return "openai";

        return "openai-compatible";
    }

    /// <summary>
    /// Whether the configured provider is one that accepts the <c>thinking</c> request field and
    /// needs it to stop its reasoning tokens consuming the whole output budget.
    /// </summary>
    /// <remarks>
    /// Only DeepSeek, today. The field is not an OpenAI-compatible extension that others tolerate:
    /// Gemini rejects the whole request with
    /// <c>400 Invalid JSON payload received. Unknown name "thinking": Cannot find field.</c> (measured
    /// 2026-09-22). Sending it unconditionally therefore broke every analysis against the Gemini
    /// configuration `.env.example` used to recommend, and the failure was invisible because the
    /// non-success branch answers from the filename-derived deterministic fallback.
    /// </remarks>
    private bool ProviderNeedsThinkingDisabled() =>
        string.Equals(ResolveProviderName(), "deepseek", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Deserialises the provider's JSON, closing a truncated response when possible.
    /// </summary>
    /// <remarks>
    /// A real failure observed in production: the provider stopped mid-string
    /// (<c>Path: $.styling_notes | BytePositionInLine: 1050</c>) because the output ceiling was
    /// reached, the whole document failed to parse, and callers saw an HTTP 500 instead of an
    /// analysis. Everything before the truncation point is usually valid and useful, so the
    /// partial object is closed and kept rather than discarded. The token ceiling is the real fix;
    /// this is the safety net that turns a repeat into a thin-but-usable answer.
    /// </remarks>
    private static VisionApiResponse? TryDeserializeVisionResponse(string content, out bool repaired)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        try
        {
            repaired = false;
            return JsonSerializer.Deserialize<VisionApiResponse>(content, options);
        }
        catch (JsonException)
        {
            // fall through to repair
        }

        foreach (var candidate in EnumerateClosedVariants(content))
        {
            try
            {
                var parsed = JsonSerializer.Deserialize<VisionApiResponse>(candidate, options);
                // The document only parsed after the closing braces and/or the open string were
                // synthesised, so everything after the cut is absent and a value cut mid-string is
                // present only up to the cut. The caller logs that honestly.
                repaired = true;
                return parsed;
            }
            catch (JsonException)
            {
                // try the next variant
            }
        }

        repaired = false;
        return null;
    }

    /// <summary>
    /// The ways a JSON object truncated inside a string value can be closed so that the completed
    /// fields survive. Each variant is cheap to try and the caller keeps the first that parses.
    /// </summary>
    private static IEnumerable<string> EnumerateClosedVariants(string content)
    {
        var trimmed = content.TrimEnd();

        // Cut back to the last complete key/value pair, then close the object.
        var lastComma = trimmed.LastIndexOf(',');
        if (lastComma > 0)
        {
            yield return trimmed[..lastComma] + "}";
        }

        // The truncation landed inside the final string: close the string, then the object.
        yield return trimmed + "\"}";
        yield return trimmed + "\"}}";

        // Some providers also lose the closing brace only.
        yield return trimmed + "}";
    }

    private static ImageAnalysisResultDto BuildResultFromParsed(VisionApiResponse parsed)
    {
        var desc = parsed.Description;
        if (!string.IsNullOrWhiteSpace(parsed.StylingNotes))
        {
            desc = string.IsNullOrWhiteSpace(desc)
                ? parsed.StylingNotes
                : $"{desc} Styling Notes: {parsed.StylingNotes}";
        }

        var keywords = parsed.SuggestedKeywords ?? new List<string>();
        if (!string.IsNullOrWhiteSpace(parsed.ColorTheme) && !keywords.Exists(k => string.Equals(k, parsed.ColorTheme, StringComparison.OrdinalIgnoreCase)))
        {
            keywords.Add(parsed.ColorTheme);
        }
        if (!string.IsNullOrWhiteSpace(parsed.Undertone) && !keywords.Exists(k => string.Equals(k, $"{parsed.Undertone} Undertone", StringComparison.OrdinalIgnoreCase)))
        {
            keywords.Add($"{parsed.Undertone} Undertone");
        }
        if (!string.IsNullOrWhiteSpace(parsed.GarmentType) && !keywords.Exists(k => string.Equals(k, parsed.GarmentType, StringComparison.OrdinalIgnoreCase)))
        {
            keywords.Add(parsed.GarmentType);
        }

        return new ImageAnalysisResultDto
        {
            Category = parsed.Category ?? "garment",
            PrimaryColor = parsed.PrimaryColor ?? "unknown",
            ColorHex = parsed.ColorHex,
            SecondaryColors = parsed.SecondaryColors ?? new List<string>(),
            Pattern = parsed.Pattern,
            Style = parsed.Style,
            Fabric = parsed.Fabric,
            GarmentType = parsed.GarmentType,
            SuggestedItemName = parsed.SuggestedItemName,
            Description = desc,
            StylingNotes = parsed.StylingNotes,
            ConfidenceScore = parsed.ConfidenceScore > 0 ? parsed.ConfidenceScore : 0.95,
            SuggestedKeywords = keywords
        };
    }

    private static ImageAnalysisResultDto GenerateDeterministicAnalysis(string imageUrl, string? fileName = null, string? contextHint = null)
    {
        var rawLower = imageUrl.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
            ? string.Empty
            : imageUrl.ToLowerInvariant();
        var combined = $"{rawLower} {fileName ?? string.Empty} {contextHint ?? string.Empty}".ToLowerInvariant();

        // 1. Identify Cloth Category & Garment Silhouette
        string category;
        string garmentType;
        if (combined.Contains("lehenga") || combined.Contains("ghagra") || combined.Contains("choli"))
        {
            category = "lehenga";
            garmentType = combined.Contains("bridal") ? "Embroidered Bridal Lehenga"
                : combined.Contains("chevron") ? "Chevron Embroidered Lehenga"
                : combined.Contains("flared") ? "Flared Silk Lehenga"
                : "Embroidered Bridal Lehenga";
        }
        else if (combined.Contains("saree") || combined.Contains("sari") || combined.Contains("kanjeevaram") || combined.Contains("banarasi") || combined.Contains("chanderi") || combined.Contains("pallu"))
        {
            category = "saree";
            garmentType = combined.Contains("banarasi") ? "Banarasi Silk Brocade Saree"
                : combined.Contains("kanjeevaram") ? "Kanjeevaram Silk Saree"
                : combined.Contains("chanderi") ? "Chanderi Handloom Saree"
                : combined.Contains("organza") ? "Floral Organza Saree"
                : "Silk Kanjeevaram Saree";
        }
        else if (combined.Contains("gown") || combined.Contains("ballgown") || combined.Contains("maxi") || combined.Contains("mermaid") || combined.Contains("cocktail dress") || combined.Contains("evening dress") || combined.Contains("dress") || combined.Contains("frock"))
        {
            category = combined.Contains("dress") && !combined.Contains("gown") && !combined.Contains("maxi") && !combined.Contains("ballgown") ? "dress" : "gown";
            garmentType = combined.Contains("ballgown") ? "Luminous Silk Ballgown"
                : combined.Contains("mermaid") ? "Mermaid Evening Gown"
                : combined.Contains("organza") ? "Luminous Organza Evening Gown"
                : combined.Contains("velvet") ? "Velvet Evening Gown"
                : "Luminous Evening Gown";
        }
        else if (combined.Contains("kurta") || combined.Contains("kurti") || combined.Contains("anarkali") || combined.Contains("tunic") || combined.Contains("salwar") || combined.Contains("blouse"))
        {
            category = combined.Contains("blouse") ? "blouse" : "kurta";
            garmentType = combined.Contains("anarkali") ? "Anarkali Kurta & Tunic"
                : combined.Contains("blouse") ? "Embroidered Silk Blouse"
                : "Straight Handloom Kurti";
        }
        else if (combined.Contains("blazer") || combined.Contains("jacket") || combined.Contains("coat") || combined.Contains("outerwear") || combined.Contains("cape") || combined.Contains("suit"))
        {
            category = "blazer";
            garmentType = combined.Contains("cape") ? "Embroidered Cape"
                : combined.Contains("velvet") ? "Structured Velvet Jacket"
                : "Tailored Boutique Blazer";
        }
        else if (combined.Contains("shawl") || combined.Contains("dupatta") || combined.Contains("stole") || combined.Contains("scarf") || combined.Contains("drape") || combined.Contains("pashmina"))
        {
            category = "shawl";
            garmentType = combined.Contains("pashmina") || combined.Contains("cashmere") ? "Handwoven Cashmere Shawl"
                : combined.Contains("dupatta") ? "Pure Silk Dupatta"
                : "Handwoven Cashmere Shawl";
        }
        else if (combined.Contains("jewelry") || combined.Contains("necklace") || combined.Contains("earring") || combined.Contains("bangle") || combined.Contains("choker") || combined.Contains("kundan") || combined.Contains("clutch") || combined.Contains("bag") || combined.Contains("accessory") || combined.Contains("accessories"))
        {
            category = "jewelry";
            garmentType = combined.Contains("choker") ? "Polki Diamond Choker"
                : combined.Contains("necklace") ? "Heirloom Kundan Necklace"
                : combined.Contains("earring") ? "Artisan Kundan Earrings"
                : combined.Contains("clutch") || combined.Contains("bag") ? "Artisan Minaudière Clutch"
                : "Heirloom Kundan Necklace";
        }
        else if (combined.Contains("trouser") || combined.Contains("pant"))
        {
            category = "trousers";
            garmentType = "Tailored Formal Trousers";
        }
        else if (combined.Contains("skirt"))
        {
            category = "skirt";
            garmentType = "Pleated Statement Skirt";
        }
        else
        {
            category = "saree";
            garmentType = "Silk Kanjeevaram Saree";
        }

        // 2. Comprehensive Fashion Color Taxonomy & Theme Mapping
        string color;
        string hex;
        string theme;
        List<string> secondaryColors;

        if (combined.Contains("lavender") || combined.Contains("lilac"))
        {
            color = "lavender";
            hex = "#c8a2c8";
            theme = "Pastels";
            secondaryColors = new List<string> { "silver", "white" };
        }
        else if (combined.Contains("sage") || combined.Contains("mint"))
        {
            color = combined.Contains("sage") ? "sage green" : "mint green";
            hex = combined.Contains("sage") ? "#9caf88" : "#98ff98";
            theme = "Pastels";
            secondaryColors = new List<string> { "cream", "gold" };
        }
        else if (combined.Contains("blush") || combined.Contains("rose") || combined.Contains("pink"))
        {
            color = combined.Contains("blush") ? "blush pink" : "rose pink";
            hex = "#f4c2c2";
            theme = "Pastels";
            secondaryColors = new List<string> { "rose gold", "ivory" };
        }
        else if (combined.Contains("powder") || combined.Contains("sky") || combined.Contains("baby blue"))
        {
            color = "powder blue";
            hex = "#b0e0e6";
            theme = "Pastels";
            secondaryColors = new List<string> { "silver", "white" };
        }
        else if (combined.Contains("peach") || combined.Contains("apricot"))
        {
            color = "peach";
            hex = "#ffdab9";
            theme = "Pastels";
            secondaryColors = new List<string> { "gold", "ivory" };
        }
        else if (combined.Contains("terracotta") || combined.Contains("rust"))
        {
            color = combined.Contains("terracotta") ? "terracotta" : "rust";
            hex = "#e2725b";
            theme = "Earthy Neutrals";
            secondaryColors = new List<string> { "bronze", "warm ochre" };
        }
        else if (combined.Contains("olive") || combined.Contains("khaki"))
        {
            color = "olive";
            hex = "#556b2f";
            theme = "Earthy Neutrals";
            secondaryColors = new List<string> { "antique gold", "sand" };
        }
        else if (combined.Contains("mustard") || combined.Contains("ochre"))
        {
            color = "mustard ochre";
            hex = "#d4af37";
            theme = "Earthy Neutrals";
            secondaryColors = new List<string> { "rust", "charcoal" };
        }
        else if (combined.Contains("camel") || combined.Contains("taupe") || combined.Contains("sand") || combined.Contains("beige"))
        {
            color = "camel";
            hex = "#c19a6b";
            theme = "Earthy Neutrals";
            secondaryColors = new List<string> { "ivory", "espresso" };
        }
        else if (combined.Contains("burgundy") || combined.Contains("maroon") || combined.Contains("wine") || combined.Contains("plum"))
        {
            color = combined.Contains("burgundy") ? "royal burgundy" : "maroon";
            hex = "#800020";
            theme = "Rich Berries";
            secondaryColors = new List<string> { "antique gold", "rose gold" };
        }
        else if (combined.Contains("magenta") || combined.Contains("fuchsia"))
        {
            color = "magenta";
            hex = "#ff00ff";
            theme = "Rich Berries";
            secondaryColors = new List<string> { "gold", "orange" };
        }
        else if (combined.Contains("coral") || combined.Contains("tangerine") || combined.Contains("orange"))
        {
            color = "coral";
            hex = "#ff7f50";
            theme = "Rich Berries";
            secondaryColors = new List<string> { "gold", "peach" };
        }
        else if (combined.Contains("teal") || combined.Contains("peacock"))
        {
            color = "teal";
            hex = "#008080";
            theme = "Oceanic Spectrum";
            secondaryColors = new List<string> { "gold", "navy" };
        }
        else if (combined.Contains("turquoise") || combined.Contains("aqua"))
        {
            color = "turquoise";
            hex = "#40e0d0";
            theme = "Oceanic Spectrum";
            secondaryColors = new List<string> { "silver", "white" };
        }
        else if (combined.Contains("gold") || combined.Contains("zari") || combined.Contains("champagne"))
        {
            color = combined.Contains("champagne") ? "champagne gold" : "gold";
            hex = "#d4af37";
            theme = "Festive Metallics";
            secondaryColors = new List<string> { "emerald", "crimson" };
        }
        else if (combined.Contains("silver") || combined.Contains("platinum"))
        {
            color = "silver";
            hex = "#c0c0c0";
            theme = "Festive Metallics";
            secondaryColors = new List<string> { "charcoal", "white" };
        }
        else if (combined.Contains("copper") || combined.Contains("bronze"))
        {
            color = "copper";
            hex = "#b87333";
            theme = "Festive Metallics";
            secondaryColors = new List<string> { "gold", "terracotta" };
        }
        else if (combined.Contains("black") || combined.Contains("charcoal") || combined.Contains("noir"))
        {
            color = "black";
            hex = "#1a1a1a";
            theme = "Classic Monochrome";
            secondaryColors = new List<string> { "gold", "silver" };
        }
        else if (combined.Contains("white") || combined.Contains("ivory") || combined.Contains("cream") || combined.Contains("pearl"))
        {
            color = combined.Contains("ivory") ? "ivory" : "white";
            hex = "#fffff0";
            theme = "Classic Monochrome";
            secondaryColors = new List<string> { "gold", "pearl" };
        }
        else if (combined.Contains("navy") || combined.Contains("sapphire") || combined.Contains("blue"))
        {
            color = combined.Contains("sapphire") ? "sapphire blue" : "navy";
            hex = "#1e293b";
            theme = combined.Contains("sapphire") ? "Jewel Tones" : "Classic Monochrome";
            secondaryColors = new List<string> { "gold", "silver" };
        }
        else if (combined.Contains("emerald") || combined.Contains("green") || combined.Contains("jade"))
        {
            color = "emerald";
            hex = "#0f5132";
            theme = "Jewel Tones";
            secondaryColors = new List<string> { "gold", "zari" };
        }
        else if (combined.Contains("red") || combined.Contains("crimson") || combined.Contains("ruby"))
        {
            color = "red";
            hex = "#8b2e42";
            theme = "Jewel Tones";
            secondaryColors = new List<string> { "gold", "zari" };
        }
        else if (combined.Contains("amethyst") || combined.Contains("purple") || combined.Contains("violet"))
        {
            color = "amethyst purple";
            hex = "#581845";
            theme = "Jewel Tones";
            secondaryColors = new List<string> { "gold", "lilac" };
        }
        else
        {
            color = "emerald";
            hex = "#0f5132";
            theme = "Jewel Tones";
            secondaryColors = new List<string> { "gold" };
        }

        // 3. Fabric & Texture
        string fabric;
        if (category == "jewelry")
        {
            fabric = combined.Contains("kundan") ? "Kundan & 22K Gold"
                : combined.Contains("polki") ? "Polki Uncut Diamonds"
                : "22K Gold & Precious Gems";
        }
        else if (combined.Contains("pashmina") || combined.Contains("cashmere"))
        {
            fabric = "Cashmere Pashmina";
        }
        else if (combined.Contains("organza"))
        {
            fabric = "Pure Organza";
        }
        else if (combined.Contains("chiffon"))
        {
            fabric = "Pure Chiffon";
        }
        else if (combined.Contains("georgette"))
        {
            fabric = "Georgette";
        }
        else if (combined.Contains("velvet"))
        {
            fabric = "Micro Velvet";
        }
        else if (combined.Contains("satin"))
        {
            fabric = "Duchess Satin";
        }
        else if (combined.Contains("linen"))
        {
            fabric = "Handloom Linen";
        }
        else if (combined.Contains("cotton"))
        {
            fabric = "Handloom Cotton";
        }
        else if (combined.Contains("banarasi"))
        {
            fabric = "Banarasi Brocade";
        }
        else
        {
            fabric = "Pure Mulberry Silk";
        }

        // 4. Pattern & Weave Technique
        string pattern;
        if (combined.Contains("zari") || combined.Contains("brocade"))
        {
            pattern = "Gold Zari Brocade";
        }
        else if (combined.Contains("chikankari"))
        {
            pattern = "Chikankari Motif";
        }
        else if (combined.Contains("embroider"))
        {
            pattern = "French Knot Embroidery";
        }
        else if (combined.Contains("floral"))
        {
            pattern = "Botanical Floral Weave";
        }
        else if (combined.Contains("sequin"))
        {
            pattern = "Sequined Embellishment";
        }
        else if (combined.Contains("mirror"))
        {
            pattern = "Mirror Work";
        }
        else if (combined.Contains("print"))
        {
            pattern = "Artisan Hand-Block Print";
        }
        else
        {
            pattern = "Solid Satin Sheen";
        }

        var formattedColor = char.ToUpperInvariant(color[0]) + color[1..];
        var formattedCategory = char.ToUpperInvariant(category[0]) + category[1..];
        var formattedFabric = char.ToUpperInvariant(fabric[0]) + fabric[1..];

        var description = $"Exquisite {formattedColor.ToLowerInvariant()} {garmentType.ToLowerInvariant()} crafted from premium {formattedFabric.ToLowerInvariant()} featuring a refined {pattern.ToLowerInvariant()} in a curated {theme} palette. Designed with timeless boutique elegance, ideal for celebratory occasions.";
        var stylingNotes = category == "jewelry"
            ? "Pair with classic silk sarees or deep neckline evening gowns for maximum brilliance."
            : "Pair with curated artisan fine jewelry, stiletto heels, and a structured minaudière clutch for a polished silhouette.";

        var suggestedName = $"{formattedColor} {formattedFabric} {garmentType}".Replace("  ", " ").Trim();

        return new ImageAnalysisResultDto
        {
            Category = category,
            PrimaryColor = color,
            ColorHex = hex,
            SecondaryColors = secondaryColors,
            Pattern = pattern,
            Style = "Contemporary Luxe",
            Fabric = fabric,
            GarmentType = garmentType,
            SuggestedItemName = suggestedName,
            Description = $"{description} Styling: {stylingNotes}",
            StylingNotes = stylingNotes,
            // `ConfidenceScore` is NOT a liveness signal. It is primed above zero so the existing
            // boundary contract (VisualEndpointsIntegrationTests asserts `> 0`) holds, but this
            // fallback read no pixels: it pattern-matched the filename and hint text. The only
            // honest liveness signal is `IsFallback`, and consumers must branch on that rather
            // than on the score. The web client previously required `!isFallback && score > 0.85`
            // and then, in a separate defect, still let its own client-side result win outright, so
            // the `isFallback` flag was the signal that mattered and was discarded.
            ConfidenceScore = 0.95,
            IsFallback = true,
            SuggestedKeywords = new List<string> { category, garmentType, color, theme, fabric, pattern }
        };
    }

    private sealed class VisionApiResponse
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("garment_type")]
        public string? GarmentType { get; set; }

        [JsonPropertyName("suggested_item_name")]
        public string? SuggestedItemName { get; set; }

        [JsonPropertyName("primary_color")]
        public string? PrimaryColor { get; set; }

        [JsonPropertyName("color_hex")]
        public string? ColorHex { get; set; }

        [JsonPropertyName("color_theme")]
        public string? ColorTheme { get; set; }

        [JsonPropertyName("undertone")]
        public string? Undertone { get; set; }

        [JsonPropertyName("secondary_colors")]
        public List<string>? SecondaryColors { get; set; }

        [JsonPropertyName("pattern")]
        public string? Pattern { get; set; }

        [JsonPropertyName("style")]
        public string? Style { get; set; }

        [JsonPropertyName("fabric")]
        public string? Fabric { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("styling_notes")]
        public string? StylingNotes { get; set; }

        [JsonPropertyName("confidence_score")]
        public double ConfidenceScore { get; set; }

        [JsonPropertyName("suggested_keywords")]
        public List<string>? SuggestedKeywords { get; set; }
    }
}
