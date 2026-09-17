import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/focus_deck.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const _tasks = [
  FocusTask(
    id: 'a',
    domain: FocusDomain.logistics,
    title: 'Approve delivery courier for Mrs. Silva',
    detail: 'Patron: Maria Silva · Delivery Waybill #AV-881.',
    timeLabel: '3:00 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Courier approved.',
  ),
  FocusTask(
    id: 'b',
    domain: FocusDomain.patron,
    title: 'Set the fitting room for Mrs. Perera',
    detail: 'Patron: Anoma Perera · Two held evening looks.',
    timeLabel: '5:30 PM',
    actionLabel: 'Mark ready',
    doneMessage: 'Fitting room ready.',
  ),
  FocusTask(
    id: 'c',
    domain: FocusDomain.wardrobe,
    title: 'Review the silk intake from Nuwa',
    detail: 'Vendor: Nuwa Silks · 24 pieces against #AV-118.',
    timeLabel: '4:15 PM',
    actionLabel: 'Sign Off',
    doneMessage: 'Intake signed off.',
  ),
  FocusTask(
    id: 'd',
    domain: FocusDomain.commerce,
    title: 'Confirm the quotation for Ranmali',
    detail: 'Patron: Ranmali Fernando · Quote #AV-902.',
    timeLabel: '12:30 PM',
    actionLabel: 'Approve',
    doneMessage: 'Quotation approved.',
  ),
];

Widget _bed(List<FocusTask> tasks) => MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(body: FocusSection(tasks: tasks)),
    );

void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// The docket currently in hand. Everything else is a layer behind it.
Finder _front() => find.byKey(const Key('focus_deck_top'));

/// Finds [finder] inside the docket in hand only, never in the layers behind.
Finder _inFront(Finder finder) =>
    find.descendant(of: _front(), matching: finder);

/// Advances past the pile's animation without running the toast's three-second
/// timer to completion.
Future<void> _finishMotion(WidgetTester tester) async {
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 400));
  await tester.pump();
}

void main() {
  group('FocusDeck', () {
    testWidgets('opens with the first docket in hand and counts the pile',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      expect(_inFront(find.text(_tasks.first.title)), findsOneWidget);
      expect(_inFront(find.text('1 of 4')), findsOneWidget);
      expect(_inFront(find.text('LOGISTICS')), findsOneWidget);
    });

    testWidgets('swiping sends the docket in hand to the bottom of the pile',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      await tester.drag(_front(), const Offset(-220, 0));
      await tester.pumpAndSettle();

      expect(_inFront(find.text(_tasks[1].title)), findsOneWidget);
      expect(_inFront(find.text('2 of 4')), findsOneWidget);
      expect(_inFront(find.text(_tasks.first.title)), findsNothing);
    });

    testWidgets('cycles back to the first docket once every one is sent back',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      for (var i = 0; i < _tasks.length; i++) {
        await tester.drag(_front(), const Offset(-220, 0));
        await tester.pumpAndSettle();
      }

      expect(_inFront(find.text(_tasks.first.title)), findsOneWidget);
      expect(_inFront(find.text('1 of 4')), findsOneWidget);
    });

    testWidgets('a short drag falls back to the docket in hand',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      await tester.drag(_front(), const Offset(-20, 0));
      await tester.pumpAndSettle();

      expect(_inFront(find.text(_tasks.first.title)), findsOneWidget);
      expect(_inFront(find.text('1 of 4')), findsOneWidget);
    });

    testWidgets('the chevron sends the docket in hand to the back',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      await tester.tap(
        _inFront(find.byIcon(Icons.keyboard_double_arrow_down_rounded)),
      );
      await tester.pumpAndSettle();

      expect(_inFront(find.text(_tasks[1].title)), findsOneWidget);
      expect(_inFront(find.text('2 of 4')), findsOneWidget);
    });

    testWidgets('signing a docket off lifts it away and confirms it',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      await tester.tap(_inFront(find.widgetWithText(FilledButton, 'Sign Off')));
      await _finishMotion(tester);

      expect(find.text('Courier approved.'), findsOneWidget);
      expect(_inFront(find.text(_tasks[1].title)), findsOneWidget);
      expect(_inFront(find.text('1 of 3')), findsOneWidget);
    });

    testWidgets('shows a clear empty state once every docket is cleared',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      for (var i = 0; i < _tasks.length; i++) {
        await tester.tap(_inFront(find.byType(FilledButton)));
        await _finishMotion(tester);
      }

      expect(find.text('Nothing is waiting on you'), findsOneWidget);
      expect(find.byType(FilledButton), findsNothing);
    });

    testWidgets('a vertical drag scrolls the page instead of moving the pile',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
            body: ListView(
              children: [
                const SizedBox(height: 220),
                FocusSection(tasks: _tasks),
                const SizedBox(height: 600),
              ],
            ),
          ),
        ),
      );

      final before = tester.getTopLeft(_front()).dy;
      await tester.drag(_front(), const Offset(0, -180));
      await tester.pumpAndSettle();

      expect(tester.getTopLeft(_front()).dy, lessThan(before));
      expect(_inFront(find.text(_tasks.first.title)), findsOneWidget);
      expect(_inFront(find.text('1 of 4')), findsOneWidget);
    });

    testWidgets('names the part of the boutique the docket belongs to',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed([_tasks[2]]));

      expect(_inFront(find.text('WARDROBE')), findsOneWidget);
      expect(_inFront(find.text('1 of 1')), findsOneWidget);
    });

    testWidgets('hides the cycle button when there is nothing behind',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed([_tasks[2]]));

      expect(
        find.byIcon(Icons.keyboard_double_arrow_down_rounded),
        findsNothing,
      );
    });
  });
}
