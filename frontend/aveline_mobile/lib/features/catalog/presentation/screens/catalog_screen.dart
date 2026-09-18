import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/brand_section_title.dart';
import '../../../../shared/widgets/section_search_field.dart';
import '../../data/catalog_product_repository.dart';
import '../../data/demo_catalog_product_repository.dart';
import '../../data/demo_catalog_tags.dart';
import '../../domain/catalog_filters.dart';
import '../../domain/catalog_product.dart';
import '../../domain/catalog_tag.dart';
import '../catalog_products_controller.dart';
import '../widgets/catalog_product_card.dart';
import '../widgets/catalog_tag_row.dart';

/// Catalog dock tab: the boutique's pieces.
///
/// The title names the shop the catalog belongs to, and the field below it
/// searches this catalog only — the app-wide search in the header is a separate
/// affordance and is deliberately not what this screen opens. Under the field
/// sit the shop's own tag row and the filter entry point, then the grid, which
/// asks for the next page as it approaches its end.
class CatalogScreen extends StatefulWidget {
  const CatalogScreen({
    super.key,
    this.boutiqueName,
    this.tags,
    this.repository,
  });

  /// Overrides the boutique name, for tests and previews. When `null`, the name
  /// is read from [BoutiqueProvider], falling back to the brand.
  final String? boutiqueName;

  /// Overrides the shop tag row, for tests and previews. Defaults to the
  /// placeholder shop tags.
  final List<CatalogTag>? tags;

  /// Overrides the paged source, for tests and previews. Defaults to the demo
  /// repository, which pages a boutique's worth of pieces in memory.
  final CatalogProductRepository? repository;

  @override
  State<CatalogScreen> createState() => _CatalogScreenState();
}

class _CatalogScreenState extends State<CatalogScreen> {
  /// What the title reads before a boutique name is known.
  ///
  /// A title that vanished or read "Catalog" alone while the name loaded made
  /// the screen look unfinished on every cold start.
  static const String _fallbackName = 'Aveline';

  /// How close to the end of the grid the next page is asked for.
  ///
  /// A screen's worth of pieces, so the page is usually in hand before the
  /// associate reaches the last row.
  static const double _loadMoreThreshold = 400;

  final TextEditingController _searchController = TextEditingController();
  final ScrollController _scrollController = ScrollController();
  late final CatalogProductsController _products;

  /// The query the field currently holds, trimmed. Search is scoped to this
  /// screen: it narrows this catalog, not the whole app.
  String _query = '';

  /// The options chosen on the filter screen.
  CatalogFilters _filters = const CatalogFilters.none();

  /// The shop tags currently narrowing the catalog.
  final Set<String> _selectedTagIds = {};

  @override
  void initState() {
    super.initState();
    _products = CatalogProductsController(
      widget.repository ?? DemoCatalogProductRepository(),
    );
    // Started before the listener is attached: `loadFirstPage` notifies
    // synchronously, and that must not reach `setState` from `initState`.
    // Nothing is lost, because the first build already reads the loading state.
    _products.loadFirstPage(query: _productQuery);
    _products.addListener(_onProductsChanged);
    _scrollController.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    _products
      ..removeListener(_onProductsChanged)
      ..dispose();
    _searchController.dispose();
    super.dispose();
  }

  /// The narrowing in force, as the paged source reads it.
  CatalogProductQuery get _productQuery => CatalogProductQuery(
    search: _query,
    tagIds: {..._selectedTagIds},
    filters: _filters,
  );

  void _onProductsChanged() => setState(() {});

  void _onScroll() {
    if (!_scrollController.hasClients) {
      return;
    }
    final position = _scrollController.position;
    if (position.pixels >= position.maxScrollExtent - _loadMoreThreshold) {
      _products.loadMore();
    }
  }

  void _reloadProducts() {
    _products.loadFirstPage(query: _productQuery);
  }

  void _onQueryChanged(String value) {
    final next = value.trim();
    if (next == _query) {
      return;
    }
    setState(() => _query = next);
    _reloadProducts();
  }

  void _clearQuery() {
    _searchController.clear();
    _onQueryChanged('');
  }

  void _toggleTag(String id) {
    setState(() {
      if (!_selectedTagIds.remove(id)) {
        _selectedTagIds.add(id);
      }
    });
    _reloadProducts();
  }

  /// Opens the filter screen and adopts whatever it returns.
  ///
  /// The shell always mounts a GoRouter; a bare Catalog in a widget test simply
  /// does not open the screen.
  Future<void> _openFilters() async {
    final router = GoRouter.maybeOf(context);
    if (router == null) {
      return;
    }

    final result = await router.push<CatalogFilters>(
      AppRoutes.catalogFilters,
      extra: _filters,
    );
    if (!mounted || result == null) {
      return;
    }
    setState(() => _filters = result);
    _reloadProducts();
  }

  void _clearAll() {
    _searchController.clear();
    setState(() {
      _query = '';
      _filters = const CatalogFilters.none();
      _selectedTagIds.clear();
    });
    _reloadProducts();
  }

