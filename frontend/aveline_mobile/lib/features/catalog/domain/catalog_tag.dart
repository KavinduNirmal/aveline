/// A tag a boutique defines for its own pieces, e.g. `Bridal` or `New in`.
///
/// Tags are per-shop: every boutique curates its own vocabulary, so the tag row
/// on the Catalog shows this shop's list rather than a shared set.
class CatalogTag {
  const CatalogTag({required this.id, required this.label});

  /// Stable identity, used for selection and as the future API key.
  final String id;

  /// What the associate sees on the pill.
  final String label;
}
