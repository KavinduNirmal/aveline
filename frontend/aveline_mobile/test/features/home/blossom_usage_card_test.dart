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

    test('reads the low-water line from the server threshold', () {
      // The server flags a low balance when the remaining allowance falls to
      // `lowBalanceThresholdPercent` (default 20), not at a fixed 0.8.
      const near = BlossomUsage(
        used: 85,
        allowance: 100,
        renewsOn: '1 October',
        lowBalanceThresholdPercent: 20,
      );
      const comfortable = BlossomUsage(
        used: 25,
        allowance: 100,
        renewsOn: '1 October',
        lowBalanceThresholdPercent: 20,
      );

      expect(near.isRunningLow, isTrue);
      expect(comfortable.isRunningLow, isFalse);
    });

    test('honours a wider low-water threshold', () {
      const usage = BlossomUsage(
        used: 25,
        allowance: 100,
        renewsOn: '1 October',
        lowBalanceThresholdPercent: 80,
      );

      expect(usage.isRunningLow, isTrue);
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

    testWidgets('offers the real purchase path to the owner', (tester) async {
      // The card no longer sends a toast claiming an approval request: the button opens the
      // top-up sheet, which lists the server's packs and creates a checkout.
      var opened = 0;
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Scaffold(
            body: BlossomUsageCard(
              usage: _usage,
              onRequestMore: () => opened += 1,
            ),
          ),
        ),
      );

      expect(find.text('Request additional blossoms'), findsOneWidget);

      await tester.tap(find.text('Request additional blossoms'));
      await tester.pump();

      expect(opened, 1);
    });

    testWidgets('disables the request when the caller may not purchase', (
      tester,
    ) async {
      await tester.pumpWidget(_bed(_usage));

      final button = tester.widget<OutlinedButton>(
        find.widgetWithText(OutlinedButton, 'Request additional blossoms'),
      );

      expect(button.onPressed, isNull);
    });

    testWidgets('shows a spinner while the balance is on its way', (
      tester,
    ) async {
      await tester.pumpWidget(
        const MaterialApp(
          home: Scaffold(body: BlossomUsageCard.loading()),
        ),
      );

      expect(find.byKey(const Key('blossom_usage_loading')), findsOneWidget);
      expect(find.text('0'), findsNothing);
      expect(find.text('of 0 left'), findsNothing);
    });

    testWidgets('shows an error and a retry instead of a zero', (tester) async {
      var retries = 0;
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: BlossomUsageCard.unavailable(
              errorMessage: 'Not available for your role.',
              onRetry: () => retries++,
            ),
          ),
        ),
      );

      expect(find.byKey(const Key('blossom_usage_error')), findsOneWidget);
      expect(find.text('0'), findsNothing);
      expect(find.text('Not available for your role.'), findsOneWidget);

      await tester.tap(find.text('Try again'));
      expect(retries, 1);
    });

    testWidgets('renders a fractional balance without truncating it', (
      tester,
    ) async {
      await tester.pumpWidget(
        _bed(
          const BlossomUsage(
            used: 168.5,
            allowance: 200,
            renewsOn: '1 October',
          ),
        ),
      );

      expect(find.text('31.5'), findsOneWidget);
      expect(find.text('of 200 left'), findsOneWidget);
    });
  });
}
