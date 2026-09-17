import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../core/notifications/notification_provider.dart';

/// Wraps an icon or widget with a notification badge dot when unread/unhandled
/// notifications exist in [NotificationProvider], or when [forceShow] is `true`.
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

    final hasNotification = forceShow ?? (provider?.latest != null);
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Stack(
      clipBehavior: Clip.none,
      children: [
        child,
        if (hasNotification)
          Positioned(
            top: -1,
            right: -1,
            child: Container(
              key: const Key('notification_badge_dot'),
              width: 9,
              height: 9,
              decoration: BoxDecoration(
                color: scheme.primary,
                shape: BoxShape.circle,
                border: Border.all(
                  color: scheme.surface,
                  width: 1.5,
                ),
              ),
            ),
          ),
      ],
    );
  }
}
