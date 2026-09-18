import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/brand_section_title.dart';
import '../../../../shared/widgets/section_search_field.dart';
import '../../data/customer_repository.dart';
import '../../data/demo_customer_repository.dart';
import '../../domain/customer.dart';
import '../../domain/customer_book.dart';
import '../../domain/customer_level.dart';
import '../customers_controller.dart';
import '../customer_section_offsets.dart';
import '../widgets/customer_alphabet_index.dart';
import '../widgets/customer_level_row.dart';
import '../widgets/customer_section_header.dart';
import '../widgets/customer_tile.dart';

/// Customers dock tab: the boutique's client book.
///
/// The title names the shop the book belongs to, and the field below it searches
/// this book only — the app-wide search in the header is a separate affordance
/// and is deliberately not what this screen opens. Under the field sit the client
/// levels, and under those the book itself, laid out like a phone's contact list:
/// a circle, the name, the id the shop keys them by, and their grade, with an
/// alphabet strip down the trailing edge that jumps to a letter.
class CustomersScreen extends StatefulWidget {
  const CustomersScreen({super.key, this.boutiqueName, this.repository});

  /// Overrides the boutique name, for tests and previews. When `null`, the name
  /// is read from [BoutiqueProvider], falling back to the brand.
  final String? boutiqueName;

  /// Overrides the book's source, for tests and previews. Defaults to the demo
  /// repository, which holds a boutique's worth of clients in memory.
  final CustomerRepository? repository;

  @override
  State<CustomersScreen> createState() => _CustomersScreenState();
}

class _CustomersScreenState extends State<CustomersScreen> {
  /// What the title reads before a boutique name is known.
  static const String _fallbackName = 'Aveline';

  final TextEditingController _searchController = TextEditingController();
  final ScrollController _scrollController = ScrollController();

  /// Measures the block above the sections, which is where the index's offsets
  /// start counting from.
  final GlobalKey _headerKey = GlobalKey();

  late final CustomersController _customers;

  /// The query the field currently holds, trimmed. Search is scoped to this
  /// screen: it narrows this book, not the whole app.
  String _query = '';

  /// The level narrowing the book, or `null` for every level.
  CustomerLevel? _level;

  /// Where the first letter's header starts, in the scroll view's coordinates.
  ///
  /// The block above it is as tall as the typeface makes it, so it is measured
  /// from the rendered header rather than assumed.
  double _listStart = 0;

  /// The letter at the top of the list, which the strip highlights.
  String? _activeLetter;

  @override
  void initState() {
    super.initState();
    _customers = CustomersController(
      widget.repository ?? DemoCustomerRepository(),
    );
    // Started before the listener is attached: `load` notifies synchronously, and
    // that must not reach `setState` from `initState`. Nothing is lost, because
    // the first build already reads the loading state.
    _customers.load(query: _customerQuery);
    _customers.addListener(_onBookChanged);
    _scrollController.addListener(_onScroll);
  }

  @override
  void dispose() {
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    _customers
      ..removeListener(_onBookChanged)
      ..dispose();
    _searchController.dispose();
    super.dispose();
  }

  /// The narrowing in force, as the book's source reads it.
  CustomerQuery get _customerQuery =>
      CustomerQuery(search: _query, level: _level);

  List<CustomerSection> get _sections => _customers.book.sections;

  void _onBookChanged() {
    setState(() {});
    _syncActiveLetter();
  }

  void _onScroll() => _syncActiveLetter();

  /// Keeps the strip's highlight on the letter at the top of the viewport.
  ///
  /// Derived from the scroll position rather than from the last letter tapped,
  /// because a jump near the end of the book runs out of scroll before it reaches
  /// the letter asked for: the highlight should say what is on screen, not what
  /// was requested.
  void _syncActiveLetter() {
    if (!_scrollController.hasClients || _sections.isEmpty) {
      return;
    }

    final index = CustomerSectionOffsets.sectionAt(
      _sections,
      _scrollController.offset,
      listStart: _listStart,
    );
    final letter = _sections[index].letter;
    if (letter != _activeLetter) {
      setState(() => _activeLetter = letter);
    }
  }

  void _reload() {
    _customers.load(query: _customerQuery);
    // A new narrowing starts at the top of the book, so the associate is not left
    // somewhere in the middle of a list they have not seen.
    if (_scrollController.hasClients) {
      _scrollController.jumpTo(0);
    }
  }

  void _onQueryChanged(String value) {
    final next = value.trim();
    if (next == _query) {
      return;
    }
    setState(() => _query = next);
    _reload();
  }

  void _clearQuery() {
    _searchController.clear();
    _onQueryChanged('');
  }

