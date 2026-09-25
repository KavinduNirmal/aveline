import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/customer_matches_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  final testPiece = CatalogProduct(
    id: 'piece-1',
    organizationId: 'org-1',
    name: 'Kanjeevaram Bridal Silk Saree',
    category: 'Sarees',
    color: 'Crimson',
    sizes: const ['Free Size'],
    price: 85000.0,
    cost: 40000.0,
    quantity: 3,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 24),
  );

  Widget buildTestWidget({
    required CatalogProduct piece,
    required DemoCatalogProductRepository repository,
    void Function(String, String)? onOpenSalon,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => Center(
            child: ElevatedButton(
              key: const Key('open_sheet_button'),
              onPressed: () {
                CustomerMatchesSheet.show(
                  context,
                  piece: piece,
                  repository: repository,
                  onOpenSalon: onOpenSalon,
                );
              },
              child: const Text('Open Matches'),
            ),
          ),
        ),
      ),
    );
  }

  group('CustomerMatchesSheet Widget Tests', () {
    testWidgets('loads and renders VIP client affinity matches', (tester) async {
      final repo = DemoCatalogProductRepository();
      await tester.pumpWidget(buildTestWidget(piece: testPiece, repository: repo));

      // Tap to open sheet
      await tester.tap(find.byKey(const Key('open_sheet_button')));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.text('VIP Client Matches'), findsOneWidget);
      expect(find.text('Kanjeevaram Bridal Silk Saree'), findsOneWidget);
      expect(find.byKey(const Key('vip_matches_list_view')), findsOneWidget);

      // Verify sample demo matches are listed
      expect(find.text('Maya Lin'), findsOneWidget);
      expect(find.text('94% Match'), findsOneWidget);
      expect(find.text('Priya Sharma'), findsOneWidget);
      expect(find.text('89% Match'), findsOneWidget);
    });

    testWidgets('triggers recalculation and updates matches list', (tester) async {
      final repo = DemoCatalogProductRepository();
      await tester.pumpWidget(buildTestWidget(piece: testPiece, repository: repo));

      await tester.tap(find.byKey(const Key('open_sheet_button')));
      await tester.pumpAndSettle();

      // Tap Recalculate
      await tester.tap(find.byKey(const Key('vip_matches_recalc_btn')));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('vip_matches_list_view')), findsOneWidget);
      expect(find.text('Maya Lin'), findsOneWidget);
    });

    testWidgets('initiates salon outreach and marks match acted', (tester) async {
      final repo = DemoCatalogProductRepository();
      String? actedCustomerId;
      String? actedClientName;

      await tester.pumpWidget(
        buildTestWidget(
          piece: testPiece,
          repository: repo,
          onOpenSalon: (cid, name) {
            actedCustomerId = cid;
            actedClientName = name;
          },
        ),
      );

      await tester.tap(find.byKey(const Key('open_sheet_button')));
      await tester.pumpAndSettle();

      // Find first outreach button
      final outreachBtn = find.byKey(const Key('vip_outreach_btn_match-1-piece-1'));
      expect(outreachBtn, findsOneWidget);

      await tester.tap(outreachBtn);
      await tester.pump();
      await tester.pumpAndSettle();

      expect(actedCustomerId, 'cust-101');
      expect(actedClientName, 'Maya Lin');
      expect(find.text('Outreach Contacted'), findsOneWidget);
    });
  });
}
