import 'package:flutter/material.dart';

import '../../domain/sourcing_request.dart';
import '../../domain/sourcing_status.dart';

/// Luxury boutique card representing a bespoke sourcing commission ticket.
class SourcingTicketCard extends StatelessWidget {
  const SourcingTicketCard({
    super.key,
    required this.ticket,
    required this.isCollapsed,
    required this.onToggleCollapse,
    required this.onStatusChange,
    required this.onArchive,
    this.onTap,
  });

  final SourcingRequest ticket;
  final bool isCollapsed;
  final VoidCallback onToggleCollapse;
  final ValueChanged<SourcingStatus> onStatusChange;
  final VoidCallback onArchive;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isDark = theme.brightness == Brightness.dark;

    final status = ticket.status;
    final badgeBg = status.badgeBackgroundColor(isDark);
    final isPositiveMargin = ticket.marginAmount >= 0;

    return Container(
      key: Key('sourcing_ticket_${ticket.id}'),
      margin: const EdgeInsets.only(bottom: 12),
      decoration: BoxDecoration(
        color: scheme.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(
          color: isCollapsed
              ? scheme.outlineVariant.withValues(alpha: 0.7)
              : scheme.primary.withValues(alpha: 0.35),
          width: isCollapsed ? 1 : 1.5,
        ),
        boxShadow: [
          BoxShadow(
            color: Colors.black.withValues(alpha: isCollapsed ? 0.03 : 0.07),
            blurRadius: isCollapsed ? 6 : 14,
            offset: const Offset(0, 3),
          ),
        ],
      ),
      child: Material(
        color: Colors.transparent,
        borderRadius: BorderRadius.circular(16),
        child: InkWell(
          onTap: onTap ?? onToggleCollapse,
          borderRadius: BorderRadius.circular(16),
          child: Padding(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                // Header: Client Name, Category & Status Badge & Actions
                Row(
                  crossAxisAlignment: CrossAxisAlignment.center,
                  children: [
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Row(
                            children: [
                              Flexible(
                                child: Text(
                                  ticket.clientName,
                                  style: theme.textTheme.titleMedium?.copyWith(
                                    fontWeight: FontWeight.bold,
                                    letterSpacing: -0.2,
                                  ),
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                ),
                              ),
                              const SizedBox(width: 8),
                              Container(
                                padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
                                decoration: BoxDecoration(
                                  color: scheme.surfaceContainerHighest.withValues(alpha: 0.6),
                                  borderRadius: BorderRadius.circular(6),
                                ),
                                child: Text(
                                  ticket.category,
                                  style: TextStyle(
                                    fontSize: 10,
                                    fontWeight: FontWeight.w600,
                                    color: scheme.onSurfaceVariant,
                                  ),
                                ),
                              ),
                            ],
                          ),
                          const SizedBox(height: 4),
                          // Status Badge
                          Container(
                            padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                            decoration: BoxDecoration(
                              color: badgeBg,
                              borderRadius: BorderRadius.circular(8),
                              border: Border.all(
                                color: status.badgeColor.withValues(alpha: 0.4),
                                width: 0.8,
                              ),
                            ),
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Container(
                                  width: 6,
                                  height: 6,
                                  decoration: BoxDecoration(
                                    color: status.badgeColor,
                                    shape: BoxShape.circle,
                                  ),
                                ),
                                const SizedBox(width: 5),
                                Flexible(
                                  child: Text(
                                    status.shortLabel,
                                    maxLines: 1,
                                    overflow: TextOverflow.ellipsis,
                                    style: TextStyle(
                                      fontSize: 10.5,
                                      fontWeight: FontWeight.w700,
                                      color: isDark ? status.badgeColor : status.badgeColor.withValues(alpha: 0.9),
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(width: 8),
                    // Actions: Archive & Expand/Fold
                    IconButton(
                      key: Key('sourcing_archive_btn_${ticket.id}'),
                      icon: Icon(
                        ticket.isArchived ? Icons.unarchive_outlined : Icons.archive_outlined,
                        size: 20,
                        color: scheme.onSurfaceVariant,
                      ),
                      tooltip: ticket.isArchived ? 'Restore' : 'Archive',
                      onPressed: onArchive,
                      visualDensity: VisualDensity.compact,
                    ),
                    IconButton(
                      key: Key('sourcing_toggle_fold_${ticket.id}'),
                      icon: Icon(
                        isCollapsed ? Icons.expand_more : Icons.expand_less,
                        size: 22,
                        color: scheme.primary,
                      ),
                      tooltip: isCollapsed ? 'Expand details' : 'Fold card',
                      onPressed: onToggleCollapse,
                      visualDensity: VisualDensity.compact,
                    ),
                  ],
                ),

                // Collapsed View
                if (isCollapsed) ...[
                  const SizedBox(height: 10),
                  Text(
                    ticket.itemDescription,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 12.5,
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                  const SizedBox(height: 8),
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Text(
                          'Cost \$${ticket.estimatedCost.toStringAsFixed(0)}  •  Target \$${ticket.targetPrice.toStringAsFixed(0)}',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w500,
                            color: scheme.onSurface,
                          ),
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      const SizedBox(width: 8),
                      Container(
                        padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
                        decoration: BoxDecoration(
                          color: isPositiveMargin
                              ? const Color(0xFF10B981).withValues(alpha: 0.15)
                              : Colors.red.withValues(alpha: 0.12),
                          borderRadius: BorderRadius.circular(6),
                        ),
                        child: Text(
                          '${isPositiveMargin ? '+' : ''}${ticket.marginPercentage.toStringAsFixed(1)}% margin',
                          style: TextStyle(
                            fontSize: 11,
                            fontWeight: FontWeight.bold,
                            color: isPositiveMargin
                                ? const Color(0xFF10B981)
                                : const Color(0xFFEF4444),
                          ),
                        ),
                      ),
                    ],
                  ),
                ],

                // Expanded View
                if (!isCollapsed) ...[
                  const SizedBox(height: 14),

                  // Reference Image (if available)
                  if (ticket.referenceImageUrl != null && ticket.referenceImageUrl!.trim().isNotEmpty) ...[
                    ClipRRect(
                      borderRadius: BorderRadius.circular(12),
                      child: Container(
                        height: 150,
                        width: double.infinity,
                        color: scheme.surfaceContainerLowest,
                        child: Image.network(
                          ticket.referenceImageUrl!,
                          fit: BoxFit.cover,
                          errorBuilder: (context, error, stackTrace) => Container(
                            color: scheme.surfaceContainerHighest.withValues(alpha: 0.5),
                            child: Center(
                              child: Icon(Icons.broken_image_outlined, size: 28, color: scheme.outline),
                            ),
                          ),
                        ),
                      ),
                    ),
                    const SizedBox(height: 12),
                  ],

                  // Item description
                  Text(
                    ticket.itemDescription,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: scheme.onSurface,
                      height: 1.4,
                    ),
                  ),
                  const SizedBox(height: 10),

                  // Color & Attributes
                  if (ticket.color.trim().isNotEmpty) ...[
                    Row(
                      children: [
                        Icon(Icons.palette_outlined, size: 14, color: scheme.onSurfaceVariant),
                        const SizedBox(width: 6),
                        Flexible(
                          child: Text.rich(
                            TextSpan(
                              text: 'Color: ',
                              style: TextStyle(fontSize: 12, color: scheme.onSurfaceVariant),
                              children: [
                                TextSpan(
                                  text: ticket.color,
                                  style: TextStyle(
                                    fontSize: 12,
                                    fontWeight: FontWeight.w600,
                                    color: scheme.onSurface,
                                  ),
                                ),
                              ],
                            ),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 12),
                  ],

                  // Financials Breakdown Card
                  Container(
                    padding: const EdgeInsets.all(12),
                    decoration: BoxDecoration(
                      color: scheme.surfaceContainerLowest,
                      borderRadius: BorderRadius.circular(12),
                      border: Border.all(color: scheme.outlineVariant.withValues(alpha: 0.6)),
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Icon(Icons.monetization_on_outlined, size: 15, color: scheme.primary),
                            const SizedBox(width: 6),
                            Flexible(
                              child: Text(
                                'FINANCIALS & PROFITABILITY',
                                style: TextStyle(
                                  fontSize: 10,
                                  fontWeight: FontWeight.bold,
                                  letterSpacing: 0.8,
                                  color: scheme.primary,
                                ),
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 10),
                        Row(
                          children: [
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    'Target Retail',
                                    style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    '\$${ticket.targetPrice.toStringAsFixed(2)}',
                                    style: const TextStyle(fontSize: 14, fontWeight: FontWeight.bold),
                                  ),
                                ],
                              ),
                            ),
                            Container(
                              height: 28,
                              width: 1,
                              color: scheme.outlineVariant.withValues(alpha: 0.5),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    'Atelier Cost',
                                    style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    '\$${ticket.estimatedCost.toStringAsFixed(2)}',
                                    style: const TextStyle(fontSize: 14, fontWeight: FontWeight.bold),
                                  ),
                                ],
                              ),
                            ),
                            Container(
                              height: 28,
                              width: 1,
                              color: scheme.outlineVariant.withValues(alpha: 0.5),
                            ),
                            const SizedBox(width: 12),
                            Expanded(
                              child: Column(
                                crossAxisAlignment: CrossAxisAlignment.start,
                                children: [
                                  Text(
                                    'Est. Margin',
                                    style: TextStyle(fontSize: 11, color: scheme.onSurfaceVariant),
                                  ),
                                  const SizedBox(height: 2),
                                  Text(
                                    '${isPositiveMargin ? '+' : ''}${ticket.marginPercentage.toStringAsFixed(1)}%',
                                    style: TextStyle(
                                      fontSize: 14,
                                      fontWeight: FontWeight.bold,
                                      color: isPositiveMargin
                                          ? const Color(0xFF10B981)
                                          : const Color(0xFFEF4444),
                                    ),
                                  ),
                                ],
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),

                  // Supplier / Atelier Attribution
                  if (ticket.supplierName.trim().isNotEmpty) ...[
                    Container(
                      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerHighest.withValues(alpha: 0.4),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Row(
                        children: [
                          Icon(Icons.business_outlined, size: 16, color: scheme.onSurfaceVariant),
                          const SizedBox(width: 8),
                          Expanded(
                            child: Text(
                              'Assigned: ${ticket.supplierName}',
                              style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w500,
                                color: scheme.onSurface,
                              ),
                              overflow: TextOverflow.ellipsis,
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 14),
                  ],

                  // Notes (if available)
                  if (ticket.notes != null && ticket.notes!.trim().isNotEmpty) ...[
                    Container(
                      padding: const EdgeInsets.all(10),
                      decoration: BoxDecoration(
                        color: scheme.surfaceContainerHighest.withValues(alpha: 0.25),
                        borderRadius: BorderRadius.circular(8),
                      ),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Icon(Icons.speaker_notes_outlined, size: 14, color: scheme.onSurfaceVariant),
                          const SizedBox(width: 6),
                          Expanded(
                            child: Text(
                              ticket.notes!,
                              style: TextStyle(
                                fontSize: 11.5,
                                fontStyle: FontStyle.italic,
                                color: scheme.onSurfaceVariant,
                              ),
                            ),
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 14),
                  ],

                  // Stage Progression Dropdown / Selector
                  Container(
                    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 4),
                    decoration: BoxDecoration(
                      color: scheme.surfaceContainerLowest,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(color: scheme.outlineVariant),
                    ),
                    child: Row(
                      children: [
                        Text(
                          'Pipeline Stage:',
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: FontWeight.w600,
                            color: scheme.onSurfaceVariant,
                          ),
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: DropdownButtonHideUnderline(
                            child: DropdownButton<SourcingStatus>(
                              key: Key('sourcing_status_dropdown_${ticket.id}'),
                              value: ticket.status,
                              isDense: true,
                              isExpanded: true,
                              icon: const Icon(Icons.arrow_drop_down, size: 20),
                              items: SourcingStatus.values.map((s) {
                                return DropdownMenuItem<SourcingStatus>(
                                  value: s,
                                  child: Row(
                                    mainAxisSize: MainAxisSize.min,
                                    children: [
                                      Container(
                                        width: 8,
                                        height: 8,
                                        decoration: BoxDecoration(
                                          color: s.badgeColor,
                                          shape: BoxShape.circle,
                                        ),
                                      ),
                                      const SizedBox(width: 8),
                                      Flexible(
                                        child: Text(
                                          s.label,
                                          style: const TextStyle(fontSize: 12.5),
                                          maxLines: 1,
                                          overflow: TextOverflow.ellipsis,
                                        ),
                                      ),
                                    ],
                                  ),
                                );
                              }).toList(),
                              onChanged: (newStatus) {
                                if (newStatus != null && newStatus != ticket.status) {
                                  onStatusChange(newStatus);
                                }
                              },
                            ),
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
      ),
    );
  }
}
