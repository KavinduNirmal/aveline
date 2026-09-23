import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../data/customer_repository.dart';
import '../../data/demo_customer_repository.dart';
import '../../domain/customer.dart';
import '../../domain/customer_detail.dart';
import '../customer_interactions_controller.dart';
import '../customer_palette.dart';
import '../widgets/customer_avatar.dart';
import '../widgets/customer_interaction_filter_row.dart';
import '../widgets/customer_quick_actions_bar.dart';
import '../widgets/log_interaction_sheet.dart';

/// Dedicated Customer Interaction & Timeline Page.
///
/// Displays the client dossier summary, communication quick actions,
/// full chronological interaction timeline with channel tinting and direction markers,
/// search and multi-criteria filters, and a floating interaction logging studio.
class CustomerInteractionScreen extends StatefulWidget {
  const CustomerInteractionScreen({
    super.key,
    required this.customerId,
    this.customer,
    this.repository,
  });

  final String customerId;
  final CustomerDetail? customer;
  final CustomerRepository? repository;

  @override
  State<CustomerInteractionScreen> createState() =>
      _CustomerInteractionScreenState();
}

class _CustomerInteractionScreenState extends State<CustomerInteractionScreen> {
  late final CustomerRepository _repository =
      widget.repository ?? DemoCustomerRepository();
  late final CustomerInteractionsController _controller;

  final ScrollController _scrollController = ScrollController();
  final ValueNotifier<double> _scrollOffset = ValueNotifier<double>(0);
  final TextEditingController _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _controller = CustomerInteractionsController(
      _repository,
      customerId: widget.customerId,
      initialDetail: widget.customer,
    );
    _scrollController.addListener(_onScroll);

