import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/catalog_product_screen.dart';
import 'package:aveline_mobile/shared/widgets/aurora_field.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';

CatalogProduct _piece({
  CatalogItemStatus status = CatalogItemStatus.available,
  int quantity = 4,
}) {
  return CatalogProduct(
    id: 'piece-001',
    organizationId: 'org-1',
    name: 'Handloom Silk Saree',
    category: 'Sarees',
    color: 'Wine',
    sizes: const ['36', '38', '40'],
    price: 24500,
    cost: 12000,
    quantity: quantity,
    status: status,
    isAvailable: status == CatalogItemStatus.available,
    createdAtUtc: DateTime.utc(2026, 9, 12),
    fabric: 'Raw silk',
    style: 'Contemporary festive',
    sku: 'AVL-0001-S',
    description:
        'Handwoven raw silk with a fine zari border, finished in the atelier.',
    sourcedFrom: 'Varanasi Weavers',
    discountMinPercent: 5,
    discountMaxPercent: 15,
    metadata: const {'Care': 'Dry clean only', 'Origin': 'Sri Lanka'},
    tags: const {'bridal'},
  );
}

/// Resolves pieces from a fixed pool, standing in for the inventory API.
class _StubRepository implements CatalogProductRepository {
  _StubRepository(this.pool, {this.failStatus = false, this.failSupply = false});

  final List<CatalogProduct> pool;

  /// Refuses a status change the way the API refuses one, so the screen's failure path is
  /// assertable without a network.
  final bool failStatus;

  /// Refuses a supply ticket, for the same reason.
  final bool failSupply;

  /// The statuses the screen asked the API for, in the order it asked.
  final List<CatalogItemStatus> requestedStatuses = [];

  /// The pieces a supply ticket was requested for.
  final List<String> requestedSupplies = [];

  @override
  Future<CatalogProduct?> fetchProduct(String id) async {
    for (final product in pool) {
      if (product.id == id) {
        return product;
      }
    }
    return null;
  }

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    return const CatalogProductPage(
      products: <CatalogProduct>[],
      hasMore: false,
    );
  }

  @override
  Future<CatalogProduct> updateStatus(
    String id,
    CatalogItemStatus status,
  ) async {
    if (failStatus) {
      throw StateError('the catalog refused the change');
    }

    requestedStatuses.add(status);
    final index = pool.indexWhere((piece) => piece.id == id);
    if (index < 0) {
      throw StateError('no piece $id');
    }

    // The server's row comes back, so the screen draws what the API now holds rather than
    // what it hoped to hold.
    final updated = pool[index].copyWith(
      status: status,
      isAvailable: status == CatalogItemStatus.available,
    );
    pool[index] = updated;
    return updated;
  }

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) async {
    if (failSupply) {
      throw StateError('the sourcing route refused the ticket');
    }

    requestedSupplies.add(piece.id);
    return 'src-1';
  }
}

/// A tall phone viewport, so the whole stack of cards is laid out and every
/// action can be tapped without scrolling. The layout is width-driven, so this
/// only removes scroll bookkeeping from the assertions.
void _useTallSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 7200);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// Pushed routes have to inherit reduced motion too, or the backdrop's ambient
/// animation never settles.
void _useReducedMotion(WidgetTester tester) {
  tester.platformDispatcher.accessibilityFeaturesTestValue =
      const FakeAccessibilityFeatures(disableAnimations: true);
  addTearDown(tester.platformDispatcher.clearAccessibilityFeaturesTestValue);
}

/// Reduced motion is on, matching the rest of the suite.
Widget _wrap({
  CatalogProduct? product,
  String productId = 'piece-001',
  CatalogProductRepository? repository,
}) {
  return MaterialApp(
    theme: AppTheme.light,
    home: Builder(
      builder: (context) => MediaQuery(
        data: MediaQuery.of(context).copyWith(disableAnimations: true),
        child: CatalogProductScreen(
          product: product,
          productId: productId,
          repository: repository ?? _StubRepository([_piece()]),
        ),
      ),
    ),
  );
}

