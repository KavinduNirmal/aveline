import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../domain/app_notification.dart';
import 'notification_kind_visuals.dart';
import 'notification_swipe_background.dart';

/// One notification in the inbox.
///
/// Three gestures live on this tile and they have to stay out of each other's
/// way:
/// - **tap the header** opens the notification, unfolding it in place. Opening
///   one closes the one before it, because an accordion that stacks panels turns
///   a long inbox into a wall.
/// - **swipe right** marks it read. The tile springs back: a read notification is
///   still a notification, and taking it away would hide the very thing the
///   associate just dealt with.
/// - **swipe left** deletes it. The tile leaves, and the screen offers the undo.
///
/// Read tiles step back rather than disappear: no shadow, a recessed fill and a
/// lighter title, so a long inbox still shows at a glance what is new.
class NotificationTile extends StatelessWidget {
  const NotificationTile({
    super.key,
    required this.notification,
    required this.expanded,
    required this.onToggle,
    required this.onMarkRead,
    required this.onDelete,
    this.onOpen,
  });

  final AppNotification notification;

  /// Whether the notification is unfolded.
  final bool expanded;

  /// Opens or closes the notification.
  final VoidCallback onToggle;

  /// Marks it read. Awaited so the gesture's own animation waits for the reply.
  final Future<void> Function() onMarkRead;

  /// Takes it out of the inbox.
  final VoidCallback onDelete;

  /// Opens the client this notification is about, or `null` when it addresses
  /// nothing the app can open.
  final VoidCallback? onOpen;

  /// The tile's own duration for the unfold, so the motion is set in one place.
  static const Duration unfoldDuration = Duration(milliseconds: 220);

  /// What the motion becomes under reduced motion.
  ///
  /// Not `Duration.zero`. A zero-length [AnimatedSize] restarts its controller
  /// from inside its own `performLayout`, and a controller that finishes with no
  /// duration notifies its listeners on the spot, which marks the render object
  /// needing layout while it is already laying out - Flutter rejects that. One
  /// millisecond takes the ordinary ticker path and is instant to the eye.
  static const Duration _instant = Duration(milliseconds: 1);

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final reduceMotion = MediaQuery.disableAnimationsOf(context);

