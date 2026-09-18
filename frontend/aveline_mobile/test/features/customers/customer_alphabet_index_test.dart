import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_alphabet_index.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const List<String> _letters = ['A', 'B', 'C', 'D', 'E'];

Future<void> _pumpIndex(
  WidgetTester tester, {
  List<String> letters = _letters,
  String? activeLetter,
  ValueChanged<String>? onLetterSelected,
}) async {
  await tester.pumpWidget(
    MaterialApp(
      theme: AppTheme.light,
      home: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: Scaffold(
            body: Align(
              alignment: Alignment.centerRight,
              child: CustomerAlphabetIndex(
                letters: letters,
                activeLetter: activeLetter,
                onLetterSelected: onLetterSelected ?? (_) {},
              ),
            ),
          ),
        ),
      ),
    ),
  );
}

TextStyle? _letterStyle(WidgetTester tester, String letter) =>
    tester.widget<Text>(find.byKey(ValueKey('customer_index_$letter'))).style;

void main() {
  group('CustomerAlphabetIndex.letterAt', () {
    test('turns a position into a letter', () {
      const cell = CustomerAlphabetIndex.cellHeight;

      expect(CustomerAlphabetIndex.letterAt(0, _letters), 'A');
      expect(CustomerAlphabetIndex.letterAt(cell + 1, _letters), 'B');
      expect(CustomerAlphabetIndex.letterAt(2.5 * cell, _letters), 'C');
    });

    test('clamps at both ends of the strip', () {
      // A drag that runs off the end of the strip should land on the last
      // letter rather than on nothing: dragging is how the list is scanned, and
      // it routinely overshoots.
      expect(CustomerAlphabetIndex.letterAt(-30, _letters), 'A');
      expect(CustomerAlphabetIndex.letterAt(9999, _letters), 'E');
    });

    test('has nothing to say about a book with no letters', () {
      expect(CustomerAlphabetIndex.letterAt(10, const []), isNull);
    });
  });

  group('CustomerAlphabetIndex', () {
    testWidgets('offers a letter per section, in the book order', (
      tester,
    ) async {
      await _pumpIndex(tester);

      for (final letter in _letters) {
        expect(find.byKey(ValueKey('customer_index_$letter')), findsOneWidget);
      }
      // The strip is narrower than the list it sits beside.
      expect(
        tester.getSize(find.byType(CustomerAlphabetIndex)).width,
        CustomerAlphabetIndex.width,
      );
    });

    testWidgets('draws nothing for a book with no letters', (tester) async {
      await _pumpIndex(tester, letters: const []);

      expect(find.byType(Text), findsNothing);
    });

    testWidgets('reports the letter that was tapped', (tester) async {
      final tapped = <String>[];
      await _pumpIndex(tester, onLetterSelected: tapped.add);

      await tester.tap(find.byKey(const ValueKey('customer_index_C')));
      await tester.pump();

      expect(tapped, ['C']);
    });

    testWidgets('scans the strip when it is dragged along', (tester) async {
      final tapped = <String>[];
      await _pumpIndex(tester, onLetterSelected: tapped.add);

      final strip = find.byType(CustomerAlphabetIndex);
      final top = tester.getTopLeft(strip);
      const cell = CustomerAlphabetIndex.cellHeight;

      // A drag from the first letter to the last reports as it moves, which is
      // what makes a long book scannable rather than tappable letter by letter.
      final gesture = await tester.startGesture(top + const Offset(12, 2));
      for (var i = 1; i < _letters.length; i++) {
        await gesture.moveTo(top + Offset(12, i * cell + 2));
        await tester.pump();
      }
      await gesture.up();
      await tester.pump();

      expect(tapped.last, 'E');
      expect(
        tapped.length,
        greaterThan(1),
        reason: 'a drag reports the letters it passes, not only where it stopped',
      );
    });

    testWidgets('marks the letter at the top of the list', (tester) async {
      await _pumpIndex(tester, activeLetter: 'B');

      expect(_letterStyle(tester, 'B')?.fontWeight, FontWeight.w700);
      expect(_letterStyle(tester, 'A')?.fontWeight, FontWeight.w500);
      expect(
        _letterStyle(tester, 'B')?.color,
        isNot(_letterStyle(tester, 'C')?.color),
      );
    });
  });
}
