import 'package:aveline_mobile/core/auth/clerk_bootstrap.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/shared/widgets/aveline_loading_screen.dart';
import 'package:aveline_mobile/shared/widgets/blossom.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  const networkFailure = ClerkBootstrapFailure(
    isNetwork: true,
    detail: 'ClientException with SocketException: Failed host lookup',
  );
  const otherFailure = ClerkBootstrapFailure(
    isNetwork: false,
    detail: 'Invalid authentication',
  );

  Widget host({
    AvelineBootStatus status = AvelineBootStatus.preparing,
    ClerkBootstrapFailure? failure,
    VoidCallback? onRetry,
  }) {
    return MaterialApp(
      theme: AppTheme.light,
      home: AvelineLoadingScreen(
        status: status,
        failure: failure,
        onRetry: onRetry,
      ),
    );
  }

  /// Settles the copy switcher without waiting on the ambient petal loop, which
  /// never stops.
  Future<void> settleCopy(WidgetTester tester) async {
    await tester.pump(const Duration(milliseconds: 500));
    await tester.pump();
  }

  group('AvelineLoadingScreen', () {
    testWidgets('shows the mark and the preparing line while booting', (
      tester,
    ) async {
      await tester.pumpWidget(host());
      await tester.pump(const Duration(milliseconds: 300));

      expect(find.byType(Blossom), findsOneWidget);
      expect(find.text('Preparing your salon'), findsOneWidget);
      expect(find.text('Aveline is ready'), findsNothing);
    });

    testWidgets('unfurls the petals instead of fading the mark in', (
      tester,
    ) async {
      await tester.pumpWidget(host());

      Blossom mark() => tester.widget<Blossom>(find.byType(Blossom));
      expect(mark().petalReveal, everyElement(0.0));

      await tester.pump(const Duration(milliseconds: 1400));

      expect(mark().petalReveal, everyElement(1.0));
    });

    testWidgets('says Aveline is ready once booted', (tester) async {
      await tester.pumpWidget(host());
      await tester.pumpWidget(host(status: AvelineBootStatus.ready));
      await settleCopy(tester);

      expect(find.text('Aveline is ready'), findsOneWidget);
      expect(find.text('Preparing your salon'), findsNothing);
    });

    testWidgets('explains a network failure and offers a retry', (tester) async {
      var retries = 0;
      await tester.pumpWidget(
        host(
          status: AvelineBootStatus.failed,
          failure: networkFailure,
          onRetry: () => retries++,
        ),
      );
      await settleCopy(tester);

      expect(find.text("Aveline can't reach the salon"), findsOneWidget);
      expect(
        find.text('Check your connection, then try again.'),
        findsOneWidget,
      );

      await tester.tap(find.byKey(const Key('loading_retry_button')));
      await tester.pump();

      expect(retries, 1);
    });

    testWidgets('separates a non-network failure from a connection problem', (
      tester,
    ) async {
      await tester.pumpWidget(
        host(
          status: AvelineBootStatus.failed,
          failure: otherFailure,
          onRetry: () {},
        ),
      );
      await settleCopy(tester);

      expect(find.text("Aveline couldn't start"), findsOneWidget);
      expect(
        find.text('Something went wrong while preparing the salon.'),
        findsOneWidget,
      );
    });

    testWidgets('hides the retry button when the caller cannot retry', (
      tester,
    ) async {
      await tester.pumpWidget(
        host(status: AvelineBootStatus.failed, failure: networkFailure),
      );
      await settleCopy(tester);

      expect(find.byKey(const Key('loading_retry_button')), findsNothing);
    });

    testWidgets('fits a small phone screen in every state', (tester) async {
      await tester.binding.setSurfaceSize(const Size(360, 640));
      addTearDown(() => tester.binding.setSurfaceSize(null));

      for (final status in AvelineBootStatus.values) {
        await tester.pumpWidget(
          host(
            status: status,
            failure: networkFailure,
            onRetry: () {},
          ),
        );
        await settleCopy(tester);

        expect(tester.takeException(), isNull, reason: 'overflowed in $status');
      }
    });

    testWidgets('shows the finished frame when animations are disabled', (
      tester,
    ) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Builder(
            builder: (context) => MediaQuery(
              data: MediaQuery.of(context).copyWith(disableAnimations: true),
              child: const AvelineLoadingScreen(),
            ),
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 16));

      expect(find.byType(Blossom), findsOneWidget);
      expect(
        tester.widget<Blossom>(find.byType(Blossom)).petalReveal,
        everyElement(1.0),
      );
    });
  });
}
