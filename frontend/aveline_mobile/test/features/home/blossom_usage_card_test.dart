import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/domain/blossom_usage.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/blossom_usage_card.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const _usage = BlossomUsage(used: 168, allowance: 200, renewsOn: '1 October');

Widget _bed(BlossomUsage usage) => MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(body: BlossomUsageCard(usage: usage)),
    );

void main() {
  group('BlossomUsage', () {
    test('counts what is left', () {
      expect(_usage.remaining, 32);
      expect(_usage.usedFraction, closeTo(0.84, 0.001));
      expect(_usage.isRunningLow, isTrue);
    });

    test('clamps when usage runs past the allowance', () {
      const over = BlossomUsage(used: 240, allowance: 200, renewsOn: '1 October');

      expect(over.remaining, 0);
      expect(over.usedFraction, 1.0);
    });

    test('is only running low near the allowance', () {
      const comfortable = BlossomUsage(
        used: 100,
        allowance: 200,
        renewsOn: '1 October',
      );
      const nearly = BlossomUsage(
        used: 160,
        allowance: 200,
        renewsOn: '1 October',
      );

      expect(comfortable.isRunningLow, isFalse);
      expect(nearly.isRunningLow, isTrue);
    });

    test('a zero allowance does not divide by zero', () {
      const empty = BlossomUsage(used: 0, allowance: 0, renewsOn: '1 October');

      expect(empty.usedFraction, 0);
      expect(empty.remaining, 0);
    });
  });

  group('BlossomUsageCard', () {
    testWidgets('leads with what is left, and says what has been spent',
        (tester) async {
      await tester.pumpWidget(_bed(_usage));

      expect(find.text('BLOSSOM USAGE'), findsOneWidget);
      expect(find.text('32'), findsOneWidget);
      expect(find.text('of 200 left'), findsOneWidget);
      expect(
        find.text('168 used this cycle · renews 1 October'),
        findsOneWidget,
      );
    });

    testWidgets('draws the remaining figure at display size', (tester) async {
      await tester.pumpWidget(_bed(_usage));

      final figure = tester.widget<Text>(find.text('32'));

      expect(figure.style?.fontSize, AppTheme.textTheme.displayLarge?.fontSize);
      expect(figure.style?.fontFamily, startsWith('PlayfairDisplay'));
    });

    testWidgets('offers the request the owner has to approve', (tester) async {
      await tester.pumpWidget(_bed(_usage));

      expect(find.text('Request additional blossoms'), findsOneWidget);

      await tester.tap(find.text('Request additional blossoms'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      // UI only: the card says what would happen, and sends nothing.
      expect(
        find.text('Request sent to the owner for approval.'),
        findsOneWidget,
      );
    });
  });
}
