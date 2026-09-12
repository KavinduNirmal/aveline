using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
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
        _apiKey = configuration["Vision:ApiKey"]
            ?? configuration["Vision__ApiKey"]
            ?? configuration["VISION_API_KEY"]
            ?? configuration["OpenAI:ApiKey"];
        _model = configuration["Vision:Model"]
            ?? configuration["Vision__Model"]
            ?? configuration["VISION_MODEL"]
            ?? "gpt-4o-mini";
        _logger = logger;
        _usageTracker = usageTracker;
    }

    public async Task<ImageAnalysisResultDto> AnalyzeAsync(
        string imageUrl,
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(imageUrl))
        {
            throw new ArgumentException("Image URL must not be empty.", nameof(imageUrl));
        }

        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _logger.LogInformation("Vision API key not configured; using deterministic fallback image analysis.");
            return GenerateDeterministicAnalysis(imageUrl);
        }

        try
        {
            var prompt = "You are a haute couture luxury boutique AI stylist. Analyze this garment/clothing image with high aesthetic fidelity. " +
                         "Return a JSON object with properties: " +
                         "category (string e.g. Saree, Lehenga, Gown, Kurta, Dress, Outerwear), " +
                         "primary_color (string - authentic descriptive color name e.g. 'Emerald Green', 'Deep Crimson', 'Dusty Rose', 'Midnight Blue'), " +
                         "color_hex (string - exact 6-character hex code sampled from dominant fabric pixels e.g. '#0F5132'), " +
                         "secondary_colors (array of strings for accent/border/embroidery hues), " +
                         "fabric (string - e.g. 'Pure Mulberry Silk', 'Micro Velvet', 'Chanderi', 'Raw Silk', 'Organza'), " +
                         "pattern (string - e.g. 'Gold Zari Brocade', 'French Knot Embroidery', 'Handloom Motif', 'Solid Satin'), " +
                         "style (string - e.g. 'Traditional Heirloom', 'Contemporary Luxe', 'Festive Statement'), " +
                         "description (string - 2 to 3 sentences of elegant, luxury boutique catalog copy describing silhouette, drape, craftsmanship, and aesthetic vibe), " +
                         "styling_notes (string - curated styling advice with recommended jewelry, footwear, occasion wear, and color pairings), " +
                         "confidence_score (number 0.0-1.0), " +
                         "suggested_keywords (array of strings).";

            var payload = new
            {
                model = _model,
                messages = new object[]
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
                response_format = new { type = "json_object" },
                max_tokens = 750
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, "v1/chat/completions")
            {
                Content = JsonContent.Create(payload)
            };
            request.Headers.TryAddWithoutValidation("Authorization", $"Bearer {_apiKey}");

            var response = await _httpClient.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Vision API returned status {StatusCode}; falling back to deterministic analysis.", response.StatusCode);
                return GenerateDeterministicAnalysis(imageUrl);
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
                        Provider: "openai",
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
                var parsed = JsonSerializer.Deserialize<VisionApiResponse>(choiceContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (parsed != null)
                {
                    var desc = parsed.Description;
                    if (!string.IsNullOrWhiteSpace(parsed.StylingNotes))
                    {
                        desc = string.IsNullOrWhiteSpace(desc)
                            ? parsed.StylingNotes
                            : $"{desc} Styling Notes: {parsed.StylingNotes}";
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
                        Description = desc,
                        StylingNotes = parsed.StylingNotes,
                        ConfidenceScore = parsed.ConfidenceScore > 0 ? parsed.ConfidenceScore : 0.95,
                        SuggestedKeywords = parsed.SuggestedKeywords ?? new List<string>()
                    };
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling external vision API; falling back to deterministic analysis.");
        }

        return GenerateDeterministicAnalysis(imageUrl);
    }

    private static ImageAnalysisResultDto GenerateDeterministicAnalysis(string imageUrl)
    {
        var lower = imageUrl.ToLowerInvariant();
        var category = lower.Contains("saree") ? "saree"
            : lower.Contains("dress") ? "dress"
            : lower.Contains("lehenga") ? "lehenga"
            : lower.Contains("blouse") ? "blouse"
            : lower.Contains("kurta") ? "kurta"
            : "dress";

        var color = lower.Contains("emerald") || lower.Contains("green") ? "emerald"
            : lower.Contains("red") || lower.Contains("crimson") ? "red"
            : lower.Contains("gold") ? "gold"
            : lower.Contains("blue") || lower.Contains("navy") ? "navy"
            : "emerald";

        var hex = color switch
        {
            "emerald" => "#0f5132",
            "red" => "#8b2e42",
            "gold" => "#d4af37",
            "navy" => "#1e293b",
            _ => "#0f5132"
        };

        var fabric = lower.Contains("silk") ? "silk"
            : lower.Contains("cotton") ? "cotton"
            : lower.Contains("velvet") ? "velvet"
            : lower.Contains("chiffon") ? "chiffon"
            : "silk";

        var pattern = lower.Contains("zari") || lower.Contains("embroider") ? "embroidered"
            : lower.Contains("floral") ? "floral"
            : "solid";

        var formattedColor = char.ToUpperInvariant(color[0]) + color[1..];
        var formattedCategory = char.ToUpperInvariant(category[0]) + category[1..];
        var formattedFabric = char.ToUpperInvariant(fabric[0]) + fabric[1..];

        var description = $"Exquisite {formattedColor.ToLowerInvariant()} {formattedCategory.ToLowerInvariant()} crafted from premium {formattedFabric.ToLowerInvariant()} featuring a refined {pattern.ToLowerInvariant()} aesthetic. Designed with timeless boutique elegance, ideal for evening galas and celebratory occasions.";
        var stylingNotes = "Pair with understated gold jewelry, stiletto heels, and a structured minaudière clutch for a polished silhouette.";

        return new ImageAnalysisResultDto
        {
            Category = category,
            PrimaryColor = color,
            ColorHex = hex,
            SecondaryColors = new List<string> { "gold" },
            Pattern = pattern,
            Style = "traditional",
            Fabric = fabric,
            Description = $"{description} Styling: {stylingNotes}",
            StylingNotes = stylingNotes,
            ConfidenceScore = 0.95,
            SuggestedKeywords = new List<string> { category, color, fabric, pattern }
        };
    }

    private sealed class VisionApiResponse
    {
        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("primary_color")]
        public string? PrimaryColor { get; set; }

        [JsonPropertyName("color_hex")]
        public string? ColorHex { get; set; }

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
