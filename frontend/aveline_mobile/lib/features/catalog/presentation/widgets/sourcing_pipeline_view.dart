import 'package:flutter/material.dart';

import '../../domain/sourcing_request.dart';
import '../sourcing_controller.dart';
import 'archived_tickets_sheet.dart';
import 'sourcing_stage_chips.dart';
import 'sourcing_ticket_card.dart';

/// Luxury atelier sourcing pipeline feed featuring stage filtering, metrics, card folding, and archive management.
class SourcingPipelineView extends StatefulWidget {
  const SourcingPipelineView({
    super.key,
    required this.controller,
    required this.onCreateTicket,
  });

  final SourcingController controller;
  final VoidCallback onCreateTicket;

  @override
  State<SourcingPipelineView> createState() => _SourcingPipelineViewState();
}

class _SourcingPipelineViewState extends State<SourcingPipelineView> {
  final _searchController = TextEditingController();

  @override
  void initState() {
    super.initState();
    _searchController.addListener(_onSearchChanged);
  }

  @override
  void dispose() {
    _searchController.removeListener(_onSearchChanged);
    _searchController.dispose();
    super.dispose();
  }

  void _onSearchChanged() {
    widget.controller.setSearchQuery(_searchController.text);
  }

  void _showArchivedSheet(BuildContext context) {
    showModalBottomSheet<void>(
      context: context,
      isScrollControlled: true,
      backgroundColor: Colors.transparent,
      builder: (_) => ArchivedTicketsSheet(controller: widget.controller),
    );
  }

