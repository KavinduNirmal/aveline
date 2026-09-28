import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/section_search_field.dart';
import '../../../customers/data/customer_repository.dart';
import '../../../customers/domain/customer.dart';
import '../../../customers/domain/customer_book.dart';
import '../../../customers/presentation/widgets/customer_avatar.dart';
import '../../../customers/presentation/widgets/customer_level_badge.dart';

/// Picks the client an associate is about to log a visit for.
///
/// Logging a visit is an edit to one client's record, so the action cannot go
/// anywhere useful until it knows whose record that is. This is the step that
/// answers it: the same book the Customers tab shows, narrowed by name, so the
/// client standing at the counter is found in one tap rather than by scrolling
/// the whole shop.
///
/// It reads the same [CustomerRepository] the profile screen resolves from
/// rather than Home's own highlight list, because the id it returns is what the
/// profile is addressed by: a client this sheet names is a client the profile
/// can actually open.
Future<void> showLogVisitSheet(
  BuildContext context, {
  required CustomerBookSource repository,
  required ValueChanged<String> onClientSelected,
}) {
  return showModalBottomSheet<void>(
    context: context,
    backgroundColor: Colors.transparent,
    isScrollControlled: true,
    builder: (context) => _LogVisitSheet(
      repository: repository,
      onClientSelected: onClientSelected,
    ),
  );
}

class _LogVisitSheet extends StatefulWidget {
  const _LogVisitSheet({
    required this.repository,
    required this.onClientSelected,
  });

  final CustomerBookSource repository;
  final ValueChanged<String> onClientSelected;

  @override
  State<_LogVisitSheet> createState() => _LogVisitSheetState();
}

class _LogVisitSheetState extends State<_LogVisitSheet> {
  final TextEditingController _search = TextEditingController();

  CustomerBook? _book;
  String _query = '';