    return Dismissible(
      key: ValueKey('notification_dismissible_${notification.id}'),
      // Both ways: right to read, left to delete.
      direction: DismissDirection.horizontal,
      movementDuration: reduceMotion ? _instant : unfoldDuration,
      // `null`, not `Duration.zero`: a zero-length resize completes the
      // controller before Dismissible's own build runs, which trips its
      // "still part of the tree" assertion. `null` means what reduced motion
      // wants anyway - no contracting animation, `onDismissed` fires at once.
      resizeDuration: reduceMotion ? null : unfoldDuration,
      background: NotificationSwipeBackground(
        key: ValueKey('notification_swipe_read_${notification.id}'),
        icon: Icons.mark_email_read_rounded,
        label: 'Mark read',
        tint: scheme.primary,
        alignment: Alignment.centerLeft,
      ),
      secondaryBackground: NotificationSwipeBackground(
        key: ValueKey('notification_swipe_delete_${notification.id}'),
        icon: Icons.delete_outline_rounded,
        label: 'Delete',
        tint: scheme.error,
        alignment: Alignment.centerRight,
      ),
      confirmDismiss: (direction) async {
        if (direction == DismissDirection.endToStart) {
          // The delete: let the tile leave, and the screen commits it in
          // `onDismissed`. Committing here would take the tile out of the list
          // while the dismiss animation is still running it.
          return true;
        }
        // The read: do the work, then refuse the dismissal so the tile comes
        // back. Refusing is what keeps a read notification in the inbox.
        await onMarkRead();
        return false;
      },
      onDismissed: (_) => onDelete(),
      child: _card(context, reduceMotion: reduceMotion),
    );
  }

  Widget _card(BuildContext context, {required bool reduceMotion}) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final unread = !notification.isRead;

    return Container(
      decoration: BoxDecoration(
        // Unread sits on top of the page; read sinks into it.
        color: unread ? scheme.surfaceContainerLowest : scheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(16),
        boxShadow: unread
            ? [
                BoxShadow(
                  color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
                  blurRadius: 20,
                  offset: const Offset(0, 4),
                ),
              ]
            : null,
      ),
      clipBehavior: Clip.antiAlias,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Material(
            color: Colors.transparent,
            child: InkWell(
              key: ValueKey('notification_header_${notification.id}'),
              onTap: onToggle,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(16, 14, 12, 14),
                child: _header(context),
              ),
            ),
          ),
          AnimatedSize(
            duration: reduceMotion ? _instant : unfoldDuration,
            curve: Curves.easeOutCubic,
            alignment: Alignment.topCenter,
            child: expanded
                ? _body(context)
                // Full width so the unfold animates height only, rather than
                // sliding in from the left as the width changes too.
                : const SizedBox(width: double.infinity),
          ),
        ],
      ),
    );
  }

  Widget _header(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final visuals = NotificationKindVisuals.of(notification.kind);
    final unread = !notification.isRead;
    final reduceMotion = MediaQuery.disableAnimationsOf(context);

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _Enclosure(visuals: visuals),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Expanded(
                    child: Text(
                      notification.title,
                      key: ValueKey('notification_title_${notification.id}'),
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.titleMedium?.copyWith(
                        fontWeight: unread ? FontWeight.w700 : FontWeight.w600,
                        color: unread
                            ? scheme.onSurface
                            : scheme.onSurface.withValues(alpha: 0.72),
                      ),
                    ),
                  ),
                  if (unread) ...[
                    const SizedBox(width: 8),
                    Padding(
                      padding: const EdgeInsets.only(top: 6),
                      child: Container(
                        key: ValueKey(
                          'notification_unread_dot_${notification.id}',
                        ),
                        width: 8,
                        height: 8,
                        decoration: BoxDecoration(
                          color: scheme.primary,
                          shape: BoxShape.circle,
                        ),
                      ),
                    ),
                  ],
                ],
              ),
              const SizedBox(height: 5),
              Row(
                children: [
                  Flexible(
                    child: Text(
                      notification.kind.label.toUpperCase(),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: visuals.tint,
                        letterSpacing: 1.1,
                      ),
                    ),
                  ),
                  Text(
                    '  ·  ',
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: scheme.outlineVariant,
                    ),
                  ),
                  Text(
                    relativeMoment(notification.createdAt),
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: scheme.onSurfaceVariant,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ),
              // The preview is only for the folded state: leaving it in place
              // once the tile is open would print the body twice.
              if (!expanded) ...[
                const SizedBox(height: 7),
                Text(
                  notification.body,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(width: 6),
        AnimatedRotation(
          // Half a turn unfolds the chevron downwards.
          turns: expanded ? 0.5 : 0,
          duration: reduceMotion ? _instant : unfoldDuration,
          child: Icon(
            Icons.expand_more_rounded,
            key: ValueKey('notification_chevron_${notification.id}'),
            size: 22,
            color: scheme.onSurfaceVariant,
          ),
        ),
      ],
    );
  }

  /// The whole notification, unfolded.
  Widget _body(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final facts = _facts;

    return Padding(
      key: ValueKey('notification_full_body_${notification.id}'),
      padding: const EdgeInsets.fromLTRB(16, 0, 16, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Divider(
            height: 1,
            thickness: 1,
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
          const SizedBox(height: 14),
          Text(
            notification.body,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurface,
            ),
          ),
          if (facts.isNotEmpty) ...[
            const SizedBox(height: 14),
            Wrap(spacing: 8, runSpacing: 8, children: facts),
          ],
          const SizedBox(height: 14),
          Text(
            relativeDayAndTime(notification.createdAt),
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              fontWeight: FontWeight.w500,
            ),
          ),
          const SizedBox(height: 6),
          _actions(context),
        ],
      ),
    );
  }

  /// The payload the dispatcher attached, as chips.
  ///
  /// Every entry is shown, keys tidied into words rather than mapped by hand: an
  /// entry the app has never seen is exactly the kind of thing an associate
  /// opening a notification wants to read.
  List<Widget> get _facts => [
    for (final entry in notification.data.entries)
      if (entry.value case final value? when value.isNotEmpty)
        _Fact(label: entry.key, value: value),
  ];

  Widget _actions(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Row(
      children: [
        if (onOpen != null)
          TextButton.icon(
            key: ValueKey('notification_action_open_${notification.id}'),
            onPressed: onOpen,
            icon: const Icon(Icons.open_in_new_rounded, size: 16),
            label: const Text('Open'),
          ),
        if (!notification.isRead)
          TextButton.icon(
            key: ValueKey('notification_action_read_${notification.id}'),
            onPressed: () {
              onMarkRead();
            },
            icon: const Icon(Icons.mark_email_read_outlined, size: 16),
            label: const Text('Mark as read'),
          ),
        const Spacer(),
        TextButton.icon(
          key: ValueKey('notification_action_delete_${notification.id}'),
          onPressed: onDelete,
          style: TextButton.styleFrom(foregroundColor: scheme.error),
          icon: const Icon(Icons.delete_outline_rounded, size: 16),
          label: const Text('Delete'),
        ),
      ],
    );
  }
}

/// The rounded square a notification's mark sits in.
///
/// A squircle rather than a circle, per the shape language in
/// `.agents/brain/DESIGN.md`: icons are housed in soft enclosures that echo
/// fabric buttons, and the inbox has six of them stacked down one column.
class _Enclosure extends StatelessWidget {
  const _Enclosure({required this.visuals});

  final NotificationKindVisuals visuals;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 44,
      height: 44,
      decoration: BoxDecoration(
        color: visuals.tint.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(14),
      ),
      child: Icon(visuals.icon, size: 21, color: visuals.tint),
    );
  }
}

/// One entry from the notification's payload.
class _Fact extends StatelessWidget {
  const _Fact({required this.label, required this.value});

  final String label;
  final String value;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: scheme.surfaceContainer,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            '${_humanise(label).toUpperCase()}  ',
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              letterSpacing: 0.7,
            ),
          ),
          Text(
            value,
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurface,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
      ),
    );
  }

  /// `orderId` reads as `Order id`, `daysSinceVisit` as `Days since visit`.
  static String _humanise(String key) {
    final spaced = key.replaceAllMapped(
      RegExp('([a-z0-9])([A-Z])'),
      (match) => '${match[1]} ${match[2]}',
    );
    if (spaced.isEmpty) {
      return spaced;
    }
    return spaced[0].toUpperCase() + spaced.substring(1).toLowerCase();
  }
}
