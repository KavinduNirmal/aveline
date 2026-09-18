import '../../../shared/utils/currency_formatter.dart';
import '../../../shared/utils/date_formatter.dart';

/// Where a piece sits in the boutique's workflow.
///
/// The wire values match `InventoryItemDto.Status`, which the API sends as a
/// bare string.
enum CatalogItemStatus {
  available('available', 'Available'),
  onHold('on_hold', 'On hold'),
  unavailable('unavailable', 'Unavailable'),
  soldOut('sold_out', 'Sold out'),
  archived('archived', 'Archived');

  const CatalogItemStatus(this.wireValue, this.label);

  /// What the API stores and returns.
  final String wireValue;

  /// What the screen prints.
  final String label;

  static CatalogItemStatus parse(String? value) {
    for (final status in values) {
      if (status.wireValue == value) {
        return status;
      }
    }
    return CatalogItemStatus.available;
  }

  /// Whether the piece can still be put on hold for a client.
  bool get isHoldable => this == available;

  /// Whether the piece can still leave the floor.
  bool get isSellable => this == available || this == onHold;

  /// Whether the piece is off the floor, so marking it down again is a no-op.
  bool get isClosed =>
      this == unavailable || this == soldOut || this == archived;
}

/// One piece in the boutique's catalog.
///
/// Mirrors `InventoryItemDto`. `sourcedFrom` and the discount range are not on
/// the DTO yet; the API carries them in [metadata] for now, and they are
/// promoted here so the screen can read them like any other field.
class CatalogProduct {
  const CatalogProduct({
    required this.id,
    required this.organizationId,
    required this.name,
    required this.category,
    required this.color,
    required this.sizes,
    required this.price,
    required this.cost,
    required this.quantity,
    required this.status,
    required this.isAvailable,
    required this.createdAtUtc,
    this.fabric,
    this.style,
    this.imageUrl,
    this.sku,
    this.description,
    this.metadata = const <String, String>{},
    this.deletedAt,
    this.sourcedFrom,
    this.discountMinPercent = 0,
    this.discountMaxPercent = 0,
    this.tags = const <String>{},
  });

  /// `InventoryItemDto.Id`.
  final String id;

  /// `InventoryItemDto.OrgId`.
  final String organizationId;

  /// `InventoryItemDto.ItemName`.
  final String name;

  final String category;

  /// `InventoryItemDto.Color` as the boutique typed it, e.g. `Wine`.
  final String color;

  final List<String> sizes;

  /// The retail price on the tag.
  final double price;

  /// What the boutique paid for the piece.
  final double cost;

  final int quantity;
  final CatalogItemStatus status;

  /// `InventoryItemDto.IsAvailable`, kept alongside [status] because the API
  /// sends both and they can disagree while a sync is in flight.
  final bool isAvailable;

  final DateTime createdAtUtc;

  final String? fabric;
  final String? style;
  final String? imageUrl;
  final String? sku;
  final String? description;
  final Map<String, String> metadata;
  final DateTime? deletedAt;

  /// Where the boutique sourced the piece.
  final String? sourcedFrom;

  /// The discount the boutique allows on this piece, as percentages.
  final int discountMinPercent;
  final int discountMaxPercent;

  /// Ids from the shop's own tag list.
  final Set<String> tags;

  /// `Rs 245,000`.
  String get priceLabel => rupees(price);

  String get costLabel => rupees(cost);

  /// `Rs 100,000 · 41%`: what the piece makes, in rupees and on the tag.
  String get marginLabel {
    final amount = price - cost;
    return '${rupees(amount)} · $marginPercent%';
  }

  /// Gross margin on the retail price.
  int get marginPercent =>
      price <= 0 ? 0 : (((price - cost) / price) * 100).round();

  bool get hasDiscountRange => discountMaxPercent > 0;

  /// `5% - 15%`, or `None` when the boutique allows no discount.
  String get discountRangeLabel =>
      hasDiscountRange ? '$discountMinPercent% - $discountMaxPercent%' : 'None';

  /// The lowest the piece may be sold for once the deepest allowed discount is
  /// applied.
  double get floorPrice => price * (1 - discountMaxPercent / 100);

  String get floorPriceLabel => rupees(floorPrice);

  /// What the piece's line reads in the grid and on the detail header.
  String get stockLabel => switch (status) {
    CatalogItemStatus.available =>
      quantity <= 2 ? 'Only $quantity left' : '$quantity in stock',
    CatalogItemStatus.onHold => 'On hold · $quantity',
    CatalogItemStatus.unavailable => 'Unavailable · $quantity',
    CatalogItemStatus.soldOut => 'Sold out',
    CatalogItemStatus.archived => 'Archived',
  };

  bool get isLowStock =>
      status == CatalogItemStatus.available && quantity > 0 && quantity <= 2;

  /// `36 · 38 · 40`, or `One size` when the piece carries none.
  String get sizesLabel => sizes.isEmpty ? 'One size' : sizes.join(' · ');

  String get sourceLabel => _present(sourcedFrom) ?? 'Not recorded';

  String get skuLabel => _present(sku) ?? 'Not assigned';

  String get fabricLabel => _present(fabric) ?? 'Not recorded';

  String get styleLabel => _present(style) ?? 'Not recorded';

  String get availabilityLabel => isAvailable ? 'Yes' : 'No';

  /// `Added 12 Sep 2026`.
  String get createdLabel => 'Added ${shortDate(createdAtUtc)}';

  /// The boutique's own key/values, in a stable order for the table.
  List<MapEntry<String, String>> get metadataEntries {
    final entries = metadata.entries.toList()
      ..sort((a, b) => a.key.toLowerCase().compareTo(b.key.toLowerCase()));
    return entries;
  }

  /// A copy with the fields the catalog's actions change.
  CatalogProduct copyWith({
    CatalogItemStatus? status,
    bool? isAvailable,
    int? quantity,
  }) {
    return CatalogProduct(
      id: id,
      organizationId: organizationId,
      name: name,
      category: category,
      color: color,
      sizes: sizes,
      price: price,
      cost: cost,
      quantity: quantity ?? this.quantity,
      status: status ?? this.status,
      isAvailable: isAvailable ?? this.isAvailable,
      createdAtUtc: createdAtUtc,
      fabric: fabric,
      style: style,
      imageUrl: imageUrl,
      sku: sku,
      description: description,
      metadata: metadata,
      deletedAt: deletedAt,
      sourcedFrom: sourcedFrom,
      discountMinPercent: discountMinPercent,
      discountMaxPercent: discountMaxPercent,
      tags: tags,
    );
  }

  static String? _present(String? value) {
    final trimmed = value?.trim();
    return (trimmed == null || trimmed.isEmpty) ? null : trimmed;
  }

}
