import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/quick_actions_row.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

Widget _bed(ValueChanged<QuickAction> onSelected) => MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(body: QuickActionsRow(onSelected: onSelected)),
    );

void main() {
  group('QuickActionsRow', () {
    testWidgets('offers every floor tool', (tester) async {
      await tester.pumpWidget(_bed((_) {}));

      for (final action in QuickAction.values) {
        expect(
          find.text(action.label),
          findsOneWidget,
          reason: 'missing quick action ${action.label}',
        );
      }
    });

    testWidgets('reports the tapped action', (tester) async {
      final tapped = <QuickAction>[];
      await tester.pumpWidget(_bed(tapped.add));

      await tester.tap(find.text('Clock in'));
      await tester.pump();
      await tester.tap(find.text('More'));
      await tester.pump();

      expect(tapped, [QuickAction.clockIn, QuickAction.more]);
    });

    testWidgets('scrolls once the row is wider than a phone', (tester) async {
      tester.view.physicalSize = const Size(1170, 2532);
      tester.view.devicePixelRatio = 3.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(_bed((_) {}));

      final position =
          tester.state<ScrollableState>(find.byType(Scrollable)).position;
      expect(position.maxScrollExtent, greaterThan(0));
    });

    testWidgets('holds its position once dragged to the end', (tester) async {
      tester.view.physicalSize = const Size(1170, 2532);
      tester.view.devicePixelRatio = 3.0;
      addTearDown(tester.view.reset);

      await tester.pumpWidget(_bed((_) {}));
      final position =
          tester.state<ScrollableState>(find.byType(Scrollable)).position;

      await tester.drag(find.byType(ListView), const Offset(-400, 0));
      await tester.pumpAndSettle();

      // The row used to rebuild its wrapper on reaching the end, which
      // re-created the scroll view and threw the offset back to zero.
      expect(position.pixels, position.maxScrollExtent);
      expect(find.text('More'), findsOneWidget);
    });
  });
}
