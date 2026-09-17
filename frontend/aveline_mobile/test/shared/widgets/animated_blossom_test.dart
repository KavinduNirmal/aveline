import 'package:aveline_mobile/shared/widgets/animated_blossom.dart';
import 'package:aveline_mobile/shared/widgets/blossom.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AnimatedBlossom', () {
    testWidgets('renders blossom mark and button', (tester) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(
            body: Center(
              child: AnimatedBlossom(),
            ),
          ),
        ),
      );

      expect(find.byKey(const Key('animated_blossom_button')), findsOneWidget);
      expect(find.byType(Blossom), findsOneWidget);
    });

    testWidgets('tapping animated blossom calls onTap callback', (tester) async {
      var tapped = false;

      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: Center(
              child: AnimatedBlossom(
                onTap: () => tapped = true,
              ),
            ),
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('animated_blossom_button')));
      await tester.pump();

      expect(tapped, isTrue);
    });
  });
}
