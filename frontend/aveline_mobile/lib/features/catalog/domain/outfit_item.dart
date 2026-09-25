import 'package:flutter/foundation.dart';

/// A single piece assigned to a styled lookbook / ensemble composition.
@immutable
class OutfitItem {
  const OutfitItem({
    required this.id,
    required this.itemId,
    required this.name,
    required this.category,
    required this.price,
    required this.imageUrl,
    required this.position,
    this.notes,
  });

  /// The unique identifier of this composition slot item.
  final String id;

  /// The foreign key identifier of the catalog inventory item.
  final String itemId;

  /// The display name of the garment or accessory.
  final String name;

  /// The category of the piece (e.g. Sarees, Lehengas, Jewelry).
  final String category;

  /// The retail price of the individual piece.
  final double price;

  /// Image URL for visual thumbnail display.
  final String imageUrl;

  /// The styling position in the ensemble ('top', 'bottom', 'drape', 'accessory', 'footwear').
  final String position;

  /// Optional stylist advice or role notes for this piece.
  final String? notes;

  /// Parses a backend JSON object into an [OutfitItem].
  factory OutfitItem.fromJson(Map<String, dynamic> json) {
    return OutfitItem(
      id: json['id']?.toString() ?? '',
      itemId: (json['itemId'] ?? json['inventoryItemId'] ?? json['id'])?.toString() ?? '',
      name: (json['name'] ?? json['itemName'])?.toString() ?? '',
      category: json['category']?.toString() ?? '',
      price: _parsePrice(json['price']),
      imageUrl: (json['imageUrl'] ?? json['image'])?.toString() ?? '',
      position: json['position']?.toString() ?? 'top',
      notes: json['notes']?.toString(),
    );
  }

  /// Serializes to a JSON map.
  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'itemId': itemId,
      'name': name,
      'category': category,
      'price': price,
      'imageUrl': imageUrl,
      'position': position,
      if (notes != null) 'notes': notes,
    };
  }

  OutfitItem copyWith({
    String? id,
    String? itemId,
    String? name,
    String? category,
    double? price,
    String? imageUrl,
    String? position,
    String? notes,
  }) {
    return OutfitItem(
      id: id ?? this.id,
      itemId: itemId ?? this.itemId,
      name: name ?? this.name,
      category: category ?? this.category,
      price: price ?? this.price,
      imageUrl: imageUrl ?? this.imageUrl,
      position: position ?? this.position,
      notes: notes ?? this.notes,
    );
  }

  static double _parsePrice(dynamic val) {
    if (val == null) return 0.0;
    if (val is num) return val.toDouble();
    return double.tryParse(val.toString()) ?? 0.0;
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is OutfitItem &&
          runtimeType == other.runtimeType &&
          id == other.id &&
          itemId == other.itemId &&
          name == other.name &&
          category == other.category &&
          price == other.price &&
          imageUrl == other.imageUrl &&
          position == other.position &&
          notes == other.notes;

  @override
  int get hashCode => Object.hash(
        id,
        itemId,
        name,
        category,
        price,
        imageUrl,
        position,
        notes,
      );
}
