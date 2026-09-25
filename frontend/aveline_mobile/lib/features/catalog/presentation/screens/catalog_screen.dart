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
import '../../domain/outfit_composition.dart';
import '../../domain/sourcing_request.dart';
import '../catalog_products_controller.dart';
import '../sourcing_controller.dart';
import '../widgets/catalog_kpi_cards.dart';
import '../widgets/catalog_product_card.dart';
import '../widgets/catalog_tag_row.dart';
import '../widgets/lookbooks_view.dart';
import '../widgets/sourcing_pipeline_view.dart';
import '../widgets/suppliers_view.dart';
import 'add_edit_product_screen.dart';
import 'compose_outfit_screen.dart';
import 'create_sourcing_ticket_screen.dart';

/// The active tab of the catalog dock screen.
enum CatalogDockTab { pieces, lookbooks, sourcing, ateliers }

/// Catalog dock tab: the boutique's pieces, lookbooks, and bespoke sourcing pipeline.
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
  static const String _fallbackName = 'Aveline';

  /// How close to the end of the grid the next page is asked for.
  static const double _loadMoreThreshold = 400;

  final TextEditingController _searchController = TextEditingController();
  final ScrollController _scrollController = ScrollController();
  late final CatalogProductsController _products;
  late final SourcingController _sourcingController;

  /// The query the field currently holds, trimmed. Search is scoped to this
  /// screen: it narrows this catalog, not the whole app.
  String _query = '';

  /// The options chosen on the filter screen.
  CatalogFilters _filters = const CatalogFilters.none();

  /// The shop tags currently narrowing the catalog.
  final Set<String> _selectedTagIds = {};

  List<CatalogTag>? _dynamicTags;
  CatalogDockTab _selectedTab = CatalogDockTab.pieces;
  String? _seededOrganizationId;

  @override
  void initState() {
    super.initState();
    final repo = widget.repository ?? DemoCatalogProductRepository();
    _products = CatalogProductsController(repo);
    _sourcingController = SourcingController(repo);

    _loadTags();
    _products.loadFirstPage(query: _productQuery);
    _products.addListener(_onProductsChanged);
    _sourcingController.loadData();
    _scrollController.addListener(_onScroll);
  }

  Future<void> _loadTags() async {
    if (widget.tags != null) return;
    try {
      final repo = widget.repository ?? DemoCatalogProductRepository();
      final tags = await repo.fetchTags();
      if (mounted) {
        setState(() {
          _dynamicTags = tags;
        });
      }
    } catch (_) {
      // Non-fatal, fallback to defaults
    }
  }

  Future<void> _handleRefresh() async {
    await Future.wait([
      _products.loadFirstPage(query: _productQuery),
      _sourcingController.loadData(),
      _loadTags(),
    ]);
  }

  @override
  void dispose() {
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    _products
      ..removeListener(_onProductsChanged)
      ..dispose();
    _sourcingController.dispose();
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
  Future<void> _openFilters() async {
    final router = GoRouter.maybeOf(context);
    if (router == null) {
      return;
    }

    final uri = Uri(
      path: AppRoutes.catalogFilters,
      queryParameters: _filters.isEmpty ? null : _filters.toQueryParameters(),
    );

    final result = await router.push<CatalogFilters>(
      uri.toString(),
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
    GoRouter.maybeOf(context)?.push(AppRoutes.catalogProduct(product.id));
  }

  Future<void> _openAddPiece() async {
    final created = await Navigator.of(context).push<CatalogProduct>(
      MaterialPageRoute(
        builder: (_) => AddEditProductScreen(
          repository: widget.repository ?? DemoCatalogProductRepository(),
        ),
      ),
    );

    if (created != null && mounted) {
      _products.loadFirstPage(query: _productQuery);
    }
  }

  Future<void> _openComposeLook() async {
    final created = await Navigator.of(context).push<OutfitComposition>(
      MaterialPageRoute(
        builder: (_) => ComposeOutfitScreen(
          repository: widget.repository ?? DemoCatalogProductRepository(),
        ),
      ),
    );

    if (created != null && mounted) {
      setState(() {});
    }
  }

  Future<void> _openCreateSourcingTicket() async {
    final created = await Navigator.of(context).push<SourcingRequest>(
      MaterialPageRoute(
        builder: (_) => CreateSourcingTicketScreen(
          repository: widget.repository ?? DemoCatalogProductRepository(),
          controller: _sourcingController,
        ),
      ),
    );

    if (created != null && mounted) {
      setState(() {});
    }
  }

  @override
  Widget build(BuildContext context) {
    BoutiqueProvider? boutique;
    try {
      boutique = context.watch<BoutiqueProvider>();
    } catch (_) {
      boutique = null;
    }
    final organizationId = boutique?.organizationId;
    if (organizationId != null && organizationId != _seededOrganizationId) {
      _seededOrganizationId = organizationId;
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) {
          _products.loadFirstPage(query: _productQuery);
          _sourcingController.loadData();
          _loadTags();
        }
      });
    }

    final boutiqueName =
        widget.boutiqueName ?? boutique?.name ?? _fallbackName;
    final tags = widget.tags ?? _dynamicTags ?? demoCatalogTags();
    final activeNarrowingCount = _filters.activeCount + _selectedTagIds.length;

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        SafeArea(
          bottom: false,
          child: Column(
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 16, 20, 0),
                child: BrandSectionTitle(
                  boutiqueName: boutiqueName,
                  section: 'Catalog',
                  titleKey: const Key('catalog_title'),
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
                child: _CatalogSegmentedTab(
                  selectedTab: _selectedTab,
                  onTabChanged: (tab) => setState(() => _selectedTab = tab),
                ),
              ),
              Expanded(
                child: switch (_selectedTab) {
                  CatalogDockTab.pieces => RefreshIndicator(
                      onRefresh: _handleRefresh,
                      child: CustomScrollView(
                        key: const Key('catalog_scroll'),
                        controller: _scrollController,
                        physics: const AlwaysScrollableScrollPhysics(),
                        slivers: [
                        SliverToBoxAdapter(
                          child: Padding(
                            padding: const EdgeInsets.only(top: 8, bottom: 4),
                            child: CatalogKpiCards(
                              products: _products.products,
                              isLoading: _products.isLoading,
                            ),
                          ),
                        ),
                        SliverPadding(
                          padding: const EdgeInsets.fromLTRB(20, 10, 20, 0),
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
                        const SliverToBoxAdapter(child: SizedBox(height: 14)),
                        SliverToBoxAdapter(
                          child: CatalogTagRow(
                            tags: tags,
                            selectedIds: _selectedTagIds,
                            onToggled: _toggleTag,
                          ),
                        ),
                        const SliverToBoxAdapter(child: SizedBox(height: 18)),
                        ..._productSlivers(activeNarrowingCount),
                        const SliverToBoxAdapter(child: SizedBox(height: 140)),
                      ],
                    ),
                  ),
                  CatalogDockTab.lookbooks => LookbooksView(
                      repository: widget.repository ?? DemoCatalogProductRepository(),
                      searchQuery: _query,
                    ),
                  CatalogDockTab.sourcing => SourcingPipelineView(
                      controller: _sourcingController,
                      onCreateTicket: _openCreateSourcingTicket,
                    ),
                  CatalogDockTab.ateliers => SuppliersView(
                      repository: widget.repository ?? DemoCatalogProductRepository(),
                    ),
                },
              ),
            ],
          ),
        ),
        Positioned(
          bottom: 96,
          right: 20,
          child: switch (_selectedTab) {
            CatalogDockTab.pieces => FloatingActionButton.extended(
                key: const Key('catalog_add_piece_fab'),
                onPressed: _openAddPiece,
                icon: const Icon(Icons.add_a_photo_outlined, size: 20),
                label: const Text('Add Piece'),
              ),
            CatalogDockTab.lookbooks => FloatingActionButton.extended(
                key: const Key('catalog_compose_look_fab'),
                onPressed: _openComposeLook,
                icon: const Icon(Icons.auto_awesome, size: 20),
                label: const Text('Compose Look'),
              ),
            CatalogDockTab.sourcing => FloatingActionButton.extended(
                key: const Key('catalog_new_ticket_fab'),
                onPressed: _openCreateSourcingTicket,
                icon: const Icon(Icons.checkroom, size: 20),
                label: const Text('New Sourcing Ticket'),
              ),
            CatalogDockTab.ateliers => const SizedBox.shrink(),
          },
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

class _CatalogSegmentedTab extends StatelessWidget {
  const _CatalogSegmentedTab({
    required this.selectedTab,
    required this.onTabChanged,
  });

  final CatalogDockTab selectedTab;
  final ValueChanged<CatalogDockTab> onTabChanged;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      height: 40,
      padding: const EdgeInsets.all(3),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHighest.withValues(alpha: 0.5),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
      ),
      child: Row(
        children: [
          Expanded(
            child: _SegmentButton(
              key: const Key('catalog_tab_pieces'),
              label: 'Pieces',
              icon: Icons.checkroom_outlined,
              isSelected: selectedTab == CatalogDockTab.pieces,
              onTap: () => onTabChanged(CatalogDockTab.pieces),
              scheme: scheme,
            ),
          ),
          Expanded(
            child: _SegmentButton(
              key: const Key('catalog_tab_lookbooks'),
              label: 'Lookbooks',
              icon: Icons.style_outlined,
              isSelected: selectedTab == CatalogDockTab.lookbooks,
              onTap: () => onTabChanged(CatalogDockTab.lookbooks),
              scheme: scheme,
            ),
          ),
          Expanded(
            child: _SegmentButton(
              key: const Key('catalog_tab_sourcing'),
              label: 'Sourcing',
              icon: Icons.work_outline,
              isSelected: selectedTab == CatalogDockTab.sourcing,
              onTap: () => onTabChanged(CatalogDockTab.sourcing),
              scheme: scheme,
            ),
          ),
          Expanded(
            child: _SegmentButton(
              key: const Key('catalog_tab_ateliers'),
              label: 'Ateliers',
              icon: Icons.business_outlined,
              isSelected: selectedTab == CatalogDockTab.ateliers,
              onTap: () => onTabChanged(CatalogDockTab.ateliers),
              scheme: scheme,
            ),
          ),
        ],
      ),
    );
  }
}

class _SegmentButton extends StatelessWidget {
  const _SegmentButton({
    super.key,
    required this.label,
    required this.icon,
    required this.isSelected,
    required this.onTap,
    required this.scheme,
  });

  final String label;
  final IconData icon;
  final bool isSelected;
  final VoidCallback onTap;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(17),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          decoration: BoxDecoration(
            color: isSelected ? scheme.surface : Colors.transparent,
            borderRadius: BorderRadius.circular(17),
            boxShadow: isSelected
                ? [
                    BoxShadow(
                      color: Colors.black.withValues(alpha: 0.06),
                      blurRadius: 4,
                      offset: const Offset(0, 2),
                    ),
                  ]
                : null,
          ),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 4),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(
                  icon,
                  size: 14,
                  color: isSelected ? scheme.primary : scheme.onSurfaceVariant,
                ),
                const SizedBox(width: 4),
                Flexible(
                  child: Text(
                    label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 11.5,
                      fontWeight: isSelected ? FontWeight.w600 : FontWeight.w500,
                      color: isSelected ? scheme.onSurface : scheme.onSurfaceVariant,
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
