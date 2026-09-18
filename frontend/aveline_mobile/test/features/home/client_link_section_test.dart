import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/home/data/demo_client_highlights.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/client_highlight_tile.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/client_link_section.dart';
import 'package:aveline_mobile/features/home/presentation/widgets/client_status_ticker.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

/// Reduced motion is on, which is what the rest of the suite sees: the status
/// card rotates on its own otherwise, and a page that never stops animating is a
/// page the harness can never settle.
Widget _bed({
  List<ClientHighlight>? clients,
  Future<ClientHighlight?> Function(String fullName)? onCreateWalkIn,
}) =>
    MaterialApp(
      theme: AppTheme.light,
      home: Scaffold(
        body: Builder(
          builder: (context) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: true),
            child: SingleChildScrollView(
              child: ClientLinkSection(
                clients: clients ?? demoClientHighlights(),
                onCreateWalkIn: onCreateWalkIn,
              ),
            ),
          ),
        ),
      ),
    );

void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// The row itself, without the status card that sits under it: the card repeats
/// the first client's name and activity, so a bare `find.text` can match twice.
Finder _inRow(Finder finder) =>
    find.descendant(of: find.byType(ListView), matching: finder);

/// A router-backed bed, so a tap that navigates can be observed at the route it
/// lands on rather than at the toast it used to raise.
GoRouter _routerFor(List<ClientHighlight> clients) => GoRouter(
      initialLocation: AppRoutes.home,
      routes: [
        GoRoute(
          path: AppRoutes.home,
          builder: (context, state) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: true),
            child: Scaffold(
              body: SingleChildScrollView(
                child: ClientLinkSection(clients: clients),
              ),
            ),
          ),
        ),
        GoRoute(
          path: AppRoutes.customerPattern,
          builder: (context, state) => Scaffold(
            body: Text('Client ${state.pathParameters['customerId']}'),
          ),
        ),
      ],
    );

