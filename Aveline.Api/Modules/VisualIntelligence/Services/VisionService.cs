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
            ?? configuration["OpenAI:ApiKey"]
            ?? configuration["OPENAI_API_KEY"]
            ?? configuration["Gemini:ApiKey"]
            ?? configuration["GEMINI_API_KEY"]
            ?? configuration["Google:ApiKey"]
            ?? configuration["GOOGLE_API_KEY"]
            ?? Environment.GetEnvironmentVariable("VISION_API_KEY")
            ?? Environment.GetEnvironmentVariable("OPENAI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GEMINI_API_KEY")
            ?? Environment.GetEnvironmentVariable("GOOGLE_API_KEY")
            ?? Environment.GetEnvironmentVariable("LLM_API_KEY");
        _model = configuration["Vision:Model"]
            ?? configuration["Vision__Model"]
            ?? configuration["VISION_MODEL"]
            ?? "gemini-2.0-flash";
        _logger = logger;
        _usageTracker = usageTracker;
    }

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
                _logger.LogWarning("Vision API returned status {StatusCode}; falling back to deterministic analysis.", response.StatusCode);
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
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error calling external vision API; falling back to deterministic analysis.");
        }

        return GenerateDeterministicAnalysis(imageUrl, fileName, contextHint);
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
