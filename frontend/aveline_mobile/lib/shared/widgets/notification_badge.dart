import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/notifications/notification_provider.dart';
import 'count_badge.dart';

/// Wraps an icon or widget with a notification badge.
///
/// The badge wears the inbox's unread count when the inbox has reported one, so
/// reading a notification on the tab clears the mark that sent the associate
/// there. Until then it falls back to a plain dot on the last notification that
/// arrived, because the app knows something came in but not how many are waiting.
///
/// [forceShow] pins the state for previews and tests: `true` shows the dot,
/// `false` hides the badge, and `null` lets the provider decide.
class NotificationBadge extends StatelessWidget {
  const NotificationBadge({
    super.key,
    required this.child,
    this.forceShow,
  });

  /// The widget (typically an [Icon]) to badge.
  final Widget child;

  /// Optional override: when specified, forces the badge to show or hide.
  final bool? forceShow;

  @override
  Widget build(BuildContext context) {
    NotificationProvider? provider;
    try {
      provider = context.watch<NotificationProvider>();
    } catch (_) {
      provider = null;
    }

    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final countsAreKnown = provider?.hasUnreadCount ?? false;
    final unreadCount = provider?.unreadCount ?? 0;
    final hasArrived = provider?.latest != null;

    // A count replaces the dot; it does not sit beside one. A dot and a number
    // for the same thing would read as two different notifications.
    final showCount = forceShow == null && countsAreKnown && unreadCount > 0;
    final showDot =
        forceShow == true ||
        (forceShow == null && !countsAreKnown && hasArrived);

    return Stack(
      clipBehavior: Clip.none,
      children: [
        child,
        if (showCount)
          Positioned(
            top: -5,
            right: -7,
            child: CountBadge(
              key: const Key('notification_badge_count'),
              count: unreadCount,
              ringColor: scheme.surface,
              semanticLabel: unreadCount == 1
                  ? '1 unread notification'
                  : '$unreadCount unread notifications',
            ),
          ),
        if (showDot)
          Positioned(
            top: -1,
            right: -1,
            child: _Dot(key: const Key('notification_badge_dot'), scheme: scheme),
          ),
      ],
    );
  }
}

/// The dot worn before the inbox has been counted.
class _Dot extends StatelessWidget {
  const _Dot({super.key, required this.scheme});

  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Container(
      width: 9,
      height: 9,
      decoration: BoxDecoration(
        color: scheme.primary,
        shape: BoxShape.circle,
        // Ringed in the surface colour so the mark stays legible over whatever
        // the icon is sitting on.
        border: Border.all(color: scheme.surface, width: 1.5),
      ),
    );
  }
}
