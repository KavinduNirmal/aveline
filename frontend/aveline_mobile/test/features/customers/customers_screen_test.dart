import 'package:aveline_mobile/core/providers/boutique_provider.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/data/demo_customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/customers/presentation/screens/customers_screen.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_alphabet_index.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_avatar.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_level_badge.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_level_row.dart';
import 'package:aveline_mobile/features/customers/presentation/widgets/customer_tile.dart';
import 'package:aveline_mobile/shared/widgets/filter_pill.dart';
import 'package:aveline_mobile/shared/widgets/search_overlay.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

/// The book the screen is driven against.
///
/// The demo repository rather than a stub: the screen test is about the screen's
/// wiring — sections, the index, the offsets a jump needs — and sectioning is
/// exactly what a stub would have to reimplement to be useful.
CustomerRepository _book({Duration delay = Duration.zero}) =>
    DemoCustomerRepository(bookDelay: delay);

/// Fails the first call, then serves the real book.
class _FlakyRepository implements CustomerRepository {
  int failures = 1;
  int calls = 0;

  @override
  Future<CustomerBook> fetchBook({
    CustomerQuery query = const CustomerQuery(),
  }) async {
    calls++;
    if (failures > 0) {
      failures--;
      throw Exception('The book is unavailable.');
    }
    return _book().fetchBook(query: query);
  }

  @override
  Future<CustomerDetail?> fetchCustomer(String id) => _book().fetchCustomer(id);
}

/// A phone-shaped viewport, so the book lays out the way it does on the devices
/// this app ships to.
void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// A boutique provider holding a name without touching the API.
class _FakeBoutique extends BoutiqueProvider {
  _FakeBoutique(this._name);

  final String? _name;

  @override
  String? get name => _name;
}

/// Reduced motion is on, matching the rest of the suite: the backdrop carries
/// ambient animation that would never settle otherwise.
Widget _wrap({
  String? boutiqueName,
  BoutiqueProvider? boutique,
  CustomerRepository? repository,
}) {
  final app = MaterialApp(
    theme: AppTheme.light,
    home: Builder(
      builder: (context) => MediaQuery(
        data: MediaQuery.of(context).copyWith(disableAnimations: true),
        child: Scaffold(
          body: CustomersScreen(
            boutiqueName: boutiqueName,
            repository: repository ?? _book(),
          ),
        ),
      ),
    ),
  );

  if (boutique == null) {
    return app;
  }
  return ChangeNotifierProvider<BoutiqueProvider>.value(
    value: boutique,
    child: app,
  );
}

Future<void> _pumpCustomers(
  WidgetTester tester, {
  String? boutiqueName = 'Ceylon Atelier',
  BoutiqueProvider? boutique,
  CustomerRepository? repository,
}) async {
  _usePhoneSurface(tester);
  await tester.pumpWidget(
    _wrap(
      boutiqueName: boutiqueName,
      boutique: boutique,
      repository: repository,
    ),
  );
  // The book is local, so it lands on the next frame; the extra frame lets the
  // header measurement settle before anything asks where a letter starts.
  await tester.pump();
  await tester.pump();
}

/// The title's rendered plain text, e.g. `Ceylon Atelier - Customers`.
String _titleText(WidgetTester tester) {
  return tester.widget<Text>(find.byKey(const Key('customers_title'))).data!;
}

String _fieldText(WidgetTester tester) {
  return tester
      .widget<TextField>(find.byKey(const Key('customers_search_field')))
      .controller!
      .text;
}

FilterPill _levelPill(WidgetTester tester, CustomerLevel level) =>
    tester.widget<FilterPill>(
      find.byKey(ValueKey('customer_level_${level.name}')),
    );

/// The tile the given client is printed in.
CustomerTile _tileFor(WidgetTester tester, String displayName) {
  return tester.widget<CustomerTile>(
    find.ancestor(
      of: find.text(displayName),
      matching: find.byType(CustomerTile),
    ),
  );
}

