import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/suppliers_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Widget buildTestWidget({
    required DemoCatalogProductRepository repository,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: SuppliersView(repository: repository),
      ),
    );
  }

  group('SuppliersView Widget Tests', () {
    testWidgets('renders partner ateliers list with specialty and lead times', (tester) async {
      final repo = DemoCatalogProductRepository();
      await tester.pumpWidget(buildTestWidget(repository: repo));
      await tester.pumpAndSettle();

      expect(find.text('Partner Ateliers & Heritage Fabric Mills'), findsOneWidget);
      expect(find.byKey(const Key('supplier_card_sup-1')), findsOneWidget);
      expect(find.text('Varanasi Silk Works'), findsOneWidget);
      expect(find.text('Pure Katan Silk & Brocade'), findsOneWidget);
      expect(find.text('21 days'), findsOneWidget);
      expect(find.text('Rs 850'), findsOneWidget);
    });

    testWidgets('opens wholesale sample catalog when tapping View Atelier Catalog', (tester) async {
      final repo = DemoCatalogProductRepository();
      await tester.pumpWidget(buildTestWidget(repository: repo));
      await tester.pumpAndSettle();

      final viewCatalogBtn = find.byKey(const Key('view_supplier_catalog_btn_sup-1'));
      expect(viewCatalogBtn, findsOneWidget);

      await tester.tap(viewCatalogBtn);
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.text('Varanasi Silk Works - Catalog'), findsOneWidget);
      expect(find.text('Imperial Katan Brocade Saree'), findsOneWidget);
    });
  });
}