  void _openProduct(CatalogProduct product) {
    // The location carries the id; the detail screen resolves the piece from
    // it, so nothing has to survive the router re-parsing the route.
    GoRouter.maybeOf(context)?.push(AppRoutes.catalogProduct(product.id));
  }

  /// The boutique name, or `null` when no provider is above the screen.
  ///
  /// The screen is mounted directly by widget tests that supply no providers,
  /// so a missing one has to degrade rather than throw.
  String? _boutiqueNameOrNull(BuildContext context) {
    try {
      return context.watch<BoutiqueProvider>().name;
    } catch (_) {
      return null;
    }
  }

  @override
  Widget build(BuildContext context) {
    final boutiqueName =
        widget.boutiqueName ?? _boutiqueNameOrNull(context) ?? _fallbackName;
    final tags = widget.tags ?? demoCatalogTags();

    // Tags narrow the catalog too, so the standing panel counts them alongside
    // the filter screen's options. The button badge stays on the screen's own
    // count: the chosen tags are already visible as chosen pills.
    final activeNarrowingCount = _filters.activeCount + _selectedTagIds.length;

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        CustomScrollView(
          key: const Key('catalog_scroll'),
          controller: _scrollController,
          // Always scrollable, so the shell's pull-to-refresh still arms on a
          // screen whose content is shorter than the viewport.
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            // The sections the brand atmosphere is held behind.
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(20, 20, 20, 0),
              sliver: SliverToBoxAdapter(
                child: BrandSectionTitle(
                  boutiqueName: boutiqueName,
                  section: 'Catalog',
                  titleKey: const Key('catalog_title'),
                ),
              ),
            ),
            SliverPadding(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 0),
              sliver: SliverToBoxAdapter(
                child: Row(
                  children: [
                    Expanded(
                      child: SectionSearchField(
                        controller: _searchController,
                        hintText: 'Search this catalog...',
                        hasQuery: _query.isNotEmpty,
                        onChanged: _onQueryChanged,
                        onClear: _clearQuery,
                        fieldKey: const Key('catalog_search_field'),
                        clearKey: const Key('catalog_search_clear'),
                      ),
                    ),
                    const SizedBox(width: 10),
                    _CatalogFilterButton(
                      activeCount: _filters.activeCount,
                      onTap: _openFilters,
                    ),
                  ],
                ),
              ),
            ),
            const SliverToBoxAdapter(child: SizedBox(height: 16)),
            // The shop's own tags. Full-bleed: the row scrolls to both edges
            // and owns its own trailing padding.
            SliverToBoxAdapter(
              child: CatalogTagRow(
                tags: tags,
                selectedIds: _selectedTagIds,
                onToggled: _toggleTag,
              ),
            ),
            const SliverToBoxAdapter(child: SizedBox(height: 20)),
            ..._productSlivers(activeNarrowingCount),
            // The animated Blossom floats over the bottom of the shell, so the
            // grid keeps enough room to scroll clear of it.
            const SliverToBoxAdapter(child: SizedBox(height: 96)),
          ],
        ),
      ],
    );
  }

  /// The grid, and whatever stands in its place while it has nothing to show.
  List<Widget> _productSlivers(int activeNarrowingCount) {
    if (_products.isLoading && !_products.hasLoadedOnce) {
      return const [SliverToBoxAdapter(child: _CatalogProductsLoading())];
    }

    if (_products.errorMessage != null && _products.products.isEmpty) {
      return [
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverToBoxAdapter(
            child: _CatalogProductsError(
              message: _products.errorMessage!,
              onRetry: _products.retry,
            ),
          ),
        ),
      ];
    }

    if (_products.isEmpty) {
      return [
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverToBoxAdapter(
            child: _CatalogEmptyState(
              query: _query,
              activeNarrowingCount: activeNarrowingCount,
              onClearAll: _clearAll,
            ),
          ),
        ),
      ];
    }

    final products = _products.products;
    return [
      SliverPadding(
        padding: const EdgeInsets.symmetric(horizontal: 20),
        sliver: SliverGrid(
          gridDelegate: const SliverGridDelegateWithFixedCrossAxisCount(
            crossAxisCount: 2,
            mainAxisSpacing: 14,
            crossAxisSpacing: 14,
            childAspectRatio: 0.56,
          ),
          delegate: SliverChildBuilderDelegate((context, index) {
            final product = products[index];
            return CatalogProductCard(
              key: ValueKey('catalog_product_${product.id}'),
              product: product,
              onTap: () => _openProduct(product),
            );
          }, childCount: products.length),
        ),
      ),
      SliverToBoxAdapter(
        child: _CatalogListFooter(
          isLoading: _products.isLoading,
          hasMore: _products.hasMore,
          errorMessage: _products.errorMessage,
          onRetry: _products.retry,
        ),
      ),
    ];
  }
}

/// The filter entry point beside the field, badged with the applied count.
class _CatalogFilterButton extends StatelessWidget {
  const _CatalogFilterButton({required this.activeCount, required this.onTap});

