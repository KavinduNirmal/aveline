import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/supplier.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/supplier_catalog_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const testSupplier = Supplier(
    id: 'sup-1',
    name: 'Varanasi Silk Works',
    specialty: 'Pure Katan Silk & Brocade',
    location: 'Varanasi, Uttar Pradesh',
    leadTimeDays: 21,
    minimumOrder: 850.0,
    sampleCatalogCount: 3,
  );

  Widget buildTestWidget({
    required Supplier supplier,
    required DemoCatalogProductRepository repository,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => Center(
            child: ElevatedButton(
              key: const Key('open_supplier_catalog_btn'),
              onPressed: () {
                SupplierCatalogSheet.show(
                  context,
                  supplier: supplier,
                  repository: repository,
                );
              },
              child: const Text('Open Atelier Catalog'),
            ),
          ),
        ),
      ),
    );
  }

  group('SupplierCatalogSheet Widget Tests', () {
    testWidgets('renders wholesale sample catalog for supplier', (tester) async {
      final repo = DemoCatalogProductRepository();
      await tester.pumpWidget(buildTestWidget(supplier: testSupplier, repository: repo));

      await tester.tap(find.byKey(const Key('open_supplier_catalog_btn')));
      await tester.pump();
      await tester.pumpAndSettle();

      expect(find.text('Varanasi Silk Works - Catalog'), findsOneWidget);
      expect(find.byKey(const Key('supplier_catalog_list_view')), findsOneWidget);

      // Verify sample catalog items
      expect(find.text('Imperial Katan Brocade Saree'), findsOneWidget);
      expect(find.text('Wholesale: Rs 650'), findsOneWidget);
      expect(find.text('Pure Katan Silk'), findsOneWidget);
      expect(find.text('SAREES'), findsOneWidget);
    });
  });
}
