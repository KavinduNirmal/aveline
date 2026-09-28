import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_composition.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/compose_outfit_screen.dart';
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
  group('ComposeOutfitScreen', () {
    late DemoCatalogProductRepository repository;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
    });

    testWidgets('renders composition studio sections and hero pieces', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          ComposeOutfitScreen(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      expect(find.text('Compose Look with Elle'), findsOneWidget);
      expect(find.text('1. Primary Statement Piece'), findsOneWidget);
      expect(find.text('2. Ceremonial Occasion'), findsOneWidget);
      expect(find.byKey(const Key('compose_elle_trigger_button')), findsOneWidget);
    });

    testWidgets('triggers Elle AI composition and populates breakdown and notes', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(
          ComposeOutfitScreen(repository: repository),
        ),
      );
      await tester.pump(const Duration(milliseconds: 100));

      // Tap occasion
      final occChip = find.byKey(const Key('compose_occ_Groom Royal Wedding'));
      await tester.tap(occChip);
      await tester.pump(const Duration(milliseconds: 50));

      // Tap Compose with Elle AI button
      final composeBtn = find.byKey(const Key('compose_elle_trigger_button'));
      await tester.tap(composeBtn);
      await tester.pump(const Duration(milliseconds: 100));

      // Composed breakdown and notes should now appear
      expect(find.text('3. Curated Ensemble Breakdown'), findsOneWidget);
      expect(find.text('4. Editorial Title & Notes'), findsOneWidget);
      expect(find.byKey(const Key('compose_outfit_title_input')), findsOneWidget);
      expect(find.byKey(const Key('compose_outfit_notes_input')), findsOneWidget);
      expect(find.byKey(const Key('compose_outfit_save_button')), findsOneWidget);
    });

    testWidgets('submits composed lookbook and returns result', (tester) async {
      _useTallSurface(tester);
      OutfitComposition? returnedLookbook;

      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) {
              return ElevatedButton(
                onPressed: () async {
                  returnedLookbook = await Navigator.of(context).push<OutfitComposition>(
                    PageRouteBuilder(
                      pageBuilder: (context, animation, secondaryAnimation) =>
                          ComposeOutfitScreen(repository: repository),
                      transitionDuration: Duration.zero,
                    ),
                  );
                },
                child: const Text('Open Compose'),
              );
            },
          ),
        ),
      );

      await tester.tap(find.text('Open Compose'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      // Trigger compose
      final composeBtn = find.byKey(const Key('compose_elle_trigger_button'));
      await tester.tap(composeBtn);
      await tester.pump(const Duration(milliseconds: 100));

      // Enter custom title
      final titleField = find.byKey(const Key('compose_outfit_title_input'));
      await tester.enterText(titleField, 'Heirloom Custom Royal Ensemble');
      await tester.pump(const Duration(milliseconds: 50));

      // Save lookbook
      final saveBtn = find.byKey(const Key('compose_outfit_save_button'));
      await tester.tap(saveBtn);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 100));

      expect(returnedLookbook, isNotNull);
      expect(returnedLookbook!.name, 'Heirloom Custom Royal Ensemble');
    });
  });
}
