import 'package:flutter/material.dart';

import '../../../../shared/widgets/trailing_fade.dart';

/// The one-tap floor tools pinned under the Home greeting.
///
/// [unavailableMessage] is the honest answer for actions whose slice is not on
/// mobile yet, so a tap never lands on nothing.
enum QuickAction {
  // The one floor tool that is about the client standing at the counter rather
  // than about the shop's own records, so it leads the row.
  logVisit('Log a visit', Icons.storefront_outlined, null),
  clockIn('Clock in', Icons.access_time_rounded, 'Clock-in is not on mobile yet.'),
  loyaltyCard(
    'Loyalty card',
    Icons.qr_code_scanner_rounded,
    'Loyalty card scanning is not on mobile yet.',
  ),
  catalog('Catalog', Icons.checkroom_outlined, null),
  customers('Customers', Icons.people_outline, null),
  messages('Messages', Icons.chat_bubble_outline_rounded, null),
  more('More', Icons.more_horiz_rounded, null);

  const QuickAction(this.label, this.icon, this.unavailableMessage);

  final String label;
  final IconData icon;

  /// Why nothing opened, or `null` when the action has a real destination.
  final String? unavailableMessage;
}

/// A horizontally scrolling row of circular quick actions.
///
/// The row bleeds to both screen edges and is slightly wider than a phone, so
/// the last action is always partly out of view. A fade on the trailing edge
/// says "there is more" and is dropped once the row is scrolled to its end.
class QuickActionsRow extends StatefulWidget {
  const QuickActionsRow({super.key, required this.onSelected});

  /// Called with the tapped action. Home decides whether that means a route,
  /// the overflow sheet, or a note that the slice is not built yet.
  final ValueChanged<QuickAction> onSelected;

  /// Fits the enclosure plus its labels.
  static const double height = 92;

  @override
  State<QuickActionsRow> createState() => _QuickActionsRowState();
}

class _QuickActionsRowState extends State<QuickActionsRow> {
  final ScrollController _controller = ScrollController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: QuickActionsRow.height,
      child: TrailingFade(
        controller: _controller,
        child: ListView.separated(
          controller: _controller,
          scrollDirection: Axis.horizontal,
          padding: TrailingFade.endPadding(context),
          itemCount: QuickAction.values.length,
          separatorBuilder: (context, index) => const SizedBox(width: 8),
          itemBuilder: (context, index) {
            final action = QuickAction.values[index];
            return _QuickActionButton(
              action: action,
              onTap: () => widget.onSelected(action),
            );
          },
        ),
      ),
    );
  }
}

/// A soft-circle icon enclosure with its label, echoing the jewelry-button
/// shape language in `DESIGN.md`.
class _QuickActionButton extends StatelessWidget {
  const _QuickActionButton({required this.action, required this.onTap});

  final QuickAction action;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Semantics(
      button: true,
      label: action.label,
      // The transparent Material keeps the ink splash working wherever this row
      // is mounted, including inside the Home sliver, without an ambient
      // Material from the caller.
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          borderRadius: BorderRadius.circular(28),
          child: SizedBox(
            // Wide enough for the longest label (`Customers`) to stay on one
            // line at the 12pt label token. Narrower and it breaks mid-word,
            // which reads as a typo rather than as a wrap.
            width: 64,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Container(
                  width: 54,
                  height: 54,
                  decoration: BoxDecoration(
                    color: scheme.surfaceContainerLowest,
                    shape: BoxShape.circle,
                    border: Border.all(
                      color: scheme.outlineVariant.withValues(alpha: 0.7),
                    ),
                    boxShadow: [
                      BoxShadow(
                        color: const Color(0xFF8B2E42).withValues(alpha: 0.07),
                        blurRadius: 14,
                        offset: const Offset(0, 4),
                      ),
                    ],
                  ),
                  child: Icon(action.icon, size: 23, color: scheme.primary),
                ),
                const SizedBox(height: 7),
                Text(
                  action.label,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                    height: 1.15,
                    letterSpacing: 0.1,
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
