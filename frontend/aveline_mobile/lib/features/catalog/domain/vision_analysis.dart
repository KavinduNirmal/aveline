/// Multimodal Vision AI extraction analysis result for a garment image.
class VisionAnalysis {
  const VisionAnalysis({
    required this.category,
    this.detectedColor,
    this.colorHex,
    this.fabric,
    this.style,
    this.pattern,
    this.garmentType,
    this.suggestedItemName,
    this.confidenceScore,
    this.visualAttributes = const <String>[],
    this.summary = '',
    this.description,
    this.stylingNotes,
    this.isFallback = false,
  });

  final String category;
  final String? detectedColor;
  final String? colorHex;
  final String? fabric;
  final String? style;
  final String? pattern;
  final String? garmentType;
  final String? suggestedItemName;
  final double? confidenceScore;
  final List<String> visualAttributes;
  final String summary;
  final String? description;
  final String? stylingNotes;
  final bool isFallback;

  /// Parses backend `ImageAnalysisResultDto` JSON response.
  factory VisionAnalysis.fromJson(Map<String, dynamic> json) {
    final rawColor = json['detectedColor'] ??
        json['primaryColor'] ??
        json['primary_color'] ??
        json['color'];
    final color = (rawColor != null && rawColor.toString().trim().toLowerCase() != 'unknown')
        ? rawColor.toString().trim()
        : null;

    final hex = json['colorHex']?.toString() ?? json['color_hex']?.toString();

    final rawAttrs = json['suggestedKeywords'] ??
        json['visual_attributes'] ??
        json['visualAttributes'];
    final visualAttributes = (rawAttrs is List)
        ? rawAttrs.map((e) => e.toString()).toList()
        : <String>[
            if (color != null && color.isNotEmpty) color,
            if (json['fabric'] != null) json['fabric'].toString(),
            if (json['pattern'] != null) json['pattern'].toString(),
          ];

    final desc = json['description']?.toString() ?? json['summary']?.toString();

    return VisionAnalysis(
      category: json['category']?.toString() ?? 'Sarees',
      detectedColor: color,
      colorHex: hex,
      fabric: json['fabric']?.toString(),
      style: json['style']?.toString(),
      pattern: json['pattern']?.toString(),
      garmentType: json['garmentType']?.toString() ?? json['garment_type']?.toString(),
      suggestedItemName: json['suggestedItemName']?.toString() ?? json['suggested_item_name']?.toString(),
      confidenceScore: (json['confidenceScore'] as num?)?.toDouble(),
      visualAttributes: visualAttributes,
      summary: desc ?? '',
      description: desc,
      stylingNotes: json['stylingNotes']?.toString() ?? json['styling_notes']?.toString(),
      isFallback: json['isFallback'] == true || json['is_fallback'] == true,
    );
  }
}