    if (widget.customer == null) {
      _controller.load();
    }
  }

  @override
  void dispose() {
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    _scrollOffset.dispose();
    _searchController.dispose();
    _controller.dispose();
    super.dispose();
  }

  void _onScroll() {
    if (!_scrollController.hasClients) return;
    _scrollOffset.value = _scrollController.offset;
  }

  void _openLogSheet(BuildContext context, Customer customer) {
    LogInteractionSheet.show(
      context: context,
      customer: customer,
      onSubmit: (request) => _controller.recordInteraction(request),
    );
  }

  @override
  Widget build(BuildContext context) {
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        final detail = _controller.detail;
        final customer = _controller.customer;

        return Scaffold(
          body: Stack(
            children: [
              const Positioned.fill(child: BrandBackdrop()),
              Positioned.fill(
                child: _buildBody(detail, customer),
              ),
              Positioned(
                top: 0,
                left: 0,
                right: 0,
                child: _InteractionAppBar(
                  customer: customer,
                  scrollOffset: _scrollOffset,
                  onBack: () => Navigator.of(context).maybePop(),
                ),
              ),
            ],
          ),
          floatingActionButton: customer == null
              ? null
              : _LogInteractionFab(
                  onPressed: () => _openLogSheet(context, customer),
                ),
        );
      },
    );
  }

  Widget _buildBody(CustomerDetail? detail, Customer? customer) {
    if (_controller.isLoading && detail == null) {
      return const Center(
        child: SizedBox(
          width: 28,
          height: 28,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }

    if (detail == null || customer == null) {
      return Center(
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Text(
                'Customer Record Not Found',
                style: Theme.of(context).textTheme.headlineSmall,
              ),
              const SizedBox(height: 8),
              Text(
                _controller.errorMessage ??
                    'Could not find customer history for ID: ${widget.customerId}',
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: Theme.of(context).colorScheme.onSurfaceVariant,
                    ),
              ),
              const SizedBox(height: 16),
              TextButton.icon(
                onPressed: _controller.load,
                icon: const Icon(Icons.refresh_rounded, size: 16),
                label: const Text('Try Again'),
              ),
            ],
          ),
        ),
      );
    }

    final now = DateTime.now();
    final interactions = _controller.filteredInteractions;

    return ListView(
      controller: _scrollController,
      physics: const AlwaysScrollableScrollPhysics(),
      padding: EdgeInsets.only(
        top: MediaQuery.paddingOf(context).top + kToolbarHeight + 8,
        bottom: 110,
      ),
      children: [
        // 1. Client Identity & Financial Summary Card
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: _ClientSummaryCard(detail: detail, now: now),
        ),
        const SizedBox(height: 18),

        // 2. Quick Actions Bar
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: CustomerQuickActionsBar(customer: customer),
        ),
        const SizedBox(height: 24),

        // 3. Search Bar & Filter Rows
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              TextField(
                controller: _searchController,
                onChanged: _controller.setSearch,
                decoration: InputDecoration(
                  hintText: 'Search notes, staff, or messages...',
                  prefixIcon: const Icon(Icons.search_rounded, size: 20),
                  suffixIcon: _controller.search.isNotEmpty
                      ? IconButton(
                          icon: const Icon(Icons.clear_rounded, size: 18),
                          onPressed: () {
                            _searchController.clear();
                            _controller.setSearch('');
                          },
                        )
                      : null,
                ),
              ),
              const SizedBox(height: 14),
              CustomerInteractionFilterRow(
                selectedChannel: _controller.channel,
                selectedDirection: _controller.direction,
                onChannelSelected: _controller.setChannel,
                onDirectionSelected: _controller.setDirection,
              ),
            ],
          ),
        ),
        const SizedBox(height: 28),

        // 4. Timeline Header & Activity Rail
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: Row(
            children: [
              Expanded(
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Flexible(
                      child: Text(
                        'Interaction Timeline',
                        style: Theme.of(context).textTheme.titleLarge,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Text(
                      '(${interactions.length})',
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                            color: Theme.of(context).colorScheme.onSurfaceVariant,
                            fontWeight: FontWeight.w600,
                          ),
                    ),
                  ],
                ),
              ),
              if (_controller.channel != null ||
                  _controller.direction != null ||
                  _controller.search.isNotEmpty) ...[
                const SizedBox(width: 8),
                GestureDetector(
                  onTap: () {
                    _searchController.clear();
                    _controller.clearFilters();
                  },
                  child: Text(
                    'Clear filters',
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                          color: Theme.of(context).colorScheme.primary,
                          fontWeight: FontWeight.w700,
                        ),
                  ),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),

        // 5. Timeline Entries
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: interactions.isEmpty
              ? const _EmptyInteractionsView()
              : _TimelineRail(interactions: interactions, now: now),
        ),
      ],
    );
  }
}

