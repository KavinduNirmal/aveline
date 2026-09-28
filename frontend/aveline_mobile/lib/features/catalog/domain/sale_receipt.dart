import '../../../shared/utils/currency_formatter.dart';
import '../../../shared/utils/date_formatter.dart';
import 'catalog_product.dart';

/// The server-confirmed receipt after recording a counter sale.
///
/// Contains the financial snapshot and ledger journal entry ID created on the
/// backend register.
class CatalogSaleReceipt {
  const CatalogSaleReceipt({
    required this.itemId,
    required this.itemName,
    required this.quantitySold,
    required this.unitPrice,
    required this.totalAmount,
    required this.remainingStock,
    required this.status,
    required this.ledgerEntryId,
    required this.recordedAtUtc,
    this.sku,
  });

  final String itemId;
  final String itemName;
  final String? sku;
  final int quantitySold;
  final double unitPrice;
  final double totalAmount;
  final int remainingStock;
  final CatalogItemStatus status;
  final String ledgerEntryId;
  final DateTime recordedAtUtc;

  /// `Rs 125,000`
  String get totalAmountLabel => rupees(totalAmount);

  /// `Rs 62,500`
  String get unitPriceLabel => rupees(unitPrice);

  /// `12 Sep 2026 14:32`
  String get recordedAtLabel =>
      '${shortDate(recordedAtUtc)} ${clockTime(recordedAtUtc)}';

  /// Summary phrase: `2 × Royal Saree · Rs 125,000 · 3 left`
  String get summary =>
      '$quantitySold × $itemName · $totalAmountLabel · $remainingStock left';

  factory CatalogSaleReceipt.fromJson(Map<String, dynamic> json) {
    return CatalogSaleReceipt(
      itemId: json['itemId'] as String? ?? json['id'] as String? ?? '',
      itemName: json['itemName'] as String? ?? json['name'] as String? ?? 'Piece',
      sku: json['sku'] as String?,
      quantitySold: (json['quantitySold'] as num?)?.toInt() ??
          (json['quantity'] as num?)?.toInt() ??
          1,
      unitPrice: (json['unitPrice'] as num?)?.toDouble() ??
          (json['price'] as num?)?.toDouble() ??
          0.0,
      totalAmount: (json['totalAmount'] as num?)?.toDouble() ??
          (json['total'] as num?)?.toDouble() ??
          0.0,
      remainingStock: (json['remainingStock'] as num?)?.toInt() ??
          (json['stockQuantity'] as num?)?.toInt() ??
          (json['quantity'] as num?)?.toInt() ??
          0,
      status: CatalogItemStatus.parse(json['status'] as String?),
      ledgerEntryId: json['ledgerEntryId'] as String? ??
          json['journalEntryId'] as String? ??
          '',
      recordedAtUtc: json['recordedAtUtc'] != null
          ? DateTime.tryParse(json['recordedAtUtc'] as String)?.toUtc() ??
              DateTime.now().toUtc()
          : DateTime.now().toUtc(),
    );
  }

  Map<String, dynamic> toJson() => {
        'itemId': itemId,
        'itemName': itemName,
        if (sku != null) 'sku': sku,
        'quantitySold': quantitySold,
        'unitPrice': unitPrice,
        'totalAmount': totalAmount,
        'remainingStock': remainingStock,
        'status': status.wireValue,
        'ledgerEntryId': ledgerEntryId,
        'recordedAtUtc': recordedAtUtc.toIso8601String(),
      };
}
