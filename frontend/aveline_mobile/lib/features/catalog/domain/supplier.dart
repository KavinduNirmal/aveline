import 'package:flutter/foundation.dart';

/// Wholesale piece provided by a partner atelier for sample sourcing.
@immutable
class SupplierCatalogItem {
  const SupplierCatalogItem({
    required this.id,
    required this.name,
    required this.category,
    required this.fabric,
    required this.wholesalePrice,
    required this.imageUrl,
    this.inStock = true,
  });

  factory SupplierCatalogItem.fromJson(Map<String, dynamic> json) {
    return SupplierCatalogItem(
      id: json['id']?.toString() ?? '',
      name: json['name']?.toString() ?? 'Wholesale Piece',
      category: json['category']?.toString() ?? 'Sarees',
      fabric: json['fabric']?.toString() ?? 'Pure Silk',
      wholesalePrice: (json['wholesalePrice'] as num?)?.toDouble() ?? 0.0,
      imageUrl: json['imageUrl']?.toString() ?? '',
      inStock: json['inStock'] != false,
    );
  }

  final String id;
  final String name;
  final String category;
  final String fabric;
  final double wholesalePrice;
  final String imageUrl;
  final bool inStock;

  String get wholesalePriceLabel => 'Rs ${wholesalePrice.toStringAsFixed(0)}';

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'category': category,
      'fabric': fabric,
      'wholesalePrice': wholesalePrice,
      'imageUrl': imageUrl,
      'inStock': inStock,
    };
  }
}

/// Partner atelier or craft supplier for bespoke sourcing.
@immutable
class Supplier {
  const Supplier({
    required this.id,
    required this.name,
    this.location = 'India',
    this.specialty = 'Bridal & Couture',
    this.rating = 4.8,
    this.contactEmail,
    this.phone,
    this.leadTimeDays = 14,
    this.minimumOrder = 500.0,
    this.isActive = true,
    this.sampleCatalogCount = 0,
    this.catalogItems = const [],
  });

  final String id;
  final String name;
  final String location;
  final String specialty;
  final double rating;
  final String? contactEmail;
  final String? phone;
  final int leadTimeDays;
  final double minimumOrder;
  final bool isActive;
  final int sampleCatalogCount;
  final List<SupplierCatalogItem> catalogItems;

  String get minimumOrderLabel => 'Rs ${minimumOrder.toStringAsFixed(0)}';
  String get leadTimeLabel => '$leadTimeDays days';

  factory Supplier.fromJson(Map<String, dynamic> json) {
    final rawItems = json['catalogItems'];
    final itemsList = rawItems is List
        ? rawItems
            .map((item) => SupplierCatalogItem.fromJson(item as Map<String, dynamic>))
            .toList()
        : const <SupplierCatalogItem>[];

    return Supplier(
      id: json['id']?.toString() ?? '',
      name: json['name']?.toString() ?? 'Partner Atelier',
      location: json['location']?.toString() ?? 'India',
      specialty: json['specialty']?.toString() ?? 'Bridal & Couture',
      rating: (json['rating'] as num?)?.toDouble() ?? 4.8,
      contactEmail: json['contactEmail']?.toString() ?? json['email']?.toString(),
      phone: json['phone']?.toString() ?? json['contactPhone']?.toString(),
      leadTimeDays: (json['leadTimeDays'] as num?)?.toInt() ?? 14,
      minimumOrder: (json['minimumOrder'] as num?)?.toDouble() ?? 500.0,
      isActive: json['isActive'] != false,
      sampleCatalogCount: (json['sampleCatalogCount'] as num?)?.toInt() ?? itemsList.length,
      catalogItems: itemsList,
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'name': name,
      'location': location,
      'specialty': specialty,
      'rating': rating,
      if (contactEmail != null) 'contactEmail': contactEmail,
      if (phone != null) 'phone': phone,
      'leadTimeDays': leadTimeDays,
      'minimumOrder': minimumOrder,
      'isActive': isActive,
      'sampleCatalogCount': sampleCatalogCount,
      'catalogItems': catalogItems.map((e) => e.toJson()).toList(),
    };
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is Supplier &&
          runtimeType == other.runtimeType &&
          id == other.id;

  @override
  int get hashCode => id.hashCode;
}