  /// Tapping the chosen level clears it, so the row can always be returned to
  /// every level without a separate "All" pill.
  void _toggleLevel(CustomerLevel level) {
    setState(() => _level = _level == level ? null : level);
    _reload();
  }

  void _clearAll() {
    _searchController.clear();
    setState(() {
      _query = '';
      _level = null;
    });
    _reload();
  }

  /// Moves the book to [letter].
  ///
  /// Instant rather than animated, which is what scanning a contact list does:
  /// the strip is dragged, and a tween between two letters would lag the finger.
  void _jumpToLetter(String letter) {
    if (!_scrollController.hasClients) {
      return;
    }

    final index = _sections.indexWhere((section) => section.letter == letter);
    if (index < 0) {
      return;
    }

    final offsets = CustomerSectionOffsets.all(
      _sections,
      listStart: _listStart,
    );
    _scrollController.jumpTo(
      offsets[index].clamp(0, _scrollController.position.maxScrollExtent),
    );
    // A jump that ran out of scroll leaves a different letter on screen, so the
    // highlight is taken from where the list actually landed.
    _syncActiveLetter();
  }

  void _openCustomer(Customer customer) {
    // The location carries the id; the profile screen resolves the client from
    // it, so nothing has to survive the router re-parsing the route.
    GoRouter.maybeOf(context)?.push(AppRoutes.customer(customer.id));
  }

  /// Opens the client form.
  ///
  /// One seam on purpose: the floating button is the only way in, and when the
  /// form lands it should not have to be found in more than one place. Until
  /// then the toast points at the path that does work — the walk-in slot on Home
  /// — rather than claiming nothing exists.
  void _addCustomer() {
    AppToast.show(
      context,
      "New clients are added from Home's walk-in slot for now.",
    );
  }

  /// The boutique name, or `null` when no provider is above the screen.
  String? _boutiqueNameOrNull(BuildContext context) {
    try {
      return context.watch<BoutiqueProvider>().name;
    } catch (_) {
      return null;
    }
  }

  /// Measures the block above the sections once it has been laid out.
  ///
  /// Scheduled only while the measurement is still missing: the block's height is
  /// fixed once the typeface is known, so this settles after the first frame
  /// instead of running on every rebuild.
  void _measureListStart() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) {
        return;
      }
      final box = _headerKey.currentContext?.findRenderObject();
      if (box is! RenderBox || !box.hasSize) {
        return;
      }
      if (box.size.height != _listStart) {
        setState(() => _listStart = box.size.height);
      }
    });
  }

  @override
  Widget build(BuildContext context) {
    final boutiqueName =
        widget.boutiqueName ?? _boutiqueNameOrNull(context) ?? _fallbackName;
    final letters = _customers.book.letters;

    if (_listStart == 0) {
      _measureListStart();
    }

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        CustomScrollView(
          key: const Key('customers_scroll'),
          controller: _scrollController,
          // Always scrollable, so the shell's pull-to-refresh still arms on a
          // screen whose content is shorter than the viewport.
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            // One sliver for the whole block above the book, so the index has a
            // single height to start counting from. The level row needs to bleed
            // to both edges, which is why it is not inside one padded column.
            SliverToBoxAdapter(
              child: Column(
                key: _headerKey,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(20, 20, 20, 0),
                    child: BrandSectionTitle(
                      boutiqueName: boutiqueName,
                      section: 'Customers',
                      titleKey: const Key('customers_title'),
                    ),
                  ),
                  Padding(
                    padding: const EdgeInsets.fromLTRB(20, 18, 20, 0),
                    child: SectionSearchField(
                      controller: _searchController,
                      hintText: 'Search this client book...',
                      hasQuery: _query.isNotEmpty,
                      onChanged: _onQueryChanged,
                      onClear: _clearQuery,
                      fieldKey: const Key('customers_search_field'),
                      clearKey: const Key('customers_search_clear'),
                    ),
                  ),
                  const SizedBox(height: 16),
                  CustomerLevelRow(
                    selected: _level,
                    onToggled: _toggleLevel,
                  ),
                  const SizedBox(height: 8),
                ],
              ),
            ),
            ..._bookSlivers(),
            // The animated Blossom floats over the bottom of the shell, so the
            // book keeps enough room to scroll clear of it.
            const SliverToBoxAdapter(child: SizedBox(height: 96)),
          ],
        ),
        if (letters.isNotEmpty)
          Positioned(
            top: 0,
            bottom: 0,
            right: 0,
            child: SafeArea(
              child: Center(
                child: Padding(
                  padding: const EdgeInsets.only(right: 2),
                  child: CustomerAlphabetIndex(
                    letters: letters,
                    activeLetter: _activeLetter,
                    onLetterSelected: _jumpToLetter,
                  ),
                ),
              ),
            ),
          ),
        // Floating where the thumb already is: a book this long is read
        // one-handed, and the counter-side entry point has to be reachable from
        // the bottom of the list without scrolling back to the top.
        Positioned(
          right: 20,
          bottom: 0,
          child: SafeArea(
            top: false,
            child: Padding(
              padding: const EdgeInsets.only(bottom: 24),
              child: FloatingActionButton(
                key: const Key('customers_new_fab'),
                tooltip: 'New customer',
                // Material 3's default FAB is a rounded square; this one is the
                // round mark the brand's floating affordances all wear.
                shape: const CircleBorder(),
                onPressed: _addCustomer,
                child: const Icon(Icons.person_add_alt_1_rounded),
              ),
            ),
          ),
        ),
      ],
    );
  }

  /// The book, and whatever stands in its place while it has nothing to show.
  List<Widget> _bookSlivers() {
    if (_customers.isLoading && !_customers.hasLoadedOnce) {
      return const [SliverToBoxAdapter(child: _CustomersLoading())];
    }

    if (_customers.errorMessage != null && _customers.book.isEmpty) {
      return [
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverToBoxAdapter(
            child: _CustomersError(
              message: _customers.errorMessage!,
              onRetry: () => _customers.load(),
            ),
          ),
        ),
      ];
    }

    if (_customers.isEmpty) {
      return [
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverToBoxAdapter(
            child: _CustomersEmptyState(
              query: _query,
              level: _level,
              onClearAll: _clearAll,
            ),
          ),
        ),
      ];
    }

    return [
      // Each letter is one group: a pinned header pins *within its own group* and
      // is pushed off by the next one. Sibling pinned headers in a plain sliver
      // list do not do that — their overlap accumulates and every letter already
      // scrolled past stays on screen, cascading down over the rows.
      for (final section in _sections)
        SliverMainAxisGroup(
          key: ValueKey('customer_group_${section.letter}'),
          slivers: [
            SliverPersistentHeader(
              pinned: true,
              delegate: CustomerSectionHeaderDelegate(letter: section.letter),
            ),
            SliverList(
              delegate: SliverChildBuilderDelegate((context, index) {
                final customer = section.customers[index];
                return CustomerTile(
                  key: ValueKey('customer_${customer.id}'),
                  customer: customer,
                  onTap: () => _openCustomer(customer),
                );
              }, childCount: section.customers.length),
            ),
          ],
        ),
      SliverToBoxAdapter(child: const _CustomersFooter()),
    ];
  }
}

