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

/// Mirrors Home: the caller owns the deck, so a signed-off docket leaves only
/// because the caller drops it from the list.
class _DeckHost extends StatefulWidget {
  const _DeckHost({required this.initial, this.onCompleted});

  final List<FocusTask> initial;
  final ValueChanged<FocusTask>? onCompleted;

  @override
  State<_DeckHost> createState() => _DeckHostState();
}

class _DeckHostState extends State<_DeckHost> {
  late final List<FocusTask> _tasks = List.of(widget.initial);

  void _complete(FocusTask task) {
    setState(() => _tasks.removeWhere((candidate) => candidate.id == task.id));
    widget.onCompleted?.call(task);
  }

  @override
  Widget build(BuildContext context) =>
      FocusSection(tasks: _tasks, onComplete: _complete);
}

Widget _bed(List<FocusTask> tasks, {ValueChanged<FocusTask>? onCompleted}) =>
    MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: _DeckHost(initial: tasks, onCompleted: onCompleted),
      ),
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
    testWidgets('opens with the first docket in hand', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      expect(_inFront(find.text(_tasks.first.title)), findsOneWidget);
      expect(_inFront(find.text('LOGISTICS')), findsOneWidget);
    });

    testWidgets('counts what is left beside the section, not on the docket',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      expect(find.text('4 left'), findsOneWidget);

      // The pile cycles, so a position (`1 of 4`) would claim a progress that
      // never advances. It is gone from the card.
      expect(_inFront(find.text('1 of 4')), findsNothing);
      expect(find.textContaining(' of '), findsNothing);
    });

    testWidgets('swiping sends the docket in hand to the bottom of the pile',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      await tester.drag(_front(), const Offset(-220, 0));
      await tester.pumpAndSettle();

      expect(_inFront(find.text(_tasks[1].title)), findsOneWidget);
      expect(_inFront(find.text(_tasks.first.title)), findsNothing);
      // Cycling is not progress: the count is unchanged.
      expect(find.text('4 left'), findsOneWidget);
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
      expect(find.text('4 left'), findsOneWidget);
    });

    testWidgets('a short drag falls back to the docket in hand',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      await tester.drag(_front(), const Offset(-20, 0));
      await tester.pumpAndSettle();

      expect(_inFront(find.text(_tasks.first.title)), findsOneWidget);
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
    });

    testWidgets('signing a docket off lifts it away and reports it',
        (tester) async {
      _usePhoneSurface(tester);
      final completed = <FocusTask>[];
      await tester.pumpWidget(_bed(_tasks, onCompleted: completed.add));

      await tester.tap(_inFront(find.widgetWithText(FilledButton, 'Sign Off')));
      await _finishMotion(tester);

      expect(completed.map((task) => task.id), ['a']);
      expect(_inFront(find.text(_tasks[1].title)), findsOneWidget);
      expect(find.text('3 left'), findsOneWidget);
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
      // Nothing left to count, so the header drops the count rather than
      // saying "0 left" beside an empty state that already says it.
      expect(find.text('0 left'), findsNothing);
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
                const _DeckHost(initial: _tasks),
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
    });

    testWidgets('names the part of the boutique the docket belongs to',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed([_tasks[2]]));

      expect(_inFront(find.text('WARDROBE')), findsOneWidget);
      expect(find.text('1 left'), findsOneWidget);
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

    testWidgets('a layer behind never overflows, whatever its copy runs to',
        (tester) async {
      _usePhoneSurface(tester);
      // A short docket in hand with a much longer one behind it. The layers used
      // to be stretched to the front card's height, so the long one overflowed
      // its slot — and every layer overflowed while the deck animated between
      // two heights.
      await tester.pumpWidget(
        _bed(const [
          FocusTask(
            id: 'short',
            domain: FocusDomain.commerce,
            title: 'Confirm the quotation',
            detail: 'One line.',
            timeLabel: '9:00 AM',
            actionLabel: 'Sign Off',
            doneMessage: 'Quotation approved.',
          ),
          FocusTask(
            id: 'long',
            domain: FocusDomain.wardrobe,
            title: 'Review the silk intake from Nuwa and tag every piece',
            detail:
                'Vendor: Nuwa Silks · 24 pieces against purchase order #AV-118 need to be counted, tagged, and hung before the evening shift takes the floor.',
            timeLabel: '4:15 PM',
            actionLabel: 'Sign Off',
            doneMessage: 'Intake signed off.',
          ),
        ]),
      );
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
    });

    testWidgets('signing off mid-animation never overflows a layer behind',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed(_tasks));

      // Clearing dockets in quick succession is what changes the deck's height
      // while it is still animating.
      for (var i = 0; i < _tasks.length; i++) {
        await tester.tap(_inFront(find.byType(FilledButton)));
        await tester.pump();
        await tester.pump(const Duration(milliseconds: 120));
      }
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
    });
  });
}
