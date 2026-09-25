/// Payload for recording an in-store counter sale against a catalog piece.
class RecordSalePayload {
  const RecordSalePayload({
    required this.quantity,
    required this.unitPrice,
    this.customerId,
    this.note,
  });

  /// The number of units sold (must be >= 1 and <= on-hand stock).
  final int quantity;

  /// The final agreed unit price on the counter (must be > 0).
  final double unitPrice;

  /// Optional ID of the client purchasing the piece.
  final String? customerId;

  /// Optional note explaining client preferences, alterations, or discounts.
  final String? note;

  Map<String, dynamic> toJson() => {
        'quantity': quantity,
        'unitPrice': unitPrice,
        if (customerId != null && customerId!.trim().isNotEmpty)
          'customerId': customerId!.trim(),
        if (note != null && note!.trim().isNotEmpty) 'note': note!.trim(),
      };
}
