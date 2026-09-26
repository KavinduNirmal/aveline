import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/client_status_ticker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const _clients = [
  ClientHighlight(
    id: 'a',
    name: 'Eleanor Vane',
    tier: ClientTier.vip,
    activity: 'Asked for the ivory silk to be held until Friday.',
  ),
  ClientHighlight(
    id: 'b',
    name: 'Isabella Ranatunga',
    tier: ClientTier.level3,
    activity: 'Replied about the evening fitting on Thursday.',
  ),
  ClientHighlight(
    id: 'c',
    name: 'Sophia Liyanage',
    tier: ClientTier.level1,
    activity: 'Sent a photo of the saree she wants matched.',
  ),
];

/// [animate] false is what the rest of the suite sees: ambient rotation is off
/// under reduced motion, which keeps a page that rotates forever from being
/// something the test harness can never settle.
Widget _bed({
  List<ClientHighlight> clients = _clients,
  bool animate = false,
}) =>
    MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: !animate),
            child: ClientStatusTicker(clients: clients),
          ),
        ),
      ),
    );

/// Moves past one full changeover: the hold, then the transition.
Future<void> _nextStatus(WidgetTester tester) async {
  await tester.pump(ClientStatusTicker.hold);
  await tester.pump(ClientStatusTicker.transition);
}

void main() {
  group('ClientStatusTicker', () {
    testWidgets('opens on the newest status, with the tier and the reason',
        (tester) async {
      await tester.pumpWidget(_bed());

      expect(find.text('Eleanor V.'), findsOneWidget);
      expect(
        find.text('Asked for the ivory silk to be held until Friday.'),
        findsOneWidget,
      );
      expect(find.text('VIP'), findsOneWidget);
    });

    testWidgets('holds that status under reduced motion', (tester) async {
      await tester.pumpWidget(_bed());

      await tester.pump(const Duration(seconds: 30));

      expect(find.text('Eleanor V.'), findsOneWidget);
      expect(find.text('Isabella R.'), findsNothing);
    });

    testWidgets('rotates through every client in the row', (tester) async {
      await tester.pumpWidget(_bed(animate: true));

      expect(find.text('Eleanor V.'), findsOneWidget);

      await _nextStatus(tester);
      expect(find.text('Isabella R.'), findsOneWidget);

      // There is no read marker, so a client with no flag is in the rotation
      // like everyone else rather than being filtered out by a fiction.
      await _nextStatus(tester);
      expect(find.text('Sophia L.'), findsOneWidget);

      // Back to the top of the set.
      await _nextStatus(tester);
      expect(find.text('Eleanor V.'), findsOneWidget);
    });

    testWidgets('tapping moves on without waiting for the hold',
        (tester) async {
      await tester.pumpWidget(_bed());

      await tester.tap(find.text('Eleanor V.'));
      await tester.pump(ClientStatusTicker.transition);

      expect(find.text('Isabella R.'), findsOneWidget);
    });

    testWidgets('draws nothing when there is nobody to show', (tester) async {
      await tester.pumpWidget(_bed(clients: const []));

      expect(find.text('Eleanor V.'), findsNothing);
      expect(find.byType(ClientStatusTicker), findsOneWidget);
    });
  });
}
