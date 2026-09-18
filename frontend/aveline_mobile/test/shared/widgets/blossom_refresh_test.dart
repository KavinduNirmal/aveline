import 'dart:async';

import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/shared/widgets/blossom.dart';
import 'package:aveline_mobile/shared/widgets/blossom_refresh.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// Records the refresh revisions it sees, which is how the screens below a pull
/// know to re-seed.
class _RevisionProbe extends StatelessWidget {
  const _RevisionProbe({required this.seen});

  final List<int> seen;

  @override
  Widget build(BuildContext context) {
    seen.add(RefreshScope.revisionOf(context));

    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      // Deliberately shorter than the viewport: without the physics above there
      // would be nothing to over-scroll and the pull could never arm.
      children: const [SizedBox(height: 120)],
    );
  }
}

/// [animate] false is what the rest of the suite sees: reduced motion is on, so
/// the mark appears without unfurling or turning.
Widget _bed({
  required List<int> seen,
  Future<void> Function()? onRefresh,
  bool animate = false,
}) =>
    MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: !animate),
            child: BlossomRefresh(
              onRefresh: onRefresh,
              child: _RevisionProbe(seen: seen),
            ),
          ),
        ),
      ),
    );

/// Drags the list down far enough to arm the refresh, then runs the whole
/// sequence out: the settle, the refresh itself, and the mark's fade.
Future<void> _pullToRefresh(WidgetTester tester) async {
  await tester.fling(find.byType(ListView), const Offset(0, 300), 1000);
  await tester.pump();
  await tester.pump(const Duration(seconds: 1));
  await tester.pump(const Duration(seconds: 1));
  await tester.pump(const Duration(seconds: 1));
}

void main() {
  group('BlossomRefresh', () {
    testWidgets('arms on a pull when the content does not fill the screen',
        (tester) async {
      final seen = <int>[];
      var calls = 0;
      await tester.pumpWidget(
        _bed(seen: seen, onRefresh: () async => calls++),
      );

      // The list is deliberately shorter than the viewport: without
      // `AlwaysScrollableScrollPhysics` there would be nothing to over-scroll.
      await _pullToRefresh(tester);

      expect(calls, 1);
    });

    testWidgets('bumps the revision once the refresh completes',
        (tester) async {
      final seen = <int>[];
      await tester.pumpWidget(_bed(seen: seen, onRefresh: () async {}));

      expect(seen.last, 0);

      await _pullToRefresh(tester);

      // The screens below re-seed off this.
      expect(seen.last, 1);
    });

    testWidgets('holds the mark on screen while the work runs',
        (tester) async {
      final seen = <int>[];
      final release = Completer<void>();
      await tester.pumpWidget(
        _bed(seen: seen, onRefresh: () => release.future),
      );

      await tester.fling(find.byType(ListView), const Offset(0, 300), 1000);
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 300));

      // `Blossom` is the mark's glyph, and nothing else here draws one.
      expect(find.byType(Blossom), findsOneWidget);

      release.complete();
      await tester.pump();
      await tester.pump(const Duration(seconds: 1));

      expect(find.byType(Blossom), findsNothing);
    });

    testWidgets('a pull with nothing wired still resolves', (tester) async {
      final seen = <int>[];
      await tester.pumpWidget(_bed(seen: seen));

      await _pullToRefresh(tester);

      expect(seen.last, 1);
    });

    testWidgets('survives a refresh that throws, rather than crashing the pull',
        (tester) async {
      final seen = <int>[];
      await tester.pumpWidget(
        _bed(seen: seen, onRefresh: () async => throw StateError('offline')),
      );

      await _pullToRefresh(tester);

      // The screen stays usable and the gesture still reports completion.
      expect(seen.last, 1);
      expect(tester.takeException(), isNull);
    });
  });
}