/// Whether the action [key] is currently inert.
bool _actionEnabled(WidgetTester tester, Key key) {
  final inkWell = tester.widget<InkWell>(
    find.descendant(of: find.byKey(key), matching: find.byType(InkWell)),
  );
  return inkWell.onTap != null;
}

void main() {
  group('CatalogProductScreen pricing', () {
    testWidgets('leads with the retail price at display size', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      final price = tester.widget<Text>(
        find.byKey(const Key('catalog_detail_price')),
      );
      expect(price.data, 'Rs 24,500');
      expect(price.style?.fontSize, AppTheme.textTheme.displayLarge?.fontSize);
      expect(price.style?.fontFamily, startsWith('PlayfairDisplay'));
      expect(find.text('Retail price'), findsOneWidget);
    });

    testWidgets('shows cost, margin, discount range and floor price', (
      tester,
    ) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      expect(find.text('PRICING'), findsOneWidget);
      expect(find.text('Rs 12,000'), findsOneWidget);
      expect(find.text('Rs 12,500 · 51%'), findsOneWidget);
      expect(find.text('5% - 15%'), findsOneWidget);
      expect(find.text('Rs 20,825'), findsOneWidget);
    });
  });

  group('CatalogProductScreen data', () {
    testWidgets('tabulates every field the API carries', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      expect(find.text('ITEM'), findsOneWidget);
      for (final value in [
        'AVL-0001-S',
        'Sarees',
        'Wine',
        'Raw silk',
        'Contemporary festive',
        '36 · 38 · 40',
        '4',
        'Available',
        'Yes',
        'Varanasi Weavers',
        'org-1',
      ]) {
        expect(
          find.text(value),
          findsWidgets,
          reason: '$value missing from table',
        );
      }

      // The boutique's own notes get their own card.
      expect(find.text('BOUTIQUE NOTES'), findsOneWidget);
      expect(find.text('Care'), findsOneWidget);
      expect(find.text('Dry clean only'), findsOneWidget);

      expect(find.text('DESCRIPTION'), findsOneWidget);
      expect(
        find.text(
          'Handwoven raw silk with a fine zari border, finished in the atelier.',
        ),
        findsOneWidget,
      );
    });

    testWidgets('wears the workflow state on the photograph', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(product: _piece(status: CatalogItemStatus.onHold)),
      );

      expect(find.text('On hold'), findsWidgets);
    });
  });

  group('CatalogProductScreen actions', () {
    testWidgets('creates a hold, then refuses a second one', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      expect(_actionEnabled(tester, const Key('catalog_action_hold')), isTrue);

      await tester.tap(find.byKey(const Key('catalog_action_hold')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(
        find.text('Hold created for Handloom Silk Saree.'),
        findsOneWidget,
      );
      expect(_actionEnabled(tester, const Key('catalog_action_hold')), isFalse);
      // A held piece can still be sold or taken off the floor.
      expect(
        _actionEnabled(tester, const Key('catalog_action_sold_out')),
        isTrue,
      );

      // Let the toast expire so no timer outlives the test.
      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('sends the status change to the API and draws the row it answers with', (
      tester,
    ) async {
      _useTallSurface(tester);
      final repository = _StubRepository([_piece()]);
      await tester.pumpWidget(_wrap(product: _piece(), repository: repository));

      await tester.tap(find.byKey(const Key('catalog_action_hold')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      // The button is not the record: the API was asked, and the screen drew what came
      // back rather than the status it hoped for.
      expect(repository.requestedStatuses, [CatalogItemStatus.onHold]);
      expect(find.text('On hold'), findsWidgets);

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('says the piece is unchanged when the API refuses the status', (
      tester,
    ) async {
      _useTallSurface(tester);
      final repository = _StubRepository([_piece()], failStatus: true);
      await tester.pumpWidget(_wrap(product: _piece(), repository: repository));

      await tester.tap(find.byKey(const Key('catalog_action_sold_out')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(
        find.text('Could not update Handloom Silk Saree. Nothing was changed.'),
        findsOneWidget,
      );
      // The status is the server's, so a refusal leaves the piece available and its
      // actions live rather than drawing a change that never happened.
      expect(find.text('Sold out'), findsNothing);
      expect(_actionEnabled(tester, const Key('catalog_action_hold')), isTrue);

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('does not claim a supply ticket the API refused', (tester) async {
      _useTallSurface(tester);
      final repository = _StubRepository([_piece()], failSupply: true);
      await tester.pumpWidget(_wrap(product: _piece(), repository: repository));

      await tester.tap(find.byKey(const Key('catalog_action_supply')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(
        find.text('Could not log a supply request for Handloom Silk Saree.'),
        findsOneWidget,
      );
      // The action stays live for another try rather than reading as done.
      expect(_actionEnabled(tester, const Key('catalog_action_supply')), isTrue);

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('marks the piece sold out and closes the other actions', (
      tester,
    ) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      await tester.tap(find.byKey(const Key('catalog_action_sold_out')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(find.text('Handloom Silk Saree marked sold out.'), findsOneWidget);
      expect(_actionEnabled(tester, const Key('catalog_action_hold')), isFalse);
      expect(
        _actionEnabled(tester, const Key('catalog_action_unavailable')),
        isFalse,
      );
      expect(
        _actionEnabled(tester, const Key('catalog_action_sold_out')),
        isFalse,
      );

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('logs a supply request and marks it done', (tester) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece(quantity: 1)));

      expect(
        _actionEnabled(tester, const Key('catalog_action_supply')),
        isTrue,
      );

      await tester.tap(find.byKey(const Key('catalog_action_supply')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(
        find.text('Supply request logged for Handloom Silk Saree.'),
        findsOneWidget,
      );
      expect(find.text('Supply requested'), findsOneWidget);
      expect(
        _actionEnabled(tester, const Key('catalog_action_supply')),
        isFalse,
      );

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('does not claim Aveline was told when nothing was sent', (
      tester,
    ) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      expect(
        _actionEnabled(tester, const Key('catalog_action_mention')),
        isTrue,
      );

      await tester.tap(find.byKey(const Key('catalog_action_mention')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      // Nothing reaches the Salon yet, so the screen says so rather than reporting a
      // hand-off that never happened, and the action stays live for when it ships.
      expect(
        find.text('Mentioning a piece to Aveline is not available yet.'),
        findsOneWidget,
      );
      expect(find.text('Mentioned to Aveline'), findsNothing);
      expect(
        _actionEnabled(tester, const Key('catalog_action_mention')),
        isTrue,
      );

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('gives the Aveline action the brand atmosphere', (
      tester,
    ) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(product: _piece()));

      Finder washInTile() => find.descendant(
        of: find.byKey(const Key('catalog_action_mention')),
        matching: find.byType(BlossomWash),
      );

      expect(washInTile(), findsOneWidget);

      // The action is not spent by asking: nothing was sent, so it keeps its atmosphere
      // and stays live rather than going inert on a hand-off that did not happen.
      await tester.tap(find.byKey(const Key('catalog_action_mention')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 750));

      expect(washInTile(), findsOneWidget);

      await tester.pump(const Duration(seconds: 4));
    });

    testWidgets('offers no actions a sold-out piece cannot take', (
      tester,
    ) async {
      _useTallSurface(tester);
      await tester.pumpWidget(
        _wrap(product: _piece(status: CatalogItemStatus.soldOut)),
      );

      expect(_actionEnabled(tester, const Key('catalog_action_hold')), isFalse);
      expect(
        _actionEnabled(tester, const Key('catalog_action_unavailable')),
        isFalse,
      );
      expect(
        _actionEnabled(tester, const Key('catalog_action_sold_out')),
        isFalse,
      );
      // Restocking is always worth asking for.
      expect(
        _actionEnabled(tester, const Key('catalog_action_supply')),
        isTrue,
      );
    });
  });

  group('CatalogProductScreen resolution', () {
    testWidgets('resolves the piece from the id alone', (tester) async {
      _useTallSurface(tester);
      // No piece seeded: this is the deep-link and router-refresh case.
      await tester.pumpWidget(_wrap());
      await tester.pump();

      expect(find.text('Handloom Silk Saree'), findsOneWidget);
    });

    testWidgets('renders every piece the demo repository serves', (
      tester,
    ) async {
      _useTallSurface(tester);
      final repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
      final page = await tester.runAsync(
        () => repository.fetchPage(
          page: 0,
          pageSize: 100,
          query: const CatalogProductQuery(),
        ),
      );

      expect(page, isNotNull);
      expect(page!.products, isNotEmpty);
      for (final piece in page.products) {
        await tester.pumpWidget(
          MaterialApp(
            theme: AppTheme.light,
            home: Builder(
              builder: (context) => MediaQuery(
                data: MediaQuery.of(context).copyWith(disableAnimations: true),
                child: CatalogProductScreen(
                  product: piece,
                  productId: piece.id,
                  repository: repository,
                ),
              ),
            ),
          ),
        );
        await tester.pump();
        expect(
          tester.takeException(),
          isNull,
          reason: 'rendering ${piece.id} threw',
        );
      }
    });

    testWidgets('stands on the id with a retry when it cannot be found', (
      tester,
    ) async {
      _useTallSurface(tester);
      await tester.pumpWidget(_wrap(productId: 'piece-999'));
      await tester.pump();

      expect(find.text('Piece not found'), findsOneWidget);
      expect(find.textContaining('piece-999'), findsOneWidget);
      expect(find.byKey(const Key('catalog_product_retry')), findsOneWidget);
    });

    testWidgets('survives a refresh from the router listenable', (
      tester,
    ) async {
      _useTallSurface(tester);
      _useReducedMotion(tester);

      final refresh = ChangeNotifier();
      addTearDown(refresh.dispose);
      Map<String, String>? seenParams;

      final router = GoRouter(
        initialLocation: '/catalog',
        // The app refreshes the router whenever Clerk, the profile or
        // onboarding notifies. This is that refresh.
        refreshListenable: refresh,
        routes: [
          GoRoute(
            path: '/catalog',
            builder: (context, state) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () =>
                      context.push(AppRoutes.catalogProduct('piece-001')),
                  child: const Text('open piece'),
                ),
              ),
            ),
          ),
          GoRoute(
            path: AppRoutes.catalogProductPattern,
            builder: (context, state) {
              seenParams = state.pathParameters;
              return CatalogProductScreen(
                productId: state.pathParameters['productId'] ?? '',
                repository: _StubRepository([_piece()]),
              );
            },
          ),
        ],
      );

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );
      await tester.pump();

      await tester.tap(find.text('open piece'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pump();
      expect(find.text('Handloom Silk Saree'), findsOneWidget);

      refresh.notifyListeners();
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pump();

      // The id survives the refresh, and the piece is resolved from it rather
      // than from the route extra that does not survive.
      expect(seenParams?['productId'], 'piece-001');
      expect(find.text('Piece not found'), findsNothing);
      expect(find.text('Handloom Silk Saree'), findsOneWidget);
    });

    testWidgets('back returns to the screen that opened it', (tester) async {
      _useTallSurface(tester);
      _useReducedMotion(tester);

      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: Builder(
            builder: (context) => Scaffold(
              body: Center(
                child: TextButton(
                  onPressed: () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      builder: (_) => CatalogProductScreen(
                        product: _piece(),
                        productId: 'piece-001',
                        repository: _StubRepository([_piece()]),
                      ),
                    ),
                  ),
                  child: const Text('open piece'),
                ),
              ),
            ),
          ),
        ),
      );

      await tester.tap(find.text('open piece'));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      expect(find.byType(CatalogProductScreen), findsOneWidget);

      await tester.tap(find.byKey(const Key('catalog_product_back')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.byType(CatalogProductScreen), findsNothing);
      expect(find.text('open piece'), findsOneWidget);
    });
  });
}