  final int activeCount;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isActive = activeCount > 0;

    final shape = StadiumBorder(
      side: BorderSide(
        color: isActive
            ? scheme.primary.withValues(alpha: 0.45)
            : scheme.outlineVariant,
      ),
    );

    return Semantics(
      button: true,
      label: isActive ? 'Filter, $activeCount applied' : 'Filter',
      child: SizedBox(
        width: 50,
        height: 50,
        child: Material(
          color: isActive
              ? scheme.primary.withValues(alpha: 0.10)
              : scheme.surfaceContainerLow,
          shape: shape,
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            key: const Key('catalog_filter_button'),
            onTap: onTap,
            customBorder: shape,
            child: Stack(
              alignment: Alignment.center,
              children: [
                Icon(
                  Icons.tune_rounded,
                  size: 22,
                  color: isActive ? scheme.primary : scheme.onSurfaceVariant,
                ),
                if (isActive)
                  Positioned(
                    top: 6,
                    right: 6,
                    child: Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 5,
                        vertical: 1,
                      ),
                      decoration: BoxDecoration(
                        color: scheme.primary,
                        borderRadius: BorderRadius.circular(999),
                      ),
                      child: Text(
                        '$activeCount',
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: scheme.onPrimary,
                          fontSize: 10,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The first page's stand-in while it is on its way.
class _CatalogProductsLoading extends StatelessWidget {
  const _CatalogProductsLoading();

  @override
  Widget build(BuildContext context) {
    return const Padding(
      padding: EdgeInsets.symmetric(vertical: 48),
      child: Center(
        child: SizedBox(
          width: 24,
          height: 24,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      ),
    );
  }
}

/// The quiet panel shown when the first page could not be fetched.
class _CatalogProductsError extends StatelessWidget {
  const _CatalogProductsError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('catalog_products_error'),
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
            blurRadius: 20,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.cloud_off_rounded, size: 28, color: scheme.primary),
          const SizedBox(height: 14),
          Text(
            'The catalog could not load',
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            message,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('catalog_products_retry'),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: const Text('Try again'),
          ),
        ],
      ),
    );
  }
}

/// What sits under the last row: the next page on its way, or the end.
class _CatalogListFooter extends StatelessWidget {
  const _CatalogListFooter({
    required this.isLoading,
    required this.hasMore,
    required this.errorMessage,
    required this.onRetry,
  });

  final bool isLoading;
  final bool hasMore;
  final String? errorMessage;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (isLoading) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 28),
        child: Center(
          child: SizedBox(
            width: 22,
            height: 22,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
      );
    }

    if (errorMessage != null) {
      return Padding(
        padding: const EdgeInsets.symmetric(vertical: 20),
        child: Center(
          child: TextButton.icon(
            key: const Key('catalog_products_retry'),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: const Text('Could not load more. Try again'),
          ),
        ),
      );
    }

    if (!hasMore) {
      return Padding(
        padding: const EdgeInsets.only(top: 28),
        child: Center(
          child: Text(
            'That is the whole catalog.',
            key: const Key('catalog_end_of_list'),
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ),
      );
    }

    return const SizedBox(height: 28);
  }
}

/// The quiet panel shown when the narrowing in force matches nothing.
class _CatalogEmptyState extends StatelessWidget {
  const _CatalogEmptyState({
    required this.query,
    required this.activeNarrowingCount,
    required this.onClearAll,
  });

  final String query;
  final int activeNarrowingCount;
  final VoidCallback onClearAll;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final hasQuery = query.isNotEmpty;
    final hasNarrowing = activeNarrowingCount > 0;

    final String heading;
    final String body;
    if (hasQuery) {
      heading = 'No pieces match "$query"';
      body =
          'Try another name, SKU, colour, or fabric, or clear the search to '
          'see the whole catalog.';
    } else if (hasNarrowing) {
      heading = 'Nothing matches these filters yet';
      body = 'Clear a filter or try another combination.';
    } else {
      heading = 'Visual intelligence & sourcing';
      body =
          'Find a piece by name, SKU, colour, or fabric. The search stays '
          'inside this boutique\'s catalog.';
    }

    return Container(
      key: const Key('catalog_empty_state'),
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
            blurRadius: 20,
            offset: const Offset(0, 4),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            hasQuery
                ? Icons.search_off_rounded
                : hasNarrowing
                ? Icons.filter_alt_off_rounded
                : Icons.checkroom_outlined,
            size: 28,
            color: scheme.primary,
          ),
          const SizedBox(height: 14),
          Text(heading, style: theme.textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            body,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          if (hasQuery || hasNarrowing) ...[
            const SizedBox(height: 12),
            TextButton.icon(
              key: const Key('catalog_clear_all'),
              onPressed: onClearAll,
              icon: const Icon(Icons.close_rounded, size: 16),
              label: const Text('Clear all'),
            ),
          ],
        ],
      ),
    );
  }
}
