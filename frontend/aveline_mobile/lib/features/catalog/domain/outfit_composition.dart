import 'package:flutter/foundation.dart';
import 'outfit_item.dart';

/// A curated luxury lookbook / ensemble composed of multiple coordinated catalog pieces.
@immutable
class OutfitComposition {
  const OutfitComposition({
    required this.id,
    required this.name,
    required this.occasion,
    required this.totalPrice,
    required this.styleNotes,
    this.heroImageUrl = '',
    this.createdAtUtc,
    this.items = const [],
    this.organizationId,
  });

  /// The unique identifier of this composed lookbook.
  final String id;

  /// The editorial title of the ensemble (e.g. "Royal Sangeet Emerald Look").
  final String name;

  /// The ceremonial occasion or theme (e.g. "Sangeet & Reception", "Bridal Heirloom").
  final String occasion;

  /// The aggregate retail price of all pieces in the ensemble.
  final double totalPrice;

  /// Elle AI styling narrative, drape recommendations, and boutique notes.
  final String styleNotes;

  /// The primary visual thumbnail representing this ensemble.
  final String heroImageUrl;

  /// When this lookbook was created.
  final DateTime? createdAtUtc;

  /// The individual pieces coordinated in this look.
  final List<OutfitItem> items;

  /// The boutique organization ID.
  final String? organizationId;

  /// Effective primary image URL, resolving from [heroImageUrl] or the first item's image.
  String get effectiveHeroImageUrl {
    if (heroImageUrl.trim().isNotEmpty) return heroImageUrl;
    if (items.isNotEmpty && items.first.imageUrl.trim().isNotEmpty) {
      return items.first.imageUrl;
    }
    return '';
  }

  /// Parses a backend JSON object into an [OutfitComposition].
  factory OutfitComposition.fromJson(Map<String, dynamic> json) {
    final rawItems = json['items'] ?? json['outfitItems'];
    final itemsList = <OutfitItem>[];
    if (rawItems is List) {
      for (final it in rawItems) {
        if (it is Map<String, dynamic>) {
          itemsList.add(OutfitItem.fromJson(it));
        }
      }
    }

    final rawHero = json['heroImageUrl'] ?? json['imageUrl'] ?? '';
    final rawCreated = json['createdAt'] ?? json['createdAtUtc'];
    DateTime? createdDate;
    if (rawCreated != null) {
      createdDate = DateTime.tryParse(rawCreated.toString());
    }

    return OutfitComposition(
      id: json['id']?.toString() ?? '',
      name: json['name']?.toString() ?? '',
      occasion: json['occasion']?.toString() ?? 'Ceremonial & Evening',
      totalPrice: _parsePrice(json['totalPrice']),
      styleNotes: (json['styleNotes'] ?? json['notes'] ?? json['description'])?.toString() ?? '',
      heroImageUrl: rawHero.toString(),
      createdAtUtc: createdDate,
      items: List.unmodifiable(itemsList),
      organizationId: json['organizationId']?.toString(),
    );
  }

  /// Serializes to a JSON map.
  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'occasion': occasion,
      'totalPrice': totalPrice,
      'styleNotes': styleNotes,
      'heroImageUrl': heroImageUrl,
      if (createdAtUtc != null) 'createdAtUtc': createdAtUtc!.toIso8601String(),
      'items': items.map((i) => i.toJson()).toList(),
      if (organizationId != null) 'organizationId': organizationId,
    };
  }

  OutfitComposition copyWith({
    String? id,
    String? name,
    String? occasion,
    double? totalPrice,
    String? styleNotes,
    String? heroImageUrl,
    DateTime? createdAtUtc,
    List<OutfitItem>? items,
    String? organizationId,
  }) {
    return OutfitComposition(
      id: id ?? this.id,
      name: name ?? this.name,
      occasion: occasion ?? this.occasion,
      totalPrice: totalPrice ?? this.totalPrice,
      styleNotes: styleNotes ?? this.styleNotes,
      heroImageUrl: heroImageUrl ?? this.heroImageUrl,
      createdAtUtc: createdAtUtc ?? this.createdAtUtc,
      items: items ?? this.items,
      organizationId: organizationId ?? this.organizationId,
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
      other is OutfitComposition &&
          runtimeType == other.runtimeType &&
          id == other.id &&
          name == other.name &&
          occasion == other.occasion &&
          totalPrice == other.totalPrice &&
          styleNotes == other.styleNotes &&
          heroImageUrl == other.heroImageUrl &&
          listEquals(items, other.items) &&
          organizationId == other.organizationId;

  @override
  int get hashCode => Object.hash(
        id,
        name,
        occasion,
        totalPrice,
        styleNotes,
        heroImageUrl,
        Object.hashAll(items),
        organizationId,
      );
}
