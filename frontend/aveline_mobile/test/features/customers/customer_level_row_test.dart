import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_level_row.dart';
import 'package:aveline_mobile/shared/widgets/filter_pill.dart';
import 'package:aveline_mobile/shared/widgets/trailing_fade.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The level row on its own, with no screen around it.
Future<void> _pumpRow(
  WidgetTester tester, {
  CustomerLevel? selected,
  ValueChanged<CustomerLevel>? onToggled,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          // Reduced motion is on, matching the rest of the suite: the fade and
          // the pills hold still, which is what these tests are about.
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: Scaffold(
            body: CustomerLevelRow(
              selected: selected,
              onToggled: onToggled ?? (_) {},
            ),
          ),
        ),
      ),
    ),
  );
}

FilterPill _pill(WidgetTester tester, CustomerLevel level) => tester
    .widget<FilterPill>(find.byKey(ValueKey('customer_level_${level.name}')));

void main() {
  group('CustomerLevelRow', () {
    testWidgets('lists every level, strongest first, as a horizontal row', (
      tester,
    ) async {
      await _pumpRow(tester);

      for (final level in CustomerLevel.values) {
        expect(find.text(level.label), findsOneWidget);
      }

      final list = tester.widget<ListView>(
        find.descendant(
          of: find.byType(CustomerLevelRow),
          matching: find.byType(ListView),
        ),
      );
      expect(list.scrollDirection, Axis.horizontal);

      // Declaration order is the ladder's order, so the strongest level is the
      // one the associate meets first.
      expect(
        tester.getTopLeft(find.byKey(const ValueKey('customer_level_vip'))).dx,
        lessThan(
          tester
              .getTopLeft(find.byKey(const ValueKey('customer_level_level1')))
              .dx,
        ),
      );
    });

    testWidgets('fades its trailing edge so the row reads as scrollable', (
      tester,
    ) async {
      await _pumpRow(tester);

      expect(
        find.descendant(
          of: find.byType(CustomerLevelRow),
          matching: find.byType(TrailingFade),
        ),
        findsOneWidget,
      );
    });

    testWidgets('marks the level in force as the selected one', (tester) async {
      await _pumpRow(tester, selected: CustomerLevel.level2);

      expect(_pill(tester, CustomerLevel.vip).selected, isFalse);
      expect(_pill(tester, CustomerLevel.level3).selected, isFalse);
      expect(_pill(tester, CustomerLevel.level2).selected, isTrue);
      expect(_pill(tester, CustomerLevel.level1).selected, isFalse);
    });

    testWidgets('reports the level that was tapped', (tester) async {
      final tapped = <CustomerLevel>[];
      await _pumpRow(tester, onToggled: tapped.add);

      await tester.tap(find.byKey(const ValueKey('customer_level_level3')));
      await tester.pump();

      expect(tapped, [CustomerLevel.level3]);
    });
  });
}
