import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_filters.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/catalog_filter_screen.dart';
import 'package:aveline_mobile/shared/widgets/filter_pill.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Reduced motion is on, matching the rest of the suite: the backdrop carries
/// ambient animation that would never settle otherwise.
Widget _wrap({CatalogFilters? initial}) {
  return MaterialApp(
    theme: AppTheme.light,
    home: Builder(
      builder: (context) => MediaQuery(
        data: MediaQuery.of(context).copyWith(disableAnimations: true),
        child: CatalogFilterScreen(initial: initial),
      ),
    ),
  );
}

/// The pill whose label is [label].
FilterPill _pill(WidgetTester tester, String label) {
  return tester.widget<FilterPill>(
    find.ancestor(of: find.text(label), matching: find.byType(FilterPill)),
  );
}

void main() {
  group('CatalogFilterScreen', () {
    testWidgets('lists every filter group with its placeholder options', (
      tester,
    ) async {
      await tester.pumpWidget(_wrap());

      // The later groups are reached by scrolling.
      expect(find.text('AVAILABILITY'), findsOneWidget);
      expect(find.text('Available'), findsOneWidget);
      expect(find.text('CATEGORY'), findsOneWidget);
      expect(find.text('Sarees'), findsOneWidget);

      await tester.scrollUntilVisible(
        find.text('PRICE'),
        240,
        scrollable: find.byType(Scrollable).first,
      );

      expect(find.text('PRICE'), findsOneWidget);
      expect(find.text('Over 150k'), findsOneWidget);
    });

    testWidgets('marks a chosen option and counts it on the action', (
      tester,
    ) async {
      await tester.pumpWidget(_wrap());

      expect(find.text('Show all pieces'), findsOneWidget);
      expect(_pill(tester, 'Available').selected, isFalse);

      await tester.tap(find.text('Available'));
      await tester.pump();

      expect(_pill(tester, 'Available').selected, isTrue);
      expect(find.text('Show results (1)'), findsOneWidget);
    });

    testWidgets('swaps within a single-select group instead of accumulating', (
      tester,
    ) async {
      await tester.pumpWidget(_wrap());

      await tester.tap(find.text('Available'));
      await tester.pump();
      await tester.tap(find.text('Sold out'));
      await tester.pump();

      expect(_pill(tester, 'Available').selected, isFalse);
      expect(_pill(tester, 'Sold out').selected, isTrue);
      expect(find.text('Show results (1)'), findsOneWidget);
    });

    testWidgets('shows the filters it was opened with as chosen', (
      tester,
    ) async {
      final initial = const CatalogFilters.none().toggle(
        CatalogFilterGroup.category,
        'Gowns',
      );

      await tester.pumpWidget(_wrap(initial: initial));

      expect(_pill(tester, 'Gowns').selected, isTrue);
      expect(find.text('Show results (1)'), findsOneWidget);
    });

    testWidgets('reset clears every choice and goes inert', (tester) async {
      await tester.pumpWidget(_wrap());

      TextButton reset() => tester.widget<TextButton>(
        find.byKey(const Key('catalog_filter_reset')),
      );

      expect(reset().onPressed, isNull);

      await tester.tap(find.text('Sarees'));
      await tester.pump();
      expect(reset().onPressed, isNotNull);

      await tester.tap(find.byKey(const Key('catalog_filter_reset')));
      await tester.pump();

      expect(_pill(tester, 'Sarees').selected, isFalse);
      expect(find.text('Show all pieces'), findsOneWidget);
      expect(reset().onPressed, isNull);
    });

    testWidgets('returns the chosen options when results are shown', (
      tester,
    ) async {
      // The pushed route has to inherit reduced motion too, or the backdrop's
      // ambient animation never settles.
      tester.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(
        tester.platformDispatcher.clearAccessibilityFeaturesTestValue,
      );

      CatalogFilters? returned;

      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () async {
                    returned = await Navigator.of(context).push<CatalogFilters>(
                      MaterialPageRoute(
                        builder: (routeContext) => const CatalogFilterScreen(),
                      ),
                    );
                  },
                  child: const Text('open filters'),
                ),
              ),
            ),
          ),
        ),
      );

      await tester.tap(find.text('open filters'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      await tester.tap(find.text('Sarees'));
      await tester.pump();
      await tester.tap(find.text('Available'));
      await tester.pump();

      await tester.tap(find.byKey(const Key('catalog_filter_apply')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(returned, isNotNull);
      expect(
        returned!.isSelected(CatalogFilterGroup.category, 'Sarees'),
        isTrue,
      );
      expect(
        returned!.isSelected(CatalogFilterGroup.availability, 'Available'),
        isTrue,
      );
      // The host screen is back.
      expect(find.text('open filters'), findsOneWidget);
    });
  });
}