void main() {
  group('demoClientHighlights', () {
    test('offers more clients than the row shows', () {
      final clients = demoClientHighlights();

      expect(clients.length, greaterThan(ClientLinkSection.rowLimit));
      expect(clients.map((client) => client.id).toSet(), hasLength(clients.length));
      for (final client in clients) {
        expect(client.name, isNotEmpty);
        expect(client.activity, isNotEmpty);
      }
    });

    test('shortens names for under an avatar', () {
      const client = ClientHighlight(
        id: 'c',
        name: 'Eleanor Vane',
        tier: ClientTier.vip,
        activity: 'x',
      );

      expect(client.shortName, 'Eleanor V.');
      expect(client.initials, 'EV');
    });
  });

  group('ClientLinkSection', () {
    testWidgets('leads with the walk-in slot and caps the row at five',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed());

      expect(find.text('DIRECT CLIENT LINK'), findsOneWidget);
      expect(find.text('See all'), findsOneWidget);
      expect(find.text('Add New'), findsOneWidget);

      // The walk-in slot leads, then the clients by recency.
      expect(_inRow(find.text('Eleanor V.')), findsOneWidget);
      expect(_inRow(find.text('Isabella R.')), findsOneWidget);

      // The sixth and seventh clients are behind "See all", not in the row.
      expect(find.text('Nadia R.'), findsNothing);
      expect(find.text('Hiruni B.'), findsNothing);

      // The fifth is the last one the row holds; scrolling reaches it.
      await tester.drag(find.byType(ListView), const Offset(-240, 0));
      await tester.pumpAndSettle();
      expect(_inRow(find.text('Chamari S.')), findsOneWidget);
      expect(find.text('Nadia R.'), findsNothing);
    });

    testWidgets('carries the status behind the row in a card', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed());

      // The row is a glance at who is on the line; the sentence behind those
      // activity dots lives in the card under it, one client at a time.
      expect(find.byType(ClientStatusTicker), findsOneWidget);
      expect(
        find.text('Asked for the ivory silk to be held until Friday.'),
        findsOneWidget,
      );
    });

    testWidgets('shows each client value on their avatar', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed());

      // VIPs get the wine pill; everyone else gets the two-line level badge.
      expect(find.text('VIP'), findsWidgets);
      expect(find.text('LVL'), findsWidgets);
      expect(find.text('3'), findsWidgets);
      expect(find.text('2'), findsWidgets);
      expect(find.text('1'), findsWidgets);
    });

    testWidgets('See all opens the rest of the clients', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed());

      await tester.tap(find.text('See all'));
      await tester.pumpAndSettle();

      expect(find.text('Clients'), findsOneWidget);
      expect(find.text('Nadia Rahman'), findsOneWidget);
      expect(find.text('Hiruni Bandara'), findsOneWidget);
      expect(
        find.text('First visit at the Colombo store yesterday.'),
        findsOneWidget,
      );
    });

    testWidgets('a walk-in is added under the id the server returned', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      String? requestedName;

      await tester.pumpWidget(
        _bed(
          onCreateWalkIn: (fullName) async {
            requestedName = fullName;
            return ClientHighlight(
              id: 'abc-123',
              name: fullName,
              tier: null,
              activity: 'Walk-in added at the counter.',
            );
          },
        ),
      );

      await tester.tap(find.text('Add New'));
      await tester.pumpAndSettle();
      expect(find.text('Add a walk-in'), findsOneWidget);

      await tester.enterText(find.byType(TextField), 'Test Walkin');
      await tester.pump();
      await tester.tap(find.byKey(const Key('quick_add_client_submit')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(requestedName, 'Test Walkin');
      expect(_inRow(find.text('Test W.')), findsOneWidget);
      // The tile carries the server's id, not one invented here, so the profile
      // route resolves to the client that now exists.
      final tile = tester.widget<ClientHighlightTile>(
        find.ancestor(
          of: find.text('Test W.'),
          matching: find.byType(ClientHighlightTile),
        ),
      );
      expect(tile.client.id, 'abc-123');
      expect(find.textContaining('is on the client list'), findsOneWidget);
    });

    testWidgets('the walk-in form stays inert until a name is typed',
        (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(_bed());

      await tester.tap(find.text('Add New'));
      await tester.pumpAndSettle();

      FilledButton submit() => tester.widget<FilledButton>(
            find.byKey(const Key('quick_add_client_submit')),
          );

      expect(submit().onPressed, isNull);

      await tester.enterText(find.byType(TextField), 'Maria');
      await tester.pump();

      expect(submit().onPressed, isNotNull);
    });

    testWidgets('carries no activity dot, because none could ever clear', (
      tester,
    ) async {
      _usePhoneSurface(tester);

      await tester.pumpWidget(
        _bed(
          clients: const [
            ClientHighlight(
              id: 'c1',
              name: 'Sophia Liyanage',
              tier: ClientTier.level1,
              activity: 'Sent a photo of the saree she wants matched.',
            ),
          ],
        ),
      );

      // Decision D2(a): no read marker exists, so the tile ships no dot rather
      // than one that can never go out.
      expect(find.byKey(const Key('client_activity_dot')), findsNothing);
    });

    testWidgets('hides the badge for a client nobody has graded', (
      tester,
    ) async {
      _usePhoneSurface(tester);

      await tester.pumpWidget(
        _bed(
          clients: const [
            ClientHighlight(
              id: 'c1',
              name: 'Sophia Liyanage',
              tier: null,
              activity: 'Walk-in added at the counter.',
            ),
          ],
        ),
      );

      expect(find.text('LVL'), findsNothing);
      expect(find.text('VIP'), findsNothing);
    });

    testWidgets('a client tile opens their profile route', (tester) async {
      _usePhoneSurface(tester);
      final clients = demoClientHighlights();
      final router = _routerFor(clients);
      addTearDown(router.dispose);

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );

      await tester.tap(_inRow(find.text('Eleanor V.')));
      await tester.pumpAndSettle();

      // The profile is addressed by the highlight's own id, and the old
      // "not on mobile yet" dead end is gone.
      expect(find.text('Client ${clients.first.id}'), findsOneWidget);
      expect(find.textContaining('is not on mobile yet'), findsNothing);
    });

    testWidgets('a See all row opens the client profile route', (tester) async {
      _usePhoneSurface(tester);
      final clients = demoClientHighlights();
      final router = _routerFor(clients);
      addTearDown(router.dispose);

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );

      await tester.tap(find.text('See all'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Nadia Rahman'));
      await tester.pumpAndSettle();

      expect(find.text('Client client-nadia'), findsOneWidget);
      expect(find.textContaining('is not on mobile yet'), findsNothing);
    });
  });
}
