import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:flutter_test/flutter_test.dart';

CatalogProduct _product({
  double price = 24500,
  double cost = 12000,
  int quantity = 4,
  CatalogItemStatus status = CatalogItemStatus.available,
  List<String> sizes = const ['36', '38'],
  int discountMinPercent = 5,
  int discountMaxPercent = 15,
  String? sourcedFrom = 'Varanasi Weavers',
  String? sku = 'AVL-0001-S',
  Map<String, String> metadata = const {'Care': 'Dry clean only'},
}) {
  return CatalogProduct(
    id: 'piece-001',
    organizationId: 'org-1',
    name: 'Handloom Silk Saree',
    category: 'Sarees',
    color: 'Wine',
    sizes: sizes,
    price: price,
    cost: cost,
    quantity: quantity,
    status: status,
    isAvailable: status == CatalogItemStatus.available,
    createdAtUtc: DateTime.utc(2026, 9, 12),
    sourcedFrom: sourcedFrom,
    sku: sku,
    metadata: metadata,
    discountMinPercent: discountMinPercent,
    discountMaxPercent: discountMaxPercent,
  );
}

void main() {
  group('CatalogProduct money', () {
    test('groups the retail price and the cost into thousands', () {
      expect(_product().priceLabel, 'Rs 24,500');
      expect(_product(price: 150500).priceLabel, 'Rs 150,500');
      expect(_product(cost: 950).costLabel, 'Rs 950');
    });

    test('reads the margin in rupees and on the tag', () {
      expect(
        _product(price: 24500, cost: 12000).marginLabel,
        'Rs 12,500 · 51%',
      );
      // A piece bought above its tag price is a negative margin, not a crash.
      expect(_product(price: 1000, cost: 1500).marginPercent, -50);
    });

    test('reads the allowed discount as a range and a floor price', () {
      expect(
        _product(
          discountMinPercent: 5,
          discountMaxPercent: 15,
        ).discountRangeLabel,
        '5% - 15%',
      );
      // 24,500 less the deepest allowed discount.
      expect(_product().floorPriceLabel, 'Rs 20,825');
      expect(_product(discountMaxPercent: 0).discountRangeLabel, 'None');
    });
  });

  group('CatalogProduct status', () {
    test('reads the stock line for every state', () {
      expect(_product(quantity: 12).stockLabel, '12 in stock');
      expect(_product(quantity: 1).stockLabel, 'Only 1 left');
      expect(
        _product(status: CatalogItemStatus.onHold, quantity: 3).stockLabel,
        'On hold · 3',
      );
      expect(
        _product(status: CatalogItemStatus.soldOut).stockLabel,
        'Sold out',
      );
      expect(
        _product(status: CatalogItemStatus.archived).stockLabel,
        'Archived',
      );
    });

    test('flags a low-stock piece only while it is available', () {
      expect(_product(quantity: 2).isLowStock, isTrue);
      expect(_product(quantity: 3).isLowStock, isFalse);
      expect(
        _product(status: CatalogItemStatus.onHold, quantity: 1).isLowStock,
        isFalse,
      );
    });

    test('gates the actions a state still allows', () {
      const available = CatalogItemStatus.available;
      const onHold = CatalogItemStatus.onHold;
      const soldOut = CatalogItemStatus.soldOut;

      expect(available.isHoldable, isTrue);
      expect(onHold.isHoldable, isFalse);
      expect(onHold.isSellable, isTrue);
      expect(soldOut.isSellable, isFalse);
      expect(soldOut.isClosed, isTrue);
      expect(available.isClosed, isFalse);
    });

    test('parses the wire value the API sends', () {
      expect(CatalogItemStatus.parse('on_hold'), CatalogItemStatus.onHold);
      expect(CatalogItemStatus.parse('sold_out'), CatalogItemStatus.soldOut);
      expect(CatalogItemStatus.parse('nonsense'), CatalogItemStatus.available);
      expect(CatalogItemStatus.parse(null), CatalogItemStatus.available);
    });
  });

  group('CatalogProduct copy', () {
    test('falls back when the boutique has not recorded a field', () {
      final piece = _product(sourcedFrom: '   ', sku: null);

      expect(piece.sourceLabel, 'Not recorded');
      expect(piece.skuLabel, 'Not assigned');
    });

    test('lists one size when the piece carries none', () {
      expect(_product(sizes: const []).sizesLabel, 'One size');
      expect(
        _product(sizes: const ['36', '38', '40']).sizesLabel,
        '36 · 38 · 40',
      );
    });

    test('sorts the boutique notes for the table', () {
      final piece = _product(
        metadata: const {
          'Origin': 'India',
          'Care': 'Dry clean',
          'Batch': 'B-1',
        },
      );

      expect(piece.metadataEntries.map((entry) => entry.key), [
        'Batch',
        'Care',
        'Origin',
      ]);
    });

    test('changes status and availability together on copy', () {
      final held = _product().copyWith(
        status: CatalogItemStatus.onHold,
        isAvailable: false,
      );

      expect(held.status, CatalogItemStatus.onHold);
      expect(held.isAvailable, isFalse);
      // Everything else is carried over untouched.
      expect(held.priceLabel, 'Rs 24,500');
      expect(held.name, 'Handloom Silk Saree');
    });
  });
}
