import 'package:aveline_mobile/core/providers/boutique_provider.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_filters.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_tag.dart';
import 'package:aveline_mobile/features/catalog/presentation/catalog_colors.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/catalog_filter_screen.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/catalog_product_screen.dart';
import 'package:aveline_mobile/features/catalog/presentation/screens/catalog_screen.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/catalog_product_card.dart';
import 'package:aveline_mobile/features/catalog/presentation/widgets/catalog_tag_row.dart';
import 'package:aveline_mobile/shared/widgets/filter_pill.dart';
import 'package:aveline_mobile/shared/widgets/search_overlay.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

/// Two of the shop's own tags, so the row's behaviour does not depend on the
/// placeholder vocabulary.
const List<CatalogTag> _tags = [
  CatalogTag(id: 'bridal', label: 'Bridal'),
  CatalogTag(id: 'festive', label: 'Festive'),
];

/// A page source over a fixed pool, narrowing the same way the real repository
/// will: by name, by shop tag, and by category.
class _StubProductRepository implements CatalogProductRepository {
  _StubProductRepository(this.pool);

  final List<CatalogProduct> pool;

  // A read-only fake: the grid never takes an action, so a mutation has nothing to say.
  @override
  Future<CatalogProduct> updateStatus(String id, CatalogItemStatus status) =>
      throw UnimplementedError('this fake only reads');

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) => throw UnimplementedError('this fake only reads');

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    var matching = pool;

    final search = query.search.trim().toLowerCase();
    if (search.isNotEmpty) {
      matching = matching
          .where((product) => product.name.toLowerCase().contains(search))
          .toList();
    }
    if (query.tagIds.isNotEmpty) {
      matching = matching
          .where((product) => product.tags.any(query.tagIds.contains))
          .toList();
    }
    final categories = query.filters.selectedIn(CatalogFilterGroup.category);
    if (categories.isNotEmpty) {
      matching = matching
          .where((product) => categories.contains(product.category))
          .toList();
    }

    final start = page * pageSize;
    if (start >= matching.length) {
      return const CatalogProductPage(
        products: <CatalogProduct>[],
        hasMore: false,
      );
    }
    final end = start + pageSize > matching.length
        ? matching.length
        : start + pageSize;
    return CatalogProductPage(
      products: matching.sublist(start, end),
      hasMore: end < matching.length,
    );
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async {
    for (final product in pool) {
      if (product.id == id) {
        return product;
      }
    }
    return null;
  }
}

CatalogProduct _piece(
  String id, {
  String? name,
  String description = 'Handwoven raw silk with a fine zari border.',
  double price = 24500,
  double cost = 12000,
  int quantity = 4,
  CatalogItemStatus status = CatalogItemStatus.available,
  String color = 'Wine',
  List<String> sizes = const ['36', '38'],
  String category = 'Sarees',
  Set<String> tags = const {'bridal'},
}) {
  return CatalogProduct(
    id: id,
    organizationId: 'org-1',
    name: name ?? 'Handloom Silk Saree $id',
    description: description,
    price: price,
    cost: cost,
    quantity: quantity,
    status: status,
    isAvailable: status == CatalogItemStatus.available,
    createdAtUtc: DateTime.utc(2026, 9, 1),
    color: color,
    sizes: sizes,
    category: category,
    fabric: 'Raw silk',
    tags: tags,
  );
}

/// Two pieces that differ on every facet the grid shows.
List<CatalogProduct> _pool() => [
  _piece('p1', name: 'Handloom Silk Saree'),
  _piece(
    'p2',
    name: 'Chiffon Lehenga',
    category: 'Lehengas',
    tags: const {'festive'},
    color: 'Emerald',
    price: 78500,
    quantity: 1,
  ),
];

/// A phone-shaped viewport, so the two-column grid lays out the way it does on
/// the devices this app ships to.
void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

/// A boutique provider holding a name without touching the API.
///
/// The screen only reads [BoutiqueProvider.name]; parsing the `/orgs/my`
/// payload is covered by `boutique_provider_test.dart`.
class _FakeBoutique extends BoutiqueProvider {
  _FakeBoutique(this._name);

  final String? _name;

  @override
  String? get name => _name;
}

