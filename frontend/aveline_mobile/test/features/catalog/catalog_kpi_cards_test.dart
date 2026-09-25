import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/catalog_kpi_cards.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  CatalogProduct createPiece({
    required String id,
    required String name,
    required double price,
    required int quantity,
  }) {
    return CatalogProduct(
      id: id,
      organizationId: 'org-1',
      name: name,
      category: 'Sarees',
      color: 'Emerald',
      sizes: const ['Free Size'],
      price: price,
      cost: price * 0.5,
      quantity: quantity,
      status: CatalogItemStatus.available,
      isAvailable: true,
      createdAtUtc: DateTime.utc(2026, 9, 24),
    );
  }

  Widget buildTestWidget(List<CatalogProduct> products) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: CatalogKpiCards(products: products),
      ),
    );
  }

  group('CatalogKpiCards Widget Tests', () {
    testWidgets('renders correct counts and valuations for empty inventory', (tester) async {
      await tester.pumpWidget(buildTestWidget([]));

      expect(find.byKey(const Key('catalog_kpi_pieces')), findsOneWidget);
      expect(find.byKey(const Key('catalog_kpi_valuation')), findsOneWidget);
      expect(find.byKey(const Key('catalog_kpi_low_stock')), findsOneWidget);

      expect(find.text('0'), findsNWidgets(2)); // Pieces = 0, Low stock = 0
      expect(find.text('0 units in stock'), findsOneWidget);
      expect(find.text('Rs 0'), findsOneWidget);
      expect(find.text('All well stocked'), findsOneWidget);
    });

    testWidgets('calculates total pieces, units, valuation, and identifies low-stock items', (tester) async {
      final products = [
        createPiece(id: 'p1', name: 'Silk Saree', price: 50000, quantity: 4),
        createPiece(id: 'p2', name: 'Zari Dupatta', price: 25000, quantity: 2), // Low stock by quantity <= 2
        createPiece(id: 'p3', name: 'Velvet Lehenga', price: 100000, quantity: 1), // Low stock by quantity <= 2
      ];

      await tester.pumpWidget(buildTestWidget(products));

      // 3 pieces
      expect(find.text('3'), findsOneWidget);
      // 4 + 2 + 1 = 7 units
      expect(find.text('7 units in stock'), findsOneWidget);
      // Valuation: (50000*4) + (25000*2) + (100000*1) = 200,000 + 50,000 + 100,000 = 350,000 => 3.5L
      expect(find.text('Rs 3.5L'), findsOneWidget);
      // Low stock: p2 (qty 2) + p3 (qty 1) = 2
      expect(find.text('2'), findsOneWidget);
      expect(find.text('<= 2 units left'), findsOneWidget);
    });
  });
}
