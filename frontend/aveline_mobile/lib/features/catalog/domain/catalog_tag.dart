/// A tag a boutique defines for its own pieces, e.g. `Bridal` or `New in`.
///
/// Tags are per-shop: every boutique curates its own vocabulary, so the tag row
/// on the Catalog shows this shop's list rather than a shared set.
class CatalogTag {
  const CatalogTag({
    required this.id,
    required this.label,
    this.slug,
    this.colorHex,
    this.sortOrder = 0,
    this.isArchived = false,
    this.itemCount = 0,
  });

  /// Stable identity, used for selection and API queries (slug or GUID).
  final String id;

  /// What the associate sees on the pill.
  final String label;

  /// Unique slug identifier.
  final String? slug;

  /// Optional luxury color hex accent (e.g. `#D4AF37`).
  final String? colorHex;

  final int sortOrder;
  final bool isArchived;
  final int itemCount;

  factory CatalogTag.fromJson(Map<String, dynamic> json) {
    final slug = json['slug'] as String? ?? '';
    final rawId = (json['id'] as String?) ?? slug;
    final label = json['label'] as String? ?? (slug.isNotEmpty ? slug : rawId);
    // Use slug as primary filter key if available, otherwise rawId
    final idKey = slug.isNotEmpty ? slug : rawId;

    return CatalogTag(
      id: idKey,
      label: label,
      slug: slug.isNotEmpty ? slug : null,
      colorHex: json['colorHex'] as String?,
      sortOrder: (json['sortOrder'] as num?)?.toInt() ?? 0,
      isArchived: json['isArchived'] as bool? ?? false,
      itemCount: (json['itemCount'] as num?)?.toInt() ?? 0,
    );
  }

  Map<String, dynamic> toJson() => {
    'id': id,
    'slug': slug ?? id,
    'label': label,
    'colorHex': colorHex,
    'sortOrder': sortOrder,
    'isArchived': isArchived,
    'itemCount': itemCount,
  };

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is CatalogTag &&
          runtimeType == other.runtimeType &&
          id == other.id;

  @override
  int get hashCode => id.hashCode;
}