  Future<void> _handleArchive(BuildContext context, SourcingRequest ticket) async {
    final previousStatus = await widget.controller.archiveTicket(ticket);
    if (!context.mounted) return;

    if (previousStatus != null) {
      ScaffoldMessenger.of(context).clearSnackBars();
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text('Archived "${ticket.clientName}" commission'),
          duration: const Duration(seconds: 4),
          action: SnackBarAction(
            label: 'Undo',
            textColor: Theme.of(context).colorScheme.inversePrimary,
            onPressed: () {
              widget.controller.restoreTicket(ticket, targetStage: previousStatus);
            },
          ),
        ),
      );
    }
  }

  void _toggleFoldAll() {
    final tickets = widget.controller.activeTickets;
    if (tickets.isEmpty) return;

    final currentStage = widget.controller.selectedStage;
    final allFolded = currentStage != null
        ? widget.controller.allFoldedIn(currentStage)
        : tickets.every((t) => widget.controller.isCollapsed(t.id));

    widget.controller.setCollapsedFor(
      tickets.map((t) => t.id).toList(),
      !allFolded,
    );
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isDark = theme.brightness == Brightness.dark;

    return ListenableBuilder(
      listenable: widget.controller,
      builder: (context, _) {
        if (widget.controller.isLoading && widget.controller.allTickets.isEmpty) {
          return const Center(child: CircularProgressIndicator());
        }

        final activeTickets = widget.controller.activeTickets;
        final archivedCount = widget.controller.archivedTickets.length;
        final currentStage = widget.controller.selectedStage;
        final allFolded = currentStage != null
            ? widget.controller.allFoldedIn(currentStage)
            : (activeTickets.isNotEmpty && activeTickets.every((t) => widget.controller.isCollapsed(t.id)));

        return RefreshIndicator(
          onRefresh: widget.controller.loadData,
          child: CustomScrollView(
            physics: const AlwaysScrollableScrollPhysics(),
            slivers: [
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(16, 12, 16, 8),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      // KPI Metrics Row
                      _buildKpiMetrics(scheme, isDark),
                      const SizedBox(height: 12),

                      // Search Input & Action Icons
                      Row(
                        children: [
                          Expanded(
                            child: TextField(
                              key: const Key('sourcing_search_field'),
                              controller: _searchController,
                              decoration: InputDecoration(
                                hintText: 'Search client, fabric, atelier...',
                                prefixIcon: const Icon(Icons.search, size: 18),
                                suffixIcon: _searchController.text.isNotEmpty
                                    ? IconButton(
                                        icon: const Icon(Icons.clear, size: 16),
                                        onPressed: () {
                                          _searchController.clear();
                                          widget.controller.setSearchQuery('');
                                        },
                                      )
                                    : null,
                                filled: true,
                                fillColor: scheme.surfaceContainerHighest.withValues(alpha: 0.4),
                                contentPadding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
                                border: OutlineInputBorder(
                                  borderRadius: BorderRadius.circular(12),
                                  borderSide: BorderSide.none,
                                ),
                              ),
                            ),
                          ),
                          const SizedBox(width: 8),

                          // Archived Drawer Button
                          IconButton.filledTonal(
                            key: const Key('sourcing_archived_btn'),
                            onPressed: () => _showArchivedSheet(context),
                            icon: Badge(
                              isLabelVisible: archivedCount > 0,
                              label: Text(archivedCount.toString()),
                              child: const Icon(Icons.inventory_2_outlined, size: 20),
                            ),
                            tooltip: 'Archived commissions',
                          ),

                          // Fold / Unfold All Toggle
                          IconButton.filledTonal(
                            key: const Key('sourcing_fold_all_btn'),
                            onPressed: activeTickets.isEmpty ? null : _toggleFoldAll,
                            icon: Icon(
                              allFolded ? Icons.unfold_more : Icons.unfold_less,
                              size: 20,
                            ),
                            tooltip: allFolded ? 'Expand all' : 'Fold all',
                          ),
                        ],
                      ),
                      const SizedBox(height: 12),

                      // Stage Chips Capsule Bar
                      SourcingStageChips(controller: widget.controller),
                      const SizedBox(height: 12),

                      // Error Banner (if any)
                      if (widget.controller.errorMessage != null) ...[
                        Container(
                          padding: const EdgeInsets.all(10),
                          margin: const EdgeInsets.only(bottom: 12),
                          decoration: BoxDecoration(
                            color: scheme.errorContainer,
                            borderRadius: BorderRadius.circular(10),
                          ),
                          child: Row(
                            children: [
                              Icon(Icons.error_outline, size: 16, color: scheme.onErrorContainer),
                              const SizedBox(width: 8),
                              Expanded(
                                child: Text(
                                  widget.controller.errorMessage!,
                                  style: TextStyle(color: scheme.onErrorContainer, fontSize: 12),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ),

              // Tickets List / Empty State
              if (activeTickets.isEmpty)
                SliverFillRemaining(
                  hasScrollBody: false,
                  child: _buildEmptyState(scheme, theme),
                )
              else
                SliverPadding(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 96),
                  sliver: SliverList(
                    delegate: SliverChildBuilderDelegate(
                      (context, index) {
                        final ticket = activeTickets[index];
                        final isCollapsed = widget.controller.isCollapsed(ticket.id);

                        return SourcingTicketCard(
                          ticket: ticket,
                          isCollapsed: isCollapsed,
                          onToggleCollapse: () => widget.controller.toggleCollapse(ticket.id),
                          onStatusChange: (newStatus) async {
                            final success = await widget.controller.updateTicketStatus(ticket.id, newStatus);
                            if (context.mounted && success) {
                              ScaffoldMessenger.of(context).showSnackBar(
                                SnackBar(
                                  content: Text('Updated "${ticket.clientName}" to ${newStatus.label}'),
                                  duration: const Duration(seconds: 2),
                                ),
                              );
                            }
                          },
                          onArchive: () => _handleArchive(context, ticket),
                        );
                      },
                      childCount: activeTickets.length,
                    ),
                  ),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _buildKpiMetrics(ColorScheme scheme, bool isDark) {
    final activeCount = widget.controller.totalActiveCount;
    final totalCost = widget.controller.estimatedAtelierVolume;
    final avgMargin = widget.controller.averageMarginPercent;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
      ),
      child: Row(
        children: [
          Expanded(
            child: _KpiItem(
              label: 'ACTIVE TICKETS',
              value: activeCount.toString(),
              scheme: scheme,
            ),
          ),
          Container(
            height: 32,
            width: 1,
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: _KpiItem(
              label: 'ATELIER VOLUME',
              value: '\$${(totalCost / 1000).toStringAsFixed(1)}k',
              scheme: scheme,
            ),
          ),
          Container(
            height: 32,
            width: 1,
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: _KpiItem(
              label: 'AVG MARGIN',
              value: '+${avgMargin.toStringAsFixed(1)}%',
              scheme: scheme,
              valueColor: const Color(0xFF10B981),
            ),
          ),
        ],
      ),
    );
  }

  Widget _buildEmptyState(ColorScheme scheme, ThemeData theme) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Container(
              padding: const EdgeInsets.all(22),
              decoration: BoxDecoration(
                color: scheme.surfaceContainerHighest.withValues(alpha: 0.4),
                shape: BoxShape.circle,
              ),
              child: Icon(
                Icons.checkroom_outlined,
                size: 44,
                color: scheme.onSurfaceVariant.withValues(alpha: 0.6),
              ),
            ),
            const SizedBox(height: 16),
            Text(
              'No Commissions in Pipeline',
              style: theme.textTheme.titleMedium?.copyWith(fontWeight: FontWeight.bold),
            ),
            const SizedBox(height: 6),
            Text(
              widget.controller.selectedStage != null
                  ? 'There are no bespoke requests currently in "${widget.controller.selectedStage!.label}".'
                  : 'Start by creating a new bespoke sourcing ticket for your client.',
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 13, color: scheme.onSurfaceVariant),
            ),
            const SizedBox(height: 20),
            FilledButton.icon(
              key: const Key('sourcing_empty_create_btn'),
              onPressed: widget.onCreateTicket,
              icon: const Icon(Icons.add, size: 18),
              label: const Text('Create Sourcing Ticket'),
            ),
          ],
        ),
      ),
    );
  }
}

class _KpiItem extends StatelessWidget {
  const _KpiItem({
    required this.label,
    required this.value,
    required this.scheme,
    this.valueColor,
  });

  final String label;
  final String value;
  final ColorScheme scheme;
  final Color? valueColor;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          label,
          style: TextStyle(
            fontSize: 9.5,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.6,
            color: scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 3),
        Text(
          value,
          style: TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.bold,
            color: valueColor ?? scheme.onSurface,
          ),
        ),
      ],
    );
  }
}
