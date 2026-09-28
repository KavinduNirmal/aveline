import 'sourcing_status.dart';

/// A bespoke sourcing commission ticket tracked through atelier fulfillment stages.
class SourcingRequest {
  const SourcingRequest({
    required this.id,
    required this.clientName,
    required this.category,
    required this.color,
    required this.itemDescription,
    this.referenceImageUrl,
    required this.supplierId,
    required this.supplierName,
    required this.estimatedCost,
    this.proposedMarkup = 1.0,
    required this.targetPrice,
    this.status = SourcingStatus.pending,
    this.createdAt,
    this.urgency = 'medium',
    this.notes,
  });

  final String id;
  final String clientName;
  final String category;
  final String color;
  final String itemDescription;
  final String? referenceImageUrl;
  final String supplierId;
  final String supplierName;
  final double estimatedCost;
  final double proposedMarkup;
  final double targetPrice;
  final SourcingStatus status;
  final DateTime? createdAt;
  final String urgency;
  final String? notes;

  /// Absolute gross profit margin amount (Target Price - Atelier Cost).
  double get marginAmount => (targetPrice - estimatedCost).clamp(0.0, double.infinity);

  /// Gross markup percentage over atelier cost (+XX%).
  double get marginPercentage {
    if (estimatedCost <= 0) return 0.0;
    final pct = ((targetPrice - estimatedCost) / estimatedCost) * 100.0;
    return pct < 0 ? 0.0 : pct;
  }

  /// True if ticket is currently off the active pipeline in archived storage.
  bool get isArchived => status == SourcingStatus.archived;

  /// True if ticket is actively moving through quotation, approval, ordering or fulfillment.
  bool get isOpen => status != SourcingStatus.archived;

  factory SourcingRequest.fromJson(Map<String, dynamic> json) {
    DateTime? parseDate(dynamic raw) {
      if (raw == null) return null;
      if (raw is DateTime) return raw;
      try {
        return DateTime.parse(raw.toString());
      } catch (_) {
        return null;
      }
    }

    final cost = (json['estimatedCost'] as num?)?.toDouble() ??
        (json['estimated_cost'] as num?)?.toDouble() ??
        (json['cost'] as num?)?.toDouble() ??
        0.0;

    final target = (json['targetPrice'] as num?)?.toDouble() ??
        (json['target_price'] as num?)?.toDouble() ??
        (json['price'] as num?)?.toDouble() ??
        0.0;

    final markup = (json['proposedMarkup'] as num?)?.toDouble() ??
        (json['proposed_markup'] as num?)?.toDouble() ??
        (cost > 0 ? (target - cost) / cost : 1.0);

    return SourcingRequest(
      id: json['id']?.toString() ?? '',
      clientName: json['clientName']?.toString() ??
          json['client_name']?.toString() ??
          json['customerName']?.toString() ??
          'Private Client',
      category: json['category']?.toString() ?? 'Lehengas',
      color: json['color']?.toString() ?? 'Custom Hue',
      itemDescription: json['itemDescription']?.toString() ??
          json['item_description']?.toString() ??
          json['description']?.toString() ??
          'Bespoke commission request.',
      referenceImageUrl: json['referenceImageUrl']?.toString() ??
          json['reference_image_url']?.toString() ??
          json['imageUrl']?.toString() ??
          json['image_url']?.toString(),
      supplierId: json['supplierId']?.toString() ??
          json['supplier_id']?.toString() ??
          'sup-1',
      supplierName: json['supplierName']?.toString() ??
          json['supplier_name']?.toString() ??
          'Partner Atelier',
      estimatedCost: cost,
      proposedMarkup: markup,
      targetPrice: target,
      status: SourcingStatus.fromWire(json['status']?.toString()),
      createdAt: parseDate(json['createdAt'] ?? json['created_at']),
      urgency: json['urgency']?.toString() ?? 'medium',
      notes: json['notes']?.toString() ?? json['stylistNotes']?.toString(),
    );
  }

  Map<String, dynamic> toJson() {
    return {
      'id': id,
      'clientName': clientName,
      'category': category,
      'color': color,
      'itemDescription': itemDescription,
      if (referenceImageUrl != null) 'referenceImageUrl': referenceImageUrl,
      'supplierId': supplierId,
      'supplierName': supplierName,
      'estimatedCost': estimatedCost,
      'proposedMarkup': proposedMarkup,
      'targetPrice': targetPrice,
      'status': status.toWire(),
      if (createdAt != null) 'createdAt': createdAt!.toIso8601String(),
      'urgency': urgency,
      if (notes != null) 'notes': notes,
    };
  }

  SourcingRequest copyWith({
    String? id,
    String? clientName,
    String? category,
    String? color,
    String? itemDescription,
    String? referenceImageUrl,
    String? supplierId,
    String? supplierName,
    double? estimatedCost,
    double? proposedMarkup,
    double? targetPrice,
    SourcingStatus? status,
    DateTime? createdAt,
    String? urgency,
    String? notes,
  }) {
    return SourcingRequest(
      id: id ?? this.id,
      clientName: clientName ?? this.clientName,
      category: category ?? this.category,
      color: color ?? this.color,
      itemDescription: itemDescription ?? this.itemDescription,
      referenceImageUrl: referenceImageUrl ?? this.referenceImageUrl,
      supplierId: supplierId ?? this.supplierId,
      supplierName: supplierName ?? this.supplierName,
      estimatedCost: estimatedCost ?? this.estimatedCost,
      proposedMarkup: proposedMarkup ?? this.proposedMarkup,
      targetPrice: targetPrice ?? this.targetPrice,
      status: status ?? this.status,
      createdAt: createdAt ?? this.createdAt,
      urgency: urgency ?? this.urgency,
      notes: notes ?? this.notes,
    );
  }

  @override
  bool operator ==(Object other) =>
      identical(this, other) ||
      other is SourcingRequest &&
          runtimeType == other.runtimeType &&
          id == other.id;

  @override
  int get hashCode => id.hashCode;
}