/// Reduced motion is on, matching the rest of the suite: the backdrop carries
/// ambient animation that would never settle otherwise, and a rotating page is
/// not what these tests are about.
Widget _wrap({
  String? boutiqueName,
  BoutiqueProvider? boutique,
  List<CatalogTag>? tags,
  List<CatalogProduct>? pool,
}) {
  final app = MaterialApp(
    theme: AppTheme.light,
    home: Builder(
      builder: (context) => MediaQuery(
        data: MediaQuery.of(context).copyWith(disableAnimations: true),
        child: Scaffold(
          body: CatalogScreen(
            boutiqueName: boutiqueName,
            tags: tags ?? _tags,
            repository: _StubProductRepository(pool ?? _pool()),
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

/// Pumps the catalog and lets its first page land.
Future<void> _pumpCatalog(
  WidgetTester tester, {
  String? boutiqueName = 'Ceylon Atelier',
  BoutiqueProvider? boutique,
  List<CatalogTag>? tags,
  List<CatalogProduct>? pool,
}) async {
  _usePhoneSurface(tester);
  await tester.pumpWidget(
    _wrap(
      boutiqueName: boutiqueName,
      boutique: boutique,
      tags: tags,
      pool: pool,
    ),
  );
  await tester.pump();
  await tester.pump();
}

/// The title's rendered plain text, e.g. `Ceylon Atelier - Catalog`.
String _titleText(WidgetTester tester) {
  return tester.widget<Text>(find.byKey(const Key('catalog_title'))).data!;
}

void main() {
  group('CatalogScreen title', () {
    testWidgets('names the boutique and the section in the brand serif', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      expect(_titleText(tester), 'Ceylon Atelier - Catalog');

      final title = tester.widget<Text>(find.byKey(const Key('catalog_title')));
      expect(title.style?.fontFamily, startsWith('PlayfairDisplay'));
      expect(title.style?.fontSize, AppTheme.textTheme.displayMedium?.fontSize);
    });

    testWidgets('reads the boutique name from the provider', (tester) async {
      await _pumpCatalog(tester, boutique: _FakeBoutique('Ceylon Atelier'));

      expect(_titleText(tester), 'Ceylon Atelier - Catalog');
    });

    testWidgets('falls back to the brand name on a cold start', (tester) async {
      await _pumpCatalog(tester, boutiqueName: null);

      expect(_titleText(tester), 'Aveline - Catalog');
    });

    testWidgets(
      'keeps a long name to one line so it fades instead of clipping',
      (tester) async {
        await _pumpCatalog(
          tester,
          boutiqueName: 'The Colombo Heritage Atelier and Silk House',
        );

        final title = tester.widget<Text>(
          find.byKey(const Key('catalog_title')),
        );
        expect(title.maxLines, 1);
        expect(title.softWrap, isFalse);

        // The trailing fade is what stops the name being cut mid-letter. It is
        // the only `ShaderMask` above the title; the backdrop's masks sit
        // beside it, not over it.
        expect(
          find.ancestor(
            of: find.byKey(const Key('catalog_title')),
            matching: find.byType(ShaderMask),
          ),
          findsOneWidget,
        );
      },
    );
  });

  group('CatalogScreen search', () {
    testWidgets('owns a catalog-scoped field rather than the global overlay', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      expect(find.byKey(const Key('catalog_search_field')), findsOneWidget);
      expect(find.text('Search this catalog...'), findsOneWidget);
      // The header's global search is a separate affordance and must not be
      // what this screen opens.
      expect(find.byType(SearchOverlay), findsNothing);
    });

    testWidgets('narrows the grid to the pieces the query matches', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      expect(find.byKey(const ValueKey('catalog_product_p1')), findsOneWidget);
      expect(find.byKey(const ValueKey('catalog_product_p2')), findsOneWidget);

      await tester.enterText(
        find.byKey(const Key('catalog_search_field')),
        'silk',
      );
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const ValueKey('catalog_product_p1')), findsOneWidget);
      expect(find.byKey(const ValueKey('catalog_product_p2')), findsNothing);
    });

    testWidgets('offers a way back when the query matches nothing', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      await tester.enterText(
        find.byKey(const Key('catalog_search_field')),
        'nothing at all',
      );
      await tester.pump();
      await tester.pump();

      expect(find.text('No pieces match "nothing at all"'), findsOneWidget);
      expect(find.byKey(const ValueKey('catalog_product_p1')), findsNothing);

      await tester.tap(find.byKey(const Key('catalog_clear_all')));
      await tester.pump();
      await tester.pump();

      final field = tester.widget<TextField>(
        find.byKey(const Key('catalog_search_field')),
      );
      expect(field.controller?.text, isEmpty);
      expect(find.byKey(const ValueKey('catalog_product_p1')), findsOneWidget);
    });

    testWidgets('clears the query back to the whole catalog', (tester) async {
      await _pumpCatalog(tester);

      await tester.enterText(
        find.byKey(const Key('catalog_search_field')),
        'silk',
      );
      await tester.pump();
      await tester.pump();

      await tester.tap(find.byKey(const Key('catalog_search_clear')));
      await tester.pump();
      await tester.pump();

      final field = tester.widget<TextField>(
        find.byKey(const Key('catalog_search_field')),
      );
      expect(field.controller?.text, isEmpty);
      expect(find.byKey(const ValueKey('catalog_product_p2')), findsOneWidget);
      expect(find.byKey(const Key('catalog_search_clear')), findsNothing);
    });

    testWidgets('stays scrollable so the shell pull-to-refresh still arms', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      final scroll = tester.widget<CustomScrollView>(
        find.byKey(const Key('catalog_scroll')),
      );
      expect(scroll.physics, isA<AlwaysScrollableScrollPhysics>());
    });
  });

  group('CatalogScreen tags', () {
    testWidgets('shows the shop tags as a horizontally scrolling row', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      expect(find.byType(CatalogTagRow), findsOneWidget);
      expect(find.text('Bridal'), findsOneWidget);
      expect(find.text('Festive'), findsOneWidget);

      final row = tester.widget<ListView>(
        find.descendant(
          of: find.byType(CatalogTagRow),
          matching: find.byType(ListView),
        ),
      );
      expect(row.scrollDirection, Axis.horizontal);
    });

    testWidgets('narrows the grid to the chosen shop tag', (tester) async {
      await _pumpCatalog(tester);

      FilterPill pill(String id) =>
          tester.widget<FilterPill>(find.byKey(ValueKey('catalog_tag_$id')));

      expect(pill('festive').selected, isFalse);

      await tester.tap(find.byKey(const ValueKey('catalog_tag_festive')));
      await tester.pump();
      await tester.pump();

      expect(pill('festive').selected, isTrue);
      expect(find.byKey(const ValueKey('catalog_product_p2')), findsOneWidget);
      expect(find.byKey(const ValueKey('catalog_product_p1')), findsNothing);

      await tester.tap(find.byKey(const ValueKey('catalog_tag_festive')));
      await tester.pump();
      await tester.pump();

      expect(pill('festive').selected, isFalse);
      expect(find.byKey(const ValueKey('catalog_product_p1')), findsOneWidget);
    });
  });

  group('CatalogScreen products', () {
    testWidgets('lays the pieces out in two columns', (tester) async {
      await _pumpCatalog(tester);

      final grid = tester.widget<SliverGrid>(find.byType(SliverGrid));
      final delegate =
          grid.gridDelegate as SliverGridDelegateWithFixedCrossAxisCount;

      expect(delegate.crossAxisCount, 2);
      expect(find.byType(CatalogProductCard), findsNWidgets(2));
    });

    testWidgets('puts price, stock, copy, sizes and colour on the card', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      final card = find.byKey(const ValueKey('catalog_product_p1'));
      expect(card, findsOneWidget);

      expect(find.text('Rs 24,500'), findsOneWidget);
      expect(find.text('4 in stock'), findsOneWidget);
      // Both pieces carry the same default copy and sizes, so the shared lines
      // are scoped to the card under test.
      expect(
        find.descendant(
          of: card,
          matching: find.text('Handwoven raw silk with a fine zari border.'),
        ),
        findsOneWidget,
      );
      expect(
        find.descendant(of: card, matching: find.text('Sizes 36 · 38')),
        findsOneWidget,
      );

      final dot = tester.widget<CatalogColorDot>(
        find.byKey(const ValueKey('catalog_colour_p1')),
      );
      expect(dot.colorName, 'Wine');
      expect(catalogColorValue(dot.colorName), const Color(0xFF8B2E42));

      // The card is the whole affordance: nothing on it is a button.
      expect(
        find.descendant(of: card, matching: find.byType(FilledButton)),
        findsNothing,
      );
      expect(
        find.descendant(of: card, matching: find.byType(TextButton)),
        findsNothing,
      );
    });

    testWidgets('shows a low-stock piece as such', (tester) async {
      await _pumpCatalog(tester);

      expect(find.text('Only 1 left'), findsOneWidget);
    });

    testWidgets('says when the whole catalog has been listed', (tester) async {
      await _pumpCatalog(tester, pool: _pool());

      // Two pieces fit on one page, so the end marker is already on screen.
      expect(find.byKey(const Key('catalog_end_of_list')), findsOneWidget);
    });

    testWidgets('asks for the next page as the grid approaches its end', (
      tester,
    ) async {
      final pool = List.generate(
        10,
        (i) => _piece('p${i + 1}', name: 'Piece ${i + 1}'),
      );
      await _pumpCatalog(tester, pool: pool);

      // Only the first page (8) is held; the ninth and tenth arrive when the
      // grid nears its end.
      expect(find.byKey(const ValueKey('catalog_product_p9')), findsNothing);

      await tester.scrollUntilVisible(
        find.byKey(const ValueKey('catalog_product_p10')),
        600,
        scrollable: find.byType(Scrollable).first,
      );

      expect(find.byKey(const ValueKey('catalog_product_p10')), findsOneWidget);
    });

    testWidgets('opens the piece when its card is tapped', (tester) async {
      _usePhoneSurface(tester);
      // The pushed route has to inherit reduced motion too, or the backdrop's
      // ambient animation never settles.
      tester.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(
        tester.platformDispatcher.clearAccessibilityFeaturesTestValue,
      );

      final router = GoRouter(
        initialLocation: AppRoutes.catalog,
        routes: [
          GoRoute(
            path: AppRoutes.catalog,
            builder: (context, state) => Scaffold(
              body: CatalogScreen(
                boutiqueName: 'Ceylon Atelier',
                tags: _tags,
                repository: _StubProductRepository(_pool()),
              ),
            ),
          ),
          GoRoute(
            path: AppRoutes.catalogProductPattern,
            builder: (context, state) => CatalogProductScreen(
              productId: state.pathParameters['productId'] ?? '',
              repository: _StubProductRepository(_pool()),
            ),
          ),
        ],
      );

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );
      await tester.pump();
      await tester.pump();

      await tester.tap(find.byKey(const ValueKey('catalog_product_p1')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pump();

      // The piece opened, carrying the one that was tapped.
      expect(find.byType(CatalogProductScreen), findsOneWidget);
      expect(
        find.descendant(
          of: find.byType(CatalogProductScreen),
          matching: find.text('Handloom Silk Saree'),
        ),
        findsOneWidget,
      );
    });
  });

  group('CatalogScreen filters', () {
    testWidgets('offers the filter entry point beside the field', (
      tester,
    ) async {
      await _pumpCatalog(tester);

      expect(find.byKey(const Key('catalog_filter_button')), findsOneWidget);
    });

    testWidgets('opens the filter screen and narrows the grid by its result', (
      tester,
    ) async {
      _usePhoneSurface(tester);
      tester.platformDispatcher.accessibilityFeaturesTestValue =
          const FakeAccessibilityFeatures(disableAnimations: true);
      addTearDown(
        tester.platformDispatcher.clearAccessibilityFeaturesTestValue,
      );

      final router = GoRouter(
        initialLocation: AppRoutes.catalog,
        routes: [
          GoRoute(
            path: AppRoutes.catalog,
            builder: (context, state) => Scaffold(
              body: CatalogScreen(
                boutiqueName: 'Ceylon Atelier',
                tags: _tags,
                repository: _StubProductRepository(_pool()),
              ),
            ),
          ),
          GoRoute(
            path: AppRoutes.catalogFilters,
            builder: (context, state) => CatalogFilterScreen(
              initial: state.extra is CatalogFilters
                  ? state.extra as CatalogFilters
                  : null,
            ),
          ),
        ],
      );

      await tester.pumpWidget(
        MaterialApp.router(theme: AppTheme.light, routerConfig: router),
      );
      await tester.pump();
      await tester.pump();

      await tester.tap(find.byKey(const Key('catalog_filter_button')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.text('Filter options'), findsOneWidget);

      await tester.tap(find.text('Lehengas'));
      await tester.pump();

      await tester.tap(find.byKey(const Key('catalog_filter_apply')));
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));
      await tester.pump();

      // Back on the catalog, one option applied and the grid narrowed to it.
      expect(find.byKey(const Key('catalog_filter_button')), findsOneWidget);
      expect(find.text('1'), findsOneWidget);
      expect(find.byKey(const ValueKey('catalog_product_p2')), findsOneWidget);
      expect(find.byKey(const ValueKey('catalog_product_p1')), findsNothing);
    });
  });
}
