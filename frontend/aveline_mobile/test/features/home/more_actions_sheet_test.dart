import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/more_actions_sheet.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

/// A tiny host screen whose only job is to open the More sheet under a router,
/// so a row that navigates can be observed at the route it reaches.
GoRouter _router() => GoRouter(
      initialLocation: AppRoutes.home,
      routes: [
        GoRoute(
          path: AppRoutes.home,
          builder: (context, state) => Scaffold(
            body: Builder(
              builder: (context) => TextButton(
                onPressed: () => showMoreActionsSheet(context),
                child: const Text('More'),
              ),
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.settings,
          builder: (context, state) =>
              const Scaffold(body: Text('Settings destination')),
        ),
        GoRoute(
          path: AppRoutes.notifications,
          builder: (context, state) =>
              const Scaffold(body: Text('Notifications destination')),
        ),
      ],
    );

Future<void> _openSheet(WidgetTester tester, GoRouter router) async {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);

  await tester.pumpWidget(
    MaterialApp.router(theme: AppTheme.light, routerConfig: router),
  );
  await tester.tap(find.text('More'));
  await tester.pumpAndSettle();
}

void main() {
  group('MoreActionsSheet', () {
    testWidgets('the Settings row reaches the settings route', (tester) async {
      final router = _router();
      addTearDown(router.dispose);

      await _openSheet(tester, router);
      expect(find.text('More actions'), findsOneWidget);

      await tester.tap(find.text('Settings'));
      await tester.pumpAndSettle();

      expect(find.text('Settings destination'), findsOneWidget);
      expect(find.textContaining('is not on mobile yet'), findsNothing);
    });

    testWidgets('a row with no slice still says so', (tester) async {
      final router = _router();
      addTearDown(router.dispose);

      await _openSheet(tester, router);

      await tester.tap(find.text('New order'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.text('Order taking is not on mobile yet.'), findsOneWidget);
    });
  });
}
