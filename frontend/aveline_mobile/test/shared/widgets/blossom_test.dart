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
}
