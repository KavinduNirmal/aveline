import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_receipt.dart';
import 'package:aveline_mobile/features/catalog/domain/stock_adjustment_mode.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('RecordSalePayload', () {
    test('serializes quantity and unitPrice correctly', () {
      const payload = RecordSalePayload(
        quantity: 2,
        unitPrice: 45000,
        customerId: 'cust-123',
        note: 'VIP Client Wedding Order',
      );

      final json = payload.toJson();
      expect(json['quantity'], 2);
      expect(json['unitPrice'], 45000.0);
      expect(json['customerId'], 'cust-123');
      expect(json['note'], 'VIP Client Wedding Order');
    });

    test('omits empty or null customerId and note from JSON', () {
      const payload = RecordSalePayload(
        quantity: 1,
        unitPrice: 12000,
        customerId: '',
        note: '   ',
      );

      final json = payload.toJson();
      expect(json['quantity'], 1);
      expect(json['unitPrice'], 12000.0);
      expect(json.containsKey('customerId'), isFalse);
      expect(json.containsKey('note'), isFalse);
    });
  });

  group('CatalogSaleReceipt', () {
    test('deserializes JSON and provides accurate financial labels and summaries', () {
      final json = {
        'itemId': 'item-sar-001',
        'itemName': 'Banarasi Brocade Saree',
        'sku': 'AVL-SAR-001',
        'quantitySold': 2,
        'unitPrice': 65000.0,
        'totalAmount': 130000.0,
        'remainingStock': 3,
        'status': 'available',
        'ledgerEntryId': 'tx-ledger-999',
        'recordedAtUtc': '2026-09-24T10:30:00.000Z',
      };

      final receipt = CatalogSaleReceipt.fromJson(json);

      expect(receipt.itemId, 'item-sar-001');
      expect(receipt.itemName, 'Banarasi Brocade Saree');
      expect(receipt.sku, 'AVL-SAR-001');
      expect(receipt.quantitySold, 2);
      expect(receipt.unitPrice, 65000.0);
      expect(receipt.totalAmount, 130000.0);
      expect(receipt.remainingStock, 3);
      expect(receipt.status, CatalogItemStatus.available);
      expect(receipt.ledgerEntryId, 'tx-ledger-999');
      expect(receipt.totalAmountLabel, 'Rs 130,000');
      expect(receipt.unitPriceLabel, 'Rs 65,000');
      expect(receipt.summary, '2 × Banarasi Brocade Saree · Rs 130,000 · 3 left');
    });

    test('roundtrips toJson and fromJson', () {
      final receipt = CatalogSaleReceipt(
        itemId: 'item-002',
        itemName: 'Kundan Choker',
        sku: 'AVL-JEW-002',
        quantitySold: 1,
        unitPrice: 150000,
        totalAmount: 150000,
        remainingStock: 0,
        status: CatalogItemStatus.soldOut,
        ledgerEntryId: 'ledger-456',
        recordedAtUtc: DateTime.utc(2026, 9, 24, 12, 0),
      );

      final json = receipt.toJson();
      final reconstructed = CatalogSaleReceipt.fromJson(json);

      expect(reconstructed.itemId, receipt.itemId);
      expect(reconstructed.itemName, receipt.itemName);
      expect(reconstructed.remainingStock, 0);
      expect(reconstructed.status, CatalogItemStatus.soldOut);
      expect(reconstructed.totalAmount, 150000);
    });
  });

  group('StockAdjustmentMode', () {
    test('enum contains reduce and outOfStock with descriptions', () {
      expect(StockAdjustmentMode.reduce.label, 'Reduce stock');
      expect(StockAdjustmentMode.outOfStock.label, 'Mark out of stock');
    });
  });
}
