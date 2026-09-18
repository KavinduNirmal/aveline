import 'package:flutter/material.dart';

/// What a tile reveals when it is swiped, drawn behind it.
///
/// Sized to the tile it sits behind, so the panel reads as the tile turning over
/// rather than as a card floating underneath it. [alignment] decides which edge
/// the mark and the label hug: the leading edge for a swipe that starts at the
/// left, the trailing edge for one that starts at the right.
class NotificationSwipeBackground extends StatelessWidget {
  const NotificationSwipeBackground({
    super.key,
    required this.icon,
    required this.label,
    required this.tint,
    required this.alignment,
  });

  final IconData icon;

  /// What the gesture will do, in the fewest words that still say it.
  final String label;

  final Color tint;

  /// [Alignment.centerLeft] or [Alignment.centerRight].
  final Alignment alignment;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Container(
      decoration: BoxDecoration(
        // Tinted rather than filled: the gesture is a choice the associate is
        // still making, and a saturated panel would read as one already made.
        color: tint.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(16),
      ),
      alignment: alignment,
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 20, color: tint),
          const SizedBox(width: 8),
          Text(
            label,
            style: theme.textTheme.labelLarge?.copyWith(
              color: tint,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }
}
