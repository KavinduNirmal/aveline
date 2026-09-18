import 'package:aveline_mobile/shared/widgets/blossom.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  /// The painted mark, not the widget's outer box: inside a tight parent the outer box is
  /// the parent's size, while the mark itself must still be [size].
  Size paintedSize(WidgetTester tester) => tester.getSize(
        find.descendant(
          of: find.byType(Blossom),
          matching: find.byType(CustomPaint),
        ),
      );

  testWidgets('honours size inside a tight box', (tester) async {
    // Regression: `Container(width: 56, height: 56, child: Blossom(size: 30))` used to hand
    // down tight constraints that overrode `size`, so the mark painted at 56 and filled the
    // whole circle (the dock's centre launcher).
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: Center(
            child: SizedBox(
              width: 56,
              height: 56,
              child: Blossom(size: 24),
            ),
          ),
        ),
      ),
    );

    expect(paintedSize(tester), const Size(24, 24));
  });

  testWidgets('honours size when constraints are loose', (tester) async {
    await tester.pumpWidget(
      const MaterialApp(
        home: Scaffold(
          body: Center(
            child: Blossom(size: 40),
          ),
        ),
      ),
    );

    expect(paintedSize(tester), const Size(40, 40));
  });

  group('petalReveal', () {
    testWidgets('draws fully open when no reveal is given', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(home: Center(child: Blossom(size: 48))),
      );

      expect(tester.widget<Blossom>(find.byType(Blossom)).petalReveal, isNull);
    });

    testWidgets('paints part-open petals without error', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Center(
            child: Blossom(
              size: 48,
              petalReveal: List<double>.filled(8, 0.5),
            ),
          ),
        ),
      );

      expect(tester.takeException(), isNull);
    });

    test('requires one reveal value per petal', () {
      expect(
        () => Blossom(petalReveal: const [1.0, 1.0]),
        throwsAssertionError,
      );
      expect(
        () => Blossom(petalReveal: List<double>.filled(9, 1)),
        throwsAssertionError,
      );
    });
  });
}