void main() {
  group('CustomersScreen title', () {
    testWidgets('names the boutique and the section in the brand serif', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      expect(_titleText(tester), 'Ceylon Atelier - Customers');

      final title = tester.widget<Text>(find.byKey(const Key('customers_title')));
      expect(title.style?.fontFamily, startsWith('PlayfairDisplay'));
      expect(title.style?.fontSize, AppTheme.textTheme.displayMedium?.fontSize);
    });

    testWidgets('reads the boutique name from the provider', (tester) async {
      await _pumpCustomers(tester, boutique: _FakeBoutique('Ceylon Atelier'));

      expect(_titleText(tester), 'Ceylon Atelier - Customers');
    });

    testWidgets('falls back to the brand name on a cold start', (tester) async {
      await _pumpCustomers(tester, boutiqueName: null);

      expect(_titleText(tester), 'Aveline - Customers');
    });

    testWidgets(
      'keeps a long name to one line so it fades instead of clipping',
      (tester) async {
        await _pumpCustomers(
          tester,
          boutiqueName: 'The Colombo Heritage Atelier and Silk House',
        );

        final title = tester.widget<Text>(
          find.byKey(const Key('customers_title')),
        );
        expect(title.maxLines, 1);
        expect(title.softWrap, isFalse);

        // The trailing fade is what stops the name being cut mid-letter. It is
        // the only `ShaderMask` above the title; the backdrop's masks sit
        // beside it, not over it.
        expect(
          find.ancestor(
            of: find.byKey(const Key('customers_title')),
            matching: find.byType(ShaderMask),
          ),
          findsOneWidget,
        );
      },
    );
  });

  group('CustomersScreen search', () {
    testWidgets('owns a book-scoped field rather than the global overlay', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      expect(find.byKey(const Key('customers_search_field')), findsOneWidget);
      expect(find.text('Search this client book...'), findsOneWidget);
      // The header's global search is a separate affordance and must not be
      // what this screen opens.
      expect(find.byType(SearchOverlay), findsNothing);
    });

    testWidgets('holds its own query and clears it again', (tester) async {
      await _pumpCustomers(tester);

      expect(find.byKey(const Key('customers_search_clear')), findsNothing);

      await tester.enterText(
        find.byKey(const Key('customers_search_field')),
        'eleanor',
      );
      await tester.pump();

      expect(_fieldText(tester), 'eleanor');
      expect(find.byKey(const Key('customers_search_clear')), findsOneWidget);

      await tester.tap(find.byKey(const Key('customers_search_clear')));
      await tester.pump();
      await tester.pump();

      expect(_fieldText(tester), isEmpty);
      expect(find.byKey(const Key('customers_search_clear')), findsNothing);
    });

    testWidgets('narrows the book to the clients the query matches', (
      tester,
    ) async {
      // `Anjali Perera` files under A, so she is on the first screen of the book;
      // the book is too long for a client further down to be built at all.
      await _pumpCustomers(tester);
      expect(find.text('Anjali Perera'), findsOneWidget);

      await tester.enterText(
        find.byKey(const Key('customers_search_field')),
        'nadia',
      );
      await tester.pump();
      await tester.pump();

      expect(find.byType(CustomerTile), findsOneWidget);
      expect(_tileFor(tester, 'Nadia Rahman').customer.displayName, 'Nadia Rahman');
      expect(find.byKey(const Key('customers_end_of_list')), findsOneWidget);
    });

    testWidgets('stays scrollable so the shell pull-to-refresh still arms', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      final scroll = tester.widget<CustomScrollView>(
        find.byKey(const Key('customers_scroll')),
      );
      expect(scroll.physics, isA<AlwaysScrollableScrollPhysics>());
    });
  });

  group('CustomersScreen levels', () {
    testWidgets('sits the boutique levels under the field', (tester) async {
      await _pumpCustomers(tester);

      expect(find.byType(CustomerLevelRow), findsOneWidget);
      for (final level in CustomerLevel.values) {
        // Scoped to the row: `VIP` is also what a VIP client's badge prints.
        expect(
          find.descendant(
            of: find.byType(CustomerLevelRow),
            matching: find.text(level.label),
          ),
          findsOneWidget,
        );
      }
    });

    testWidgets('narrows to one level at a time', (tester) async {
      await _pumpCustomers(tester);

      expect(_levelPill(tester, CustomerLevel.vip).selected, isFalse);

      await tester.tap(find.byKey(const ValueKey('customer_level_vip')));
      await tester.pump();
      await tester.pump();

      // Only the clients who hold the grade survive, and their own badges say so.
      final vip = tester.widgetList<CustomerTile>(find.byType(CustomerTile));
      expect(vip, isNotEmpty);
      for (final tile in vip) {
        expect(tile.customer.level, CustomerLevel.vip);
      }
      expect(find.text('Chamari Silva'), findsOneWidget);

      // A client holds one grade, so choosing another swaps rather than adds.
      await tester.tap(find.byKey(const ValueKey('customer_level_level2')));
      await tester.pump();
      await tester.pump();

      expect(_levelPill(tester, CustomerLevel.vip).selected, isFalse);
      expect(_levelPill(tester, CustomerLevel.level2).selected, isTrue);
      for (final tile in tester.widgetList<CustomerTile>(
        find.byType(CustomerTile),
      )) {
        expect(tile.customer.level, CustomerLevel.level2);
      }
      expect(find.text('Maya Tennakoon'), findsOneWidget);
    });

    testWidgets('clears the level when the chosen one is tapped again', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      await tester.tap(find.byKey(const ValueKey('customer_level_vip')));
      await tester.pump();
      await tester.pump();
      // Nobody the level row excludes is on screen while it is applied.
      expect(find.text('Anjali Perera'), findsNothing);

      await tester.tap(find.byKey(const ValueKey('customer_level_vip')));
      await tester.pump();
      await tester.pump();

      expect(_levelPill(tester, CustomerLevel.vip).selected, isFalse);
      expect(find.text('Anjali Perera'), findsOneWidget);
    });
  });

  group('CustomersScreen book', () {
    testWidgets('lays a client out like a contact list', (tester) async {
      await _pumpCustomers(tester);

      final tile = _tileFor(tester, 'Chamari Silva');
      final inTile = find.descendant(
        of: find.byWidget(tile),
        matching: find.byType(CustomerAvatar),
      );

      // The circle, the name, the id the shop keys them by, and their grade.
      expect(inTile, findsOneWidget);
      expect(
        find.descendant(
          of: find.byWidget(tile),
          matching: find.text(tile.customer.initials),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(
          of: find.byWidget(tile),
          matching: find.text(tile.customer.idLabel),
        ),
        findsOneWidget,
      );

      final badge = tester.widget<CustomerLevelBadge>(
        find.descendant(
          of: find.byWidget(tile),
          matching: find.byType(CustomerLevelBadge),
        ),
      );
      expect(badge.level, tile.customer.level);
    });

    testWidgets('files the book under letters, with the index offering them', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      // The first letter of the book is on screen with its header.
      expect(find.byKey(const ValueKey('customer_section_A')), findsOneWidget);

      final index = tester.widget<CustomerAlphabetIndex>(
        find.byType(CustomerAlphabetIndex),
      );
      expect(index.letters, contains('A'));
      expect(index.letters, contains('Z'));
      // Clients the boutique has no name for file under `#`, last.
      expect(index.letters.last, '#');
    });

    testWidgets('jumps to a letter that was nowhere near the viewport', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      // A letter at the far end of the book is not built at all until it is
      // reached, which is the whole reason the index computes offsets.
      expect(find.text('Zahra Nazeer'), findsNothing);

      await tester.tap(find.byKey(const ValueKey('customer_index_Z')));
      await tester.pump();
      await tester.pump();

      expect(find.text('Zahra Nazeer'), findsOneWidget);
      expect(find.byKey(const ValueKey('customer_section_Z')), findsOneWidget);
      // The jump landed at the end of the book, so its footer is in reach.
      expect(find.byKey(const Key('customers_end_of_list')), findsOneWidget);
    });

    testWidgets('marks the letter it has jumped to', (tester) async {
      await _pumpCustomers(tester);

      TextStyle? style(String letter) => tester
          .widget<Text>(find.byKey(ValueKey('customer_index_$letter')))
          .style;

      expect(style('A')?.fontWeight, FontWeight.w700);

      await tester.tap(find.byKey(const ValueKey('customer_index_M')));
      await tester.pump();
      await tester.pump();

      expect(style('M')?.fontWeight, FontWeight.w700);
      expect(style('A')?.fontWeight, FontWeight.w500);
    });

    testWidgets('opens the client profile when a row is tapped', (tester) async {
      _usePhoneSurface(tester);
      final router = GoRouter(
        initialLocation: AppRoutes.customers,
        routes: [
          GoRoute(
            path: AppRoutes.customers,
            // The dock tab is a shell body: in the app `MainShell` wraps it in
            // the Scaffold that the search field's Material comes from, and this
            // harness mounts the tab bare, so it has to stand in for that. The
            // same file's `_wrap` does it for every other test here.
            builder: (context, state) => Scaffold(
              body: CustomersScreen(
                boutiqueName: 'Ceylon Atelier',
                repository: _book(),
              ),
            ),
          ),
          GoRoute(
            path: AppRoutes.customerPattern,
            builder: (context, state) => Scaffold(
              body: Text('Profile ${state.pathParameters['customerId']}'),
            ),
          ),
        ],
      );
      addTearDown(router.dispose);

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );
      await tester.pump();
      await tester.pump();

      // The id travels in the location, which is the only thing the profile
      // screen resolves from.
      final id = _tileFor(tester, 'Chamari Silva').customer.id;

      await tester.tap(find.text('Chamari Silva'));
      await tester.pumpAndSettle();

      expect(find.text('Profile $id'), findsOneWidget);
    });

    testWidgets('offers a way back when the narrowing matches nobody', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      await tester.enterText(
        find.byKey(const Key('customers_search_field')),
        'nobody at all',
      );
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('customers_empty_state')), findsOneWidget);
      expect(find.text('No clients match "nobody at all"'), findsOneWidget);
      // Nothing to list, so nothing to index.
      expect(find.byType(CustomerAlphabetIndex), findsNothing);

      await tester.tap(find.byKey(const Key('customers_clear_all')));
      await tester.pump();
      await tester.pump();

      expect(_fieldText(tester), isEmpty);
      expect(find.text('Anjali Perera'), findsOneWidget);
    });

    testWidgets('shows the book arriving', (tester) async {
      await _pumpCustomers(
        tester,
        repository: _book(delay: const Duration(milliseconds: 300)),
      );

      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.byType(CustomerTile), findsNothing);

      await tester.pump(const Duration(milliseconds: 400));
      await tester.pump();

      expect(find.byType(CircularProgressIndicator), findsNothing);
      expect(find.byType(CustomerTile), findsWidgets);
    });

    testWidgets('reports a failed load and retries it', (tester) async {
      final repository = _FlakyRepository();
      await _pumpCustomers(tester, repository: repository);

      expect(find.byKey(const Key('customers_error')), findsOneWidget);
      expect(find.text('The book is unavailable.'), findsOneWidget);

      await tester.tap(find.byKey(const Key('customers_retry')));
      await tester.pump();
      await tester.pump();

      expect(repository.calls, 2);
      expect(find.byKey(const Key('customers_error')), findsNothing);
      expect(find.text('Anjali Perera'), findsOneWidget);
    });
  });

  group('CustomersScreen new customer', () {
    testWidgets('floats a round action for adding a client', (tester) async {
      await _pumpCustomers(tester);

      final fab = tester.widget<FloatingActionButton>(
        find.byKey(const Key('customers_new_fab')),
      );
      expect(fab.tooltip, 'New customer');
      // Material 3's default FAB is a rounded square; this one is a circle.
      expect(fab.shape, isA<CircleBorder>());
    });

    testWidgets('points at the walk-in slot while the form is unbuilt', (
      tester,
    ) async {
      await _pumpCustomers(tester);

      await tester.tap(find.byKey(const Key('customers_new_fab')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(
        find.text("New clients are added from Home's walk-in slot for now."),
        findsOneWidget,
      );

      await tester.pump(const Duration(seconds: 4));
    });
  });
}