  @override
  void initState() {
    super.initState();
    _fetch();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _fetch() async {
    CustomerBook book;
    try {
      book = await widget.repository.fetchBook();
    } catch (_) {
      book = CustomerBook.empty;
    }

    if (!mounted) {
      return;
    }
    setState(() => _book = book);
  }

  /// The rows the query leaves, grouped under the letter they file under so a
  /// long book reads the way the Customers tab does.
  ///
  /// Narrowed here rather than by re-fetching: the lookup is a search, and a
  /// sheet that refetched on every keystroke would flash its own loading state
  /// while the associate is still typing a name.
  List<CustomerSection> get _sections {
    final query = _query.trim().toLowerCase();
    final narrowed = <CustomerSection>[];

    for (final section in _book?.sections ?? const <CustomerSection>[]) {
      final matches = section.customers
          .where(
            (customer) =>
                query.isEmpty || customer.searchHaystack.contains(query),
          )
          .toList();
      if (matches.isNotEmpty) {
        narrowed.add(
          CustomerSection(letter: section.letter, customers: matches),
        );
      }
    }

    return narrowed;
  }

  void _select(Customer customer) {
    // The sheet is dismissed first, as the profile arrives over it: a choice
    // that leaves the sheet sitting on top of the answer reads as unfinished.
    Navigator.of(context).pop();
    widget.onClientSelected(customer.id);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final sections = _sections;
    final isLoading = _book == null;

    return Padding(
      // Lifts the sheet clear of the keyboard the search field raises.
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SafeArea(
        top: false,
        child: FractionallySizedBox(
          // Tall enough for a real stretch of the book, short enough that the
          // greeting it opened from is still visible behind it.
          heightFactor: 0.78,
          child: Container(
            margin: const EdgeInsets.all(12),
            padding: const EdgeInsets.fromLTRB(20, 12, 20, 8),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerLowest,
              borderRadius: BorderRadius.circular(24),
              boxShadow: [
                BoxShadow(
                  color: const Color(0xFF8B2E42).withValues(alpha: 0.12),
                  blurRadius: 28,
                  offset: const Offset(0, 10),
                ),
              ],
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Center(
                  child: Container(
                    width: 40,
                    height: 4,
                    decoration: BoxDecoration(
                      color: scheme.outlineVariant,
                      borderRadius: BorderRadius.circular(2),
                    ),
                  ),
                ),
                const SizedBox(height: 18),
                Text('Log a visit', style: theme.textTheme.headlineSmall),
                const SizedBox(height: 6),
                Text(
                  'Pick the client who came in, then log the visit on their '
                  'profile.',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
                const SizedBox(height: 16),
                SectionSearchField(
                  controller: _search,
                  hintText: 'Search this client book...',
                  hasQuery: _query.isNotEmpty,
                  onChanged: (value) => setState(() => _query = value),
                  onClear: () {
                    _search.clear();
                    setState(() => _query = '');
                  },
                  fieldKey: const Key('log_visit_search_field'),
                  clearKey: const Key('log_visit_search_clear'),
                ),
                const SizedBox(height: 12),
                Expanded(
                  child: isLoading
                      ? const Center(
                          child: SizedBox(
                            width: 22,
                            height: 22,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          ),
                        )
                      : sections.isEmpty
                      ? _NoMatches(query: _query)
                      : ListView.builder(
                          key: const Key('log_visit_clients'),
                          padding: const EdgeInsets.only(bottom: 12),
                          itemCount: sections.length,
                          itemBuilder: (context, index) => _Section(
                            section: sections[index],
                            onSelected: _select,
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

/// One letter of the book and the clients filed under it.
class _Section extends StatelessWidget {
  const _Section({required this.section, required this.onSelected});

  final CustomerSection section;
  final ValueChanged<Customer> onSelected;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 10, bottom: 4),
          child: Row(
            children: [
              Text(
                section.letter,
                style: theme.textTheme.labelMedium?.copyWith(
                  color: theme.colorScheme.primary,
                  fontWeight: FontWeight.w700,
                  letterSpacing: 1.2,
                ),
              ),
              const SizedBox(width: 10),
              Expanded(
                child: Container(
                  height: 1,
                  color: theme.colorScheme.outlineVariant.withValues(
                    alpha: 0.5,
                  ),
                ),
              ),
            ],
          ),
        ),
        for (final customer in section.customers)
          _ClientRow(customer: customer, onTap: () => onSelected(customer)),
      ],
    );
  }
}

/// One client, as a row that can be picked.
class _ClientRow extends StatelessWidget {
  const _ClientRow({required this.customer, required this.onTap});

  final Customer customer;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      type: MaterialType.transparency,
      child: InkWell(
        key: ValueKey('log_visit_client_${customer.id}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(16),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 9),
          child: Row(
            children: [
              CustomerAvatar(customer: customer, size: 42),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            customer.displayName,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleMedium,
                          ),
                        ),
                        const SizedBox(width: 8),
                        CustomerLevelBadge(level: customer.level),
                      ],
                    ),
                    const SizedBox(height: 3),
                    Text(
                      // When they were last in, which is the one thing that
                      // decides whether this is the client at the counter.
                      _lastVisitLabel(customer),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _lastVisitLabel(Customer customer) {
    final last = customer.lastVisitAtUtc;
    if (last == null) {
      return '${customer.idLabel} · Not visited yet';
    }
    return '${customer.idLabel} · Last visit ${relativeDay(last)}';
  }
}

/// What the sheet says when the book has nothing matching.
class _NoMatches extends StatelessWidget {
  const _NoMatches({required this.query});

  final String query;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final trimmed = query.trim();

    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 12),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(
              Icons.search_off_rounded,
              size: 28,
              color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
            ),
            const SizedBox(height: 12),
            Text(
              trimmed.isEmpty
                  ? 'The client book is empty.'
                  : 'No client matches "$trimmed".',
              textAlign: TextAlign.center,
              style: theme.textTheme.titleMedium,
            ),
            const SizedBox(height: 6),
            Text(
              trimmed.isEmpty
                  ? 'Clients appear here once the shop has a book to show.'
                  : 'Check the spelling, or add them as a walk-in from the '
                        'client row on Home.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}
