import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/domain/focus_task.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/today_strip.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

FocusTask _task(String id, String timeLabel, FocusDomain domain) => FocusTask(
      id: id,
      domain: domain,
      title: 'Task $id',
      detail: 'Detail',
      timeLabel: timeLabel,
      actionLabel: 'Sign Off',
      doneMessage: 'Done',
    );

Widget _bed(List<FocusTask> tasks) => MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(body: TodayStrip(tasks: tasks)),
    );

void main() {
  group('FocusTask.minutesOfDay', () {
    test('reads a clock label in either half of the day', () {
      expect(
        _task('a', '12:30 PM', FocusDomain.commerce).minutesOfDay,
        12 * 60 + 30,
      );
      expect(_task('b', '10:00 AM', FocusDomain.commerce).minutesOfDay, 10 * 60);
      expect(
        _task('c', '5:45 PM', FocusDomain.commerce).minutesOfDay,
        17 * 60 + 45,
      );
      // Midnight and noon are the two cases a naive `+12` gets wrong.
      expect(_task('d', '12:00 AM', FocusDomain.commerce).minutesOfDay, 0);
      expect(
        _task('e', '12:00 PM', FocusDomain.commerce).minutesOfDay,
        12 * 60,
      );
    });

    test('returns null when the label is not a time', () {
      expect(_task('a', 'Later today', FocusDomain.commerce).minutesOfDay, isNull);
      expect(_task('b', '', FocusDomain.commerce).minutesOfDay, isNull);
    });
  });

  group('TodayStrip', () {
    testWidgets('counts the deck by kind of work', (tester) async {
      await tester.pumpWidget(
        _bed([
          _task('a', '10:00 AM', FocusDomain.logistics),
          _task('b', '11:30 AM', FocusDomain.logistics),
          _task('c', '12:15 PM', FocusDomain.logistics),
          _task('d', '2:00 PM', FocusDomain.patron),
          _task('e', '2:30 PM', FocusDomain.patron),
          _task('f', '4:15 PM', FocusDomain.wardrobe),
          _task('g', '5:45 PM', FocusDomain.commerce),
        ]),
      );

      expect(find.text('3'), findsOneWidget);
      expect(find.text('2'), findsOneWidget);
      expect(find.text('1'), findsOneWidget);
      expect(find.text('clients arriving'), findsOneWidget);
      expect(find.text('deliveries today'), findsOneWidget);
      expect(find.text('intake pieces'), findsOneWidget);
    });

    testWidgets('names the next commitment by the kind of work it is',
        (tester) async {
      await tester.pumpWidget(
        _bed([
          // The pile cycles, so the first task in the list is not the one due
          // next; the strip reads the clock labels instead.
          _task('late', '5:30 PM', FocusDomain.patron),
          _task('early', '10:00 AM', FocusDomain.logistics),
        ]),
      );

      expect(find.text('10:00 AM'), findsOneWidget);
      expect(find.text('5:30 PM'), findsNothing);
      expect(find.text('Next delivery'), findsOneWidget);
    });

    testWidgets('labels the next commitment by its own kind', (tester) async {
      await tester.pumpWidget(
        _bed([_task('a', '9:30 AM', FocusDomain.patron)]),
      );

      expect(find.text('Next arrival'), findsOneWidget);
    });

    testWidgets('drops the next row when nothing left is timed', (tester) async {
      await tester.pumpWidget(
        _bed([_task('a', 'Later today', FocusDomain.patron)]),
      );

      // The counts still stand, but a footer reading "no times left" beside a
      // dash is noise rather than information.
      expect(find.text('clients arriving'), findsOneWidget);
      expect(find.text('Next arrival'), findsNothing);
      expect(find.text('Next delivery'), findsNothing);
      expect(find.text('—'), findsNothing);
    });

    testWidgets('drops the card when the deck is empty', (tester) async {
      await tester.pumpWidget(_bed(const []));

      // The focus section's own empty state carries that message; a card
      // counting zeroes above it would only repeat it.
      expect(find.text('TODAY AT A GLANCE'), findsNothing);
      expect(find.text('clients arriving'), findsNothing);
    });

    testWidgets('draws the figures at display size', (tester) async {
      await tester.pumpWidget(
        _bed([_task('a', '10:00 AM', FocusDomain.logistics)]),
      );

      // `DESIGN.md` reserves `display-lg` for greetings and key numbers; these
      // are the key numbers the page was missing.
      final figure = tester.widget<Text>(find.text('1'));

      expect(figure.style?.fontSize, AppTheme.textTheme.displayLarge?.fontSize);
      expect(figure.style?.fontFamily, startsWith('PlayfairDisplay'));
    });
  });
}