/// Floating Action Button for recording a new interaction.
class _LogInteractionFab extends StatelessWidget {
  const _LogInteractionFab({required this.onPressed});

  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return FloatingActionButton.extended(
      onPressed: onPressed,
      backgroundColor: scheme.primary,
      foregroundColor: Colors.white,
      elevation: 4,
      icon: const Icon(Icons.add_rounded, size: 20),
      label: Text(
        'Log Interaction',
        style: theme.textTheme.labelLarge?.copyWith(
          color: Colors.white,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

/// Dynamic scrolling App Bar with faded background scrim.
class _InteractionAppBar extends StatelessWidget {
  const _InteractionAppBar({
    required this.customer,
    required this.scrollOffset,
    required this.onBack,
  });

  final Customer? customer;
  final ValueListenable<double> scrollOffset;
  final VoidCallback onBack;

  static const double _fadeEnd = 96;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final topInset = MediaQuery.paddingOf(context).top;

    return SizedBox(
      height: topInset + kToolbarHeight,
      child: Stack(
        children: [
          Positioned.fill(
            child: DecoratedBox(
              decoration: BoxDecoration(
                gradient: LinearGradient(
                  begin: Alignment.topCenter,
                  end: Alignment.bottomCenter,
                  colors: [
                    theme.colorScheme.surface.withValues(alpha: 0.94),
                    theme.colorScheme.surface.withValues(alpha: 0.60),
                    theme.colorScheme.surface.withValues(alpha: 0.0),
                  ],
                  stops: const [0.0, 0.6, 1.0],
                ),
              ),
            ),
          ),
          Align(
            alignment: Alignment.centerLeft,
            child: Padding(
              padding: EdgeInsets.only(top: topInset, left: 14),
              child: _CircularBackButton(onPressed: onBack),
            ),
          ),
          Align(
            alignment: Alignment.centerRight,
            child: Padding(
              padding: EdgeInsets.only(top: topInset, right: 16),
              child: ValueListenableBuilder<double>(
                valueListenable: scrollOffset,
                builder: (context, offset, child) => Opacity(
                  opacity: (offset / _fadeEnd).clamp(0.0, 1.0),
                  child: child,
                ),
                child: customer == null
                    ? const SizedBox.shrink()
                    : Row(
                        mainAxisSize: MainAxisSize.min,
                        children: [
                          Text(
                            customer!.displayName,
                            style: theme.textTheme.labelLarge?.copyWith(
                              color: theme.colorScheme.onSurface,
                            ),
                          ),
                          const SizedBox(width: 8),
                          CustomerAvatar(customer: customer!, size: 28),
                        ],
                      ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _CircularBackButton extends StatelessWidget {
  const _CircularBackButton({required this.onPressed});

  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final shape = CircleBorder(
      side: BorderSide(color: scheme.outlineVariant.withValues(alpha: 0.5)),
    );

    return Material(
      color: scheme.surfaceContainerLowest,
      shape: shape,
      elevation: 2,
      shadowColor: scheme.primary.withValues(alpha: 0.30),
      clipBehavior: Clip.antiAlias,
      child: InkWell(
        onTap: onPressed,
        customBorder: shape,
        child: SizedBox(
          width: 42,
          height: 42,
          child: Icon(
            Icons.arrow_back_rounded,
            size: 20,
            color: scheme.onSurface,
          ),
        ),
      ),
    );
  }
}

/// Luxury summary sheet presenting customer identity, grade, consent, and financial stats.
class _ClientSummaryCard extends StatelessWidget {
  const _ClientSummaryCard({
    required this.detail,
    required this.now,
  });

  final CustomerDetail detail;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final customer = detail.customer;
    final consent = detail.consent;
    final consentTone = consentColor(consent.status, scheme);

    return Container(
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(22),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.07),
            blurRadius: 24,
            offset: const Offset(0, 8),
          ),
        ],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(18, 18, 18, 0),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                CustomerAvatar(customer: customer, size: 68),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        customer.displayName,
                        style: theme.textTheme.displaySmall?.copyWith(
                          color: scheme.onSurface,
                          height: 1.1,
                        ),
                      ),
                      const SizedBox(height: 6),
                      Row(
                        children: [
                          Text(
                            customer.idLabel,
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: scheme.onSurfaceVariant,
                              letterSpacing: 1.2,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                          const SizedBox(width: 8),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 9,
                              vertical: 3,
                            ),
                            decoration: BoxDecoration(
                              color: scheme.primary.withValues(alpha: 0.10),
                              borderRadius: BorderRadius.circular(999),
                            ),
                            child: Text(
                              customer.status.label.toUpperCase(),
                              style: theme.textTheme.labelSmall?.copyWith(
                                color: scheme.primary,
                                fontWeight: FontWeight.w700,
                                fontSize: 10,
                                letterSpacing: 0.8,
                              ),
                            ),
                          ),
                        ],
                      ),
                      if (customer.hasNicknameAlias) ...[
                        const SizedBox(height: 4),
                        Text(
                          'Called "${customer.nickname}" on the floor',
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                            fontStyle: FontStyle.italic,
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(height: 14),
          // Consent Keyline
          Padding(
            padding: const EdgeInsets.symmetric(horizontal: 18),
            child: Container(
              padding: const EdgeInsets.fromLTRB(12, 8, 12, 8),
              decoration: BoxDecoration(
                color: consentTone.withValues(alpha: 0.07),
                borderRadius: BorderRadius.circular(12),
              ),
              child: Row(
                children: [
                  Icon(consentIcon(consent.status), size: 15, color: consentTone),
                  const SizedBox(width: 8),
                  Expanded(
                    child: Text(
                      'Consent: ${consent.statusLabel} · ${consent.detailLabel}',
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: consentTone,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 14),
          Divider(
            height: 1,
            thickness: 1,
            color: scheme.outlineVariant.withValues(alpha: 0.40),
          ),
          // 3-Metric Ledger
          Padding(
            padding: const EdgeInsets.fromLTRB(14, 14, 14, 16),
            child: Row(
              children: [
                Expanded(
                  child: _LedgerItem(
                    label: 'SPENT WITH US',
                    value: customer.totalSpentLabel,
                  ),
                ),
                Container(
                  width: 1,
                  height: 34,
                  color: scheme.outlineVariant.withValues(alpha: 0.40),
                ),
                Expanded(
                  child: _LedgerItem(
                    label: 'VISITS',
                    value: '${customer.visitCount}',
                  ),
                ),
                Container(
                  width: 1,
                  height: 34,
                  color: scheme.outlineVariant.withValues(alpha: 0.40),
                ),
                Expanded(
                  child: _LedgerItem(
                    label: 'LAST CONTACT',
                    value: customer.lastVisitAtUtc == null
                        ? 'Never'
                        : relativeDay(customer.lastVisitAtUtc!, now: now),
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _LedgerItem extends StatelessWidget {
  const _LedgerItem({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              fontSize: 9,
              letterSpacing: 0.9,
              fontWeight: FontWeight.w600,
            ),
          ),
          const SizedBox(height: 4),
          Text(
            value,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.titleMedium?.copyWith(
              color: scheme.onSurface,
              fontWeight: FontWeight.w700,
            ),
          ),
        ],
      ),
    );
  }
}

/// Continuous vertical timeline connecting every exchange chronologically.
class _TimelineRail extends StatelessWidget {
  const _TimelineRail({
    required this.interactions,
    required this.now,
  });

  final List<CustomerInteraction> interactions;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    return Column(
      children: [
        for (var i = 0; i < interactions.length; i++)
          _TimelineItem(
            interaction: interactions[i],
            now: now,
            isLast: i == interactions.length - 1,
          ),
      ],
    );
  }
}

class _TimelineItem extends StatelessWidget {
  const _TimelineItem({
    required this.interaction,
    required this.now,
    required this.isLast,
  });

  final CustomerInteraction interaction;
  final DateTime now;
  final bool isLast;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final tone = channelColor(interaction.channel, scheme);

    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          // Timeline rail node & connector
          Column(
            children: [
              Container(
                width: 36,
                height: 36,
                alignment: Alignment.center,
                decoration: BoxDecoration(
                  color: tone.withValues(alpha: 0.12),
                  shape: BoxShape.circle,
                  border: Border.all(
                    color: tone.withValues(alpha: 0.35),
                    width: 1.5,
                  ),
                ),
                child: Icon(
                  channelIcon(interaction.channel),
                  size: 17,
                  color: tone,
                ),
              ),
              if (!isLast)
                Expanded(
                  child: Container(
                    width: 1.5,
                    margin: const EdgeInsets.symmetric(vertical: 4),
                    color: scheme.outlineVariant.withValues(alpha: 0.65),
                  ),
                ),
            ],
          ),
          const SizedBox(width: 14),
          // Interaction Content Card
          Expanded(
            child: Padding(
              padding: EdgeInsets.only(bottom: isLast ? 4 : 22),
              child: Container(
                padding: const EdgeInsets.all(14),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.circular(16),
                  border: Border.all(
                    color: scheme.outlineVariant.withValues(alpha: 0.35),
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: const Color(0xFF8B2E42).withValues(alpha: 0.04),
                      blurRadius: 14,
                      offset: const Offset(0, 4),
                    ),
                  ],
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    // Header line: Direction + Channel + Time
                    Row(
                      children: [
                        Icon(
                          interaction.isInbound
                              ? Icons.south_west_rounded
                              : Icons.north_east_rounded,
                          size: 14,
                          color: interaction.isInbound
                              ? scheme.primary
                              : scheme.onSurfaceVariant,
                        ),
                        const SizedBox(width: 5),
                        Expanded(
                          child: Text(
                            '${interaction.directionLabel} · ${interaction.channelLabel}',
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.labelMedium?.copyWith(
                              color: scheme.onSurface,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                        const SizedBox(width: 6),
                        Text(
                          interaction.whenLabel(now: now),
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                            fontWeight: FontWeight.w400,
                          ),
                        ),
                      ],
                    ),
                    // Message / Note Body
                    if (interaction.messageContent != null &&
                        interaction.messageContent!.isNotEmpty) ...[
                      const SizedBox(height: 8),
                      Text(
                        interaction.messageContent!,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: scheme.onSurface,
                          height: 1.45,
                        ),
                      ),
                    ],
                    // Badges row: Purchase total + Staff Attribution + Tags
                    if (interaction.purchaseTotalLabel != null ||
                        interaction.staffMemberName != null ||
                        interaction.tags.isNotEmpty) ...[
                      const SizedBox(height: 10),
                      Wrap(
                        spacing: 6,
                        runSpacing: 6,
                        children: [
                          if (interaction.purchaseTotalLabel != null)
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 3,
                              ),
                              decoration: BoxDecoration(
                                color: scheme.primary.withValues(alpha: 0.10),
                                borderRadius: BorderRadius.circular(999),
                              ),
                              child: Row(
                                mainAxisSize: MainAxisSize.min,
                                children: [
                                  Icon(
                                    Icons.shopping_bag_outlined,
                                    size: 12,
                                    color: scheme.primary,
                                  ),
                                  const SizedBox(width: 4),
                                  Text(
                                    interaction.purchaseTotalLabel!,
                                    style: theme.textTheme.labelSmall?.copyWith(
                                      color: scheme.primary,
                                      fontWeight: FontWeight.w700,
                                      fontSize: 10.5,
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          if (interaction.staffMemberName != null)
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 3,
                              ),
                              decoration: BoxDecoration(
                                color: scheme.surfaceContainerHigh,
                                borderRadius: BorderRadius.circular(999),
                              ),
                              child: Text(
                                'Staff: ${interaction.staffMemberName}',
                                style: theme.textTheme.labelSmall?.copyWith(
                                  color: scheme.onSurfaceVariant,
                                  fontSize: 10.5,
                                  fontWeight: FontWeight.w500,
                                ),
                              ),
                            ),
                          for (final tag in interaction.tags)
                            Container(
                              padding: const EdgeInsets.symmetric(
                                horizontal: 8,
                                vertical: 3,
                              ),
                              decoration: BoxDecoration(
                                color: customerSlate.withValues(alpha: 0.10),
                                borderRadius: BorderRadius.circular(999),
                              ),
                              child: Text(
                                tag,
                                style: theme.textTheme.labelSmall?.copyWith(
                                  color: customerSlate,
                                  fontSize: 10.5,
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ),
                        ],
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _EmptyInteractionsView extends StatelessWidget {
  const _EmptyInteractionsView();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Center(
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 36, horizontal: 20),
        child: Column(
          children: [
            Icon(
              Icons.forum_outlined,
              size: 44,
              color: scheme.onSurfaceVariant.withValues(alpha: 0.4),
            ),
            const SizedBox(height: 12),
            Text(
              'No interactions match your filter',
              style: theme.textTheme.titleMedium?.copyWith(
                color: scheme.onSurface,
              ),
            ),
            const SizedBox(height: 4),
            Text(
              'Try adjusting your search or channel filter to view more exchanges.',
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
