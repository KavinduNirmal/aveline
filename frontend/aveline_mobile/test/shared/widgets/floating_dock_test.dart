import 'package:aveline_mobile/shared/widgets/blossom.dart';
import 'package:aveline_mobile/shared/widgets/floating_dock.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  Widget wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

  testWidgets('renders the four line-icon tabs and the center launcher',
      (tester) async {
    await tester.pumpWidget(
      wrap(
        FloatingDock(
          current: DockTab.home,
          onSelect: (_) {},
          onOpenSalon: () {},
        ),
      ),
    );

    expect(find.text('Home'), findsOneWidget);
    expect(find.text('Customers'), findsOneWidget);
    expect(find.text('Catalog'), findsOneWidget);
    expect(find.text('Profile'), findsOneWidget);
    expect(find.bySemanticsLabel('Open Salon'), findsOneWidget);
  });

  testWidgets('tapping a tab reports the selected DockTab', (tester) async {
    DockTab? selected;
    await tester.pumpWidget(
      wrap(
        FloatingDock(
          current: DockTab.home,
          onSelect: (tab) => selected = tab,
          onOpenSalon: () {},
        ),
      ),
    );

    await tester.tap(find.text('Customers'));
    expect(selected, DockTab.customers);

    await tester.tap(find.text('Catalog'));
    expect(selected, DockTab.catalog);
  });

  testWidgets('tapping the center launcher opens the Salon', (tester) async {
    var opened = false;
    await tester.pumpWidget(
      wrap(
        FloatingDock(
          current: DockTab.home,
          onSelect: (_) {},
          onOpenSalon: () => opened = true,
        ),
      ),
    );

    await tester.tap(find.bySemanticsLabel('Open Salon'));
    expect(opened, isTrue);
  });

  testWidgets('center launcher keeps the Blossom scaled down inside its circle',
      (tester) async {
    await tester.pumpWidget(
      wrap(
        FloatingDock(
          current: DockTab.home,
          onSelect: (_) {},
          onOpenSalon: () {},
        ),
      ),
    );

    final launcher = tester.getSize(find.bySemanticsLabel('Open Salon'));
    // The painted mark, not the widget's outer box: before the constraint fix the mark
    // painted at the launcher's full 56 and covered the entire circle.
    final blossom = tester.getSize(
      find.descendant(
        of: find.byType(Blossom),
        matching: find.byType(CustomPaint),
      ),
    );

    expect(launcher.width, 56);
    expect(blossom, const Size(30, 30));
    expect(blossom.width, lessThan(launcher.width * 0.6));
  });
}
