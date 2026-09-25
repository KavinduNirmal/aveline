import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/add_edit_product_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _wrap(Widget child) {
  return MaterialApp(
    home: child,
  );
}

void _useTallSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 7200);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

void main() {
  group('AddEditProductScreen', () {
    late DemoCatalogProductRepository repository;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
    });

    testWidgets('renders create piece form with sections', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          AddEditProductScreen(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Add New Piece'), findsOneWidget);
      expect(find.text('Photograph & Vision AI'), findsOneWidget);
      expect(find.text('Identity & Category'), findsOneWidget);
      expect(find.text('Color, Fabric & Sizes'), findsOneWidget);
      expect(find.text('Pricing & Inventory'), findsOneWidget);
      expect(find.text('Description & Styling Notes'), findsOneWidget);
      expect(find.byKey(const Key('add_product_submit_button')), findsOneWidget);
    });

    testWidgets('renders edit piece form when product is provided', (tester) async {
      _useTallSurface(tester);
      final product = CatalogProduct(
        id: 'edit-1',
        organizationId: 'org-1',
        name: 'Heirloom Kanjeevaram Saree',
        category: 'Sarees',
        color: 'Wine',
        sizes: const ['38', '40'],
        price: 75000.0,
        cost: 30000.0,
        quantity: 2,
        status: CatalogItemStatus.available,
        isAvailable: true,
        createdAtUtc: DateTime.now(),
        fabric: 'Mulberry Silk',
        sku: 'AVL-777',
      );

      await tester.pumpWidget(
        _wrap(
          AddEditProductScreen(
            product: product,
            repository: repository,
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Edit Piece'), findsOneWidget);
      expect(find.text('Heirloom Kanjeevaram Saree'), findsOneWidget);
      expect(find.text('AVL-777'), findsOneWidget);
      expect(find.text('Save Changes'), findsOneWidget);
    });

    testWidgets('shows validation error when piece name is empty on submit', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          AddEditProductScreen(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      // Clear piece name input
      final nameInput = find.byKey(const Key('add_product_name_input'));
      await tester.enterText(nameInput, '');
      await tester.pump(const Duration(milliseconds: 50));

      // Tap submit
      final submitBtn = find.byKey(const Key('add_product_submit_button'));
      await tester.ensureVisible(submitBtn);
      await tester.tap(submitBtn);
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Please enter a name for the piece.'), findsOneWidget);
    });

    testWidgets('generates AI description on button tap', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          AddEditProductScreen(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      final nameInput = find.byKey(const Key('add_product_name_input'));
      await tester.enterText(nameInput, 'Bridal Banarasi Silk Saree');
      await tester.pump(const Duration(milliseconds: 50));

      final aiBtn = find.byKey(const Key('add_product_generate_ai_button'));
      await tester.ensureVisible(aiBtn);
      await tester.tap(aiBtn);
      await tester.pump(const Duration(milliseconds: 100));

      final descField = tester.widget<TextField>(find.byKey(const Key('add_product_description_input')));
      expect(descField.controller?.text, contains('Bridal Banarasi Silk Saree'));
      expect(descField.controller?.text.toLowerCase(), contains('pure silk'));
    });

    testWidgets('submits new piece and returns created product', (tester) async {
      _useTallSurface(tester);
      CatalogProduct? returnedProduct;

      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) {
              return ElevatedButton(
                onPressed: () async {
                  returnedProduct = await Navigator.of(context).push<CatalogProduct>(
                    PageRouteBuilder(
                      pageBuilder: (context, animation, secondaryAnimation) => AddEditProductScreen(repository: repository),
                      transitionDuration: Duration.zero,
                    ),
                  );
                },
                child: const Text('Open Form'),
              );
            },
          ),
        ),
      );

      await tester.tap(find.text('Open Form'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      // Enter piece name & price
      final nameInput = find.byKey(const Key('add_product_name_input'));
      await tester.enterText(nameInput, 'Custom Royal Lehenga');
      await tester.pump(const Duration(milliseconds: 50));

      final priceInput = find.byKey(const Key('add_product_price_input'));
      await tester.enterText(priceInput, '120000');
      await tester.pump(const Duration(milliseconds: 50));

      // Submit form
      final submitBtn = find.byKey(const Key('add_product_submit_button'));
      await tester.ensureVisible(submitBtn);
      await tester.tap(submitBtn);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      // Should have returned to parent with product
      expect(returnedProduct, isNotNull);
      expect(returnedProduct!.name, 'Custom Royal Lehenga');
      expect(returnedProduct!.price, 120000.0);
    });
  });
}
