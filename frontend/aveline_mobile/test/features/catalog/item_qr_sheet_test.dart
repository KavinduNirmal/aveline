import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/item_qr_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final testPiece = CatalogProduct(
    id: 'piece-100',
    organizationId: 'org-aveline',
    name: 'Imperial Organza Anarkali',
    category: 'Suits',
    color: 'Dusty Rose',
    fabric: 'Pure Organza',
    sku: 'AVL-SUIT-100',
    sizes: const ['M', 'L'],
    price: 48000.0,
    cost: 22000.0,
    quantity: 5,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 24),
  );

  Widget buildTestWidget({
    required CatalogProduct piece,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => Center(
            child: ElevatedButton(
              key: const Key('open_qr_sheet_button'),
              onPressed: () {
                ItemQrSheet.show(
                  context,
                  piece: piece,
                  organizationId: 'org-aveline',
                  organizationSlug: 'aveline-couture',
                );
              },
              child: const Text('Open QR'),
            ),
          ),
        ),
      ),
    );
  }

  group('ItemQrSheet Widget Tests', () {
    testWidgets('renders floor tag preview card and QR elements', (tester) async {
      await tester.pumpWidget(buildTestWidget(piece: testPiece));

      await tester.tap(find.byKey(const Key('open_qr_sheet_button')));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.text('Floor Tag QR Code'), findsOneWidget);
      expect(find.text('AVELINE ATELIER'), findsOneWidget);
      expect(find.text('Imperial Organza Anarkali'), findsOneWidget);
      expect(find.text('AVL-SUIT-100'), findsOneWidget);
      expect(find.text('Rs 48,000'), findsOneWidget);

      // Verify format switchers exist
      expect(find.byKey(const Key('qr_format_json')), findsOneWidget);
      expect(find.byKey(const Key('qr_format_url')), findsOneWidget);
      expect(find.byKey(const Key('qr_format_sku')), findsOneWidget);
    });

    testWidgets('switches payload format when selecting URL and SKU formats', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 2.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(buildTestWidget(piece: testPiece));

      await tester.tap(find.byKey(const Key('open_qr_sheet_button')));
      await tester.pumpAndSettle();

      // Tap URL format
      await tester.tap(find.byKey(const Key('qr_format_url')));
      await tester.pumpAndSettle();

      expect(find.textContaining('https://aveline.app/app/b/aveline-couture/catalog/piece-100'), findsOneWidget);

      // Tap SKU format
      await tester.tap(find.byKey(const Key('qr_format_sku')));
      await tester.pumpAndSettle();

      expect(find.text('AVL-SUIT-100'), findsWidgets);
    });

    testWidgets('copies payload and dismisses sheet when Done is tapped', (tester) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 2.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(buildTestWidget(piece: testPiece));

      await tester.tap(find.byKey(const Key('open_qr_sheet_button')));
      await tester.pumpAndSettle();

      // Tap Copy
      await tester.tap(find.byKey(const Key('qr_copy_action_btn')));
      await tester.pumpAndSettle();

      // Tap Done
      await tester.tap(find.byKey(const Key('qr_close_sheet_btn')));
      await tester.pumpAndSettle();

      expect(find.text('Floor Tag QR Code'), findsNothing);
    });
  });
}
