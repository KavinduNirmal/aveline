import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/delete_product_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final testPiece = CatalogProduct(
    id: 'piece-to-delete',
    organizationId: 'org-1',
    name: 'Vintage Embroidered Shawl',
    category: 'Accessories',
    sku: 'AVL-ACC-99',
    color: 'Midnight Blue',
    sizes: const ['One Size'],
    price: 32000.0,
    cost: 14000.0,
    quantity: 2,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 24),
  );

  Widget buildTestWidget({
    required CatalogProduct piece,
    required DemoCatalogProductRepository repository,
    void Function(bool?)? onResult,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => Center(
            child: ElevatedButton(
              key: const Key('open_delete_sheet_btn'),
              onPressed: () async {
                final res = await DeleteProductSheet.show(
                  context,
                  piece: piece,
                  repository: repository,
                );
                onResult?.call(res);
              },
              child: const Text('Delete Piece'),
            ),
          ),
        ),
      ),
    );
  }

  group('DeleteProductSheet Widget Tests', () {
    testWidgets('renders piece information and warning message', (tester) async {
      final repo = DemoCatalogProductRepository();
      await tester.pumpWidget(buildTestWidget(piece: testPiece, repository: repo));

      await tester.tap(find.byKey(const Key('open_delete_sheet_btn')));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.text('Delete Catalog Piece'), findsOneWidget);
      expect(find.text('Vintage Embroidered Shawl'), findsOneWidget);
      expect(find.text('AVL-ACC-99 · 2 in stock'), findsOneWidget);
      expect(find.text('Rs 32,000'), findsOneWidget);
      expect(find.byKey(const Key('delete_piece_confirm_btn')), findsOneWidget);
      expect(find.byKey(const Key('delete_piece_cancel_btn')), findsOneWidget);
    });

    testWidgets('cancels deletion when Cancel button is pressed', (tester) async {
      final repo = DemoCatalogProductRepository();
      bool? result;
      await tester.pumpWidget(
        buildTestWidget(
          piece: testPiece,
          repository: repo,
          onResult: (r) => result = r,
        ),
      );

      await tester.tap(find.byKey(const Key('open_delete_sheet_btn')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('delete_piece_cancel_btn')));
      await tester.pumpAndSettle();

      expect(result, isFalse);
      expect(find.text('Delete Catalog Piece'), findsNothing);
    });

    testWidgets('executes deleteProduct on confirm and pops with true', (tester) async {
      final repo = DemoCatalogProductRepository();
      bool? result;
      await tester.pumpWidget(
        buildTestWidget(
          piece: testPiece,
          repository: repo,
          onResult: (r) => result = r,
        ),
      );

      await tester.tap(find.byKey(const Key('open_delete_sheet_btn')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('delete_piece_confirm_btn')));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(result, isTrue);
      expect(find.text('Delete Catalog Piece'), findsNothing);
    });
  });
}
