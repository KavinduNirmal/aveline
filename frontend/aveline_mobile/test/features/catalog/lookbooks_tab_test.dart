import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/lookbooks_view.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _wrap(Widget child) {
  return MaterialApp(
    home: Scaffold(
      body: child,
    ),
  );
}

void _useTallSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 7200);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.resetPhysicalSize);
  addTearDown(tester.view.resetDevicePixelRatio);
}

void main() {
  group('Lookbooks Tab & Components', () {
    late DemoCatalogProductRepository repository;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
    });

    testWidgets('renders LookbooksView with occasion filters and lookbook cards', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          LookbooksView(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('All'), findsOneWidget);
      expect(find.text('Sangeet & Reception'), findsOneWidget);
      expect(find.text('Bridal Heirloom'), findsOneWidget);
      expect(find.byKey(const Key('lookbooks_compose_header_button')), findsOneWidget);

      expect(find.text('Emerald Heritage Sangeet Look'), findsOneWidget);
      expect(find.text('Imperial Crimson Bridal Heirloom'), findsOneWidget);
    });

    testWidgets('filters lookbooks when occasion filter chip is tapped', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          LookbooksView(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      // Scroll to and tap Bridal Heirloom filter
      await tester.ensureVisible(find.text('Bridal Heirloom'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Bridal Heirloom'));
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Imperial Crimson Bridal Heirloom'), findsOneWidget);
      expect(find.text('Emerald Heritage Sangeet Look'), findsNothing);
    });

    testWidgets('opens LookbookDetailSheet when card is tapped', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          LookbooksView(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      await tester.tap(find.text('Emerald Heritage Sangeet Look'));
      await tester.pump(const Duration(milliseconds: 300));

      expect(find.text('Elle Styling Direction'), findsOneWidget);
      expect(find.text('TOTAL ENSEMBLE'), findsOneWidget);
      expect(find.textContaining('Coordinated Pieces'), findsOneWidget);
    });

    testWidgets('opens EditLookbookDialog and saves edited title', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          LookbooksView(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      // Open card menu for lb-1
      final menuBtn = find.byKey(const Key('lookbook_menu_lb-1'));
      await tester.tap(menuBtn);
      await tester.pumpAndSettle();

      // Tap Edit Details
      await tester.tap(find.text('Edit Details'));
      await tester.pumpAndSettle();

      expect(find.text('Edit Lookbook Details'), findsOneWidget);

      final nameField = find.byKey(const Key('edit_lookbook_name_input'));
      await tester.enterText(nameField, 'Renamed Haute Couture Sangeet');
      await tester.pump(const Duration(milliseconds: 50));

      final saveBtn = find.byKey(const Key('edit_lookbook_save_button'));
      await tester.tap(saveBtn);
      await tester.pumpAndSettle();

      expect(find.text('Renamed Haute Couture Sangeet'), findsOneWidget);
    });

    testWidgets('opens delete confirmation dialog and deletes lookbook', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          LookbooksView(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      // Open card menu for lb-1
      final menuBtn = find.byKey(const Key('lookbook_menu_lb-1'));
      await tester.tap(menuBtn);
      await tester.pumpAndSettle();

      // Tap Delete Lookbook
      await tester.tap(find.text('Delete Lookbook'));
      await tester.pumpAndSettle();

      expect(find.text('Delete Lookbook?'), findsOneWidget);

      final confirmBtn = find.byKey(const Key('confirm_delete_lookbook_button'));
      await tester.tap(confirmBtn);
      await tester.pumpAndSettle();

      expect(find.text('Emerald Heritage Sangeet Look'), findsNothing);
    });
  });
}