/// The book's stand-in while it is on its way.
class _CustomersLoading extends StatelessWidget {
  const _CustomersLoading();

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

/// The quiet panel shown when the book could not be fetched.
class _CustomersError extends StatelessWidget {
  const _CustomersError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('customers_error'),
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
          Text('The client book could not load', style: theme.textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            message,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('customers_retry'),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: const Text('Try again'),
          ),
        ],
      ),
    );
  }
}

/// The quiet panel shown when the narrowing in force matches nobody.
class _CustomersEmptyState extends StatelessWidget {
  const _CustomersEmptyState({
    required this.query,
    required this.level,
    required this.onClearAll,
  });

  final String query;
  final CustomerLevel? level;
  final VoidCallback onClearAll;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final hasQuery = query.isNotEmpty;
    final hasLevel = level != null;

    final String heading;
    final String body;
    if (hasQuery && hasLevel) {
      heading = 'No ${level!.label} clients match "$query"';
      body = 'Try another name, nickname, or number, or clear one of the two.';
    } else if (hasQuery) {
      heading = 'No clients match "$query"';
      body =
          'Try another name, nickname, or number, or clear the search to see the '
          'whole book.';
    } else if (hasLevel) {
      heading = 'Nobody at ${level!.label} yet';
      body =
          'No client holds this grade. Clear the level to see the whole book.';
    } else {
      heading = 'Your client book is empty';
      body = 'Clients appear here as the boutique serves them.';
    }

    return Container(
      key: const Key('customers_empty_state'),
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
                : hasLevel
                ? Icons.filter_alt_off_rounded
                : Icons.people_outline,
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
          if (query.isNotEmpty || level != null) ...[
            const SizedBox(height: 12),
            TextButton.icon(
              key: const Key('customers_clear_all'),
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

/// What sits under the last client.
class _CustomersFooter extends StatelessWidget {
  const _CustomersFooter();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(top: 28),
      child: Center(
        child: Text(
          'That is the whole book.',
          key: const Key('customers_end_of_list'),
          style: theme.textTheme.labelSmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
      ),
    );
  }
}
