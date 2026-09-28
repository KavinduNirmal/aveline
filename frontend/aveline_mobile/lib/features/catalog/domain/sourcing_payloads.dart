import 'sourcing_status.dart';

/// Payload sent to create a bespoke sourcing commission ticket.
class CreateSourcingRequestPayload {
  const CreateSourcingRequestPayload({
    required this.clientName,
    required this.category,
    required this.color,
    required this.description,
    this.referenceImageUrl,
    required this.supplierId,
    this.supplierName = 'Partner Atelier',
    required this.estimatedCost,
    required this.targetPrice,
    this.urgency = 'medium',
    this.customerId,
    this.status = SourcingStatus.pending,
    this.notes,
  });

  final String clientName;
  final String category;
  final String color;
  final String description;
  final String? referenceImageUrl;
  final String supplierId;
  final String supplierName;
  final double estimatedCost;
  final double targetPrice;
  final String urgency;
  final String? customerId;
  final SourcingStatus status;
  final String? notes;

  Map<String, dynamic> toJson({required String organizationId}) {
    final cost = estimatedCost;
    final target = targetPrice;
    final markup = cost > 0 ? (target - cost) / cost : 1.0;

    return {
      'organizationId': organizationId,
      'clientName': clientName,
      'category': category,
      'color': color,
      'description': description,
      'itemDescription': description,
      if (referenceImageUrl != null && referenceImageUrl!.trim().isNotEmpty)
        'referenceImageUrl': referenceImageUrl!.trim(),
      'supplierId': supplierId,
      'supplierName': supplierName,
      'estimatedCost': estimatedCost,
      'targetPrice': targetPrice,
      'proposedMarkup': NumberUtilities.roundTwoDecimals(markup),
      'urgency': urgency,
      'status': status.toWire(),
      if (customerId != null) 'customerId': customerId,
      if (notes != null && notes!.trim().isNotEmpty) 'notes': notes!.trim(),
    };
  }
}

/// Payload sent to patch the status of a sourcing ticket.
class UpdateSourcingStatusPayload {
  const UpdateSourcingStatusPayload({
    required this.status,
    this.notes,
  });

  final SourcingStatus status;
  final String? notes;

  Map<String, dynamic> toJson() {
    return {
      'status': status.toWire(),
      if (notes != null && notes!.trim().isNotEmpty) 'notes': notes!.trim(),
    };
  }
}

abstract final class NumberUtilities {
  static double roundTwoDecimals(double val) {
    return double.parse(val.toStringAsFixed(2));
  }
}
