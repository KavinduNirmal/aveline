import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/providers/user_provider.dart';
import 'notification_badge.dart';
import 'search_overlay.dart';

/// Universal top navigation header for the Aveline application.
///
/// Features:
/// - Left: Menu icon to toggle the side navigation drawer.
/// - Right: Search button (opens full-screen [SearchOverlay]),
///          Notifications button (with [NotificationBadge] redirecting to `/notifications`),
///          and Profile avatar button (redirecting to `/profile`).
class AvelineHeader extends StatelessWidget implements PreferredSizeWidget {
  const AvelineHeader({
    super.key,
    this.onMenuPressed,
    this.onSearchPressed,
    this.onNotificationsPressed,
    this.onProfilePressed,
  });

  /// Optional override for the hamburger menu action.
  /// When `null`, defaults to opening the ancestor [Scaffold] drawer.
  final VoidCallback? onMenuPressed;

  /// Optional override for search action.
  /// When `null`, opens the global [SearchOverlay].
  final VoidCallback? onSearchPressed;

  /// Optional override for notifications navigation.
  /// When `null`, navigates to `/notifications`.
  final VoidCallback? onNotificationsPressed;

  /// Optional override for profile navigation.
  /// When `null`, navigates to `/profile`.
  final VoidCallback? onProfilePressed;

  @override
  Size get preferredSize => const Size.fromHeight(kToolbarHeight);

  void _handleMenu(BuildContext context) {
    if (onMenuPressed != null) {
      onMenuPressed!();
    } else {
      Scaffold.of(context).openDrawer();
    }
  }

  void _handleSearch(BuildContext context) {
    if (onSearchPressed != null) {
      onSearchPressed!();
    } else {
      SearchOverlay.show(context);
    }
  }

  void _handleNotifications(BuildContext context) {
    if (onNotificationsPressed != null) {
      onNotificationsPressed!();
    } else {
      try {
        GoRouter.of(context).push('/notifications');
      } catch (_) {
        Navigator.of(context).pushNamed('/notifications');
      }
    }
  }

  void _handleProfile(BuildContext context) {
    if (onProfilePressed != null) {
      onProfilePressed!();
    } else {
      try {
        GoRouter.of(context).push('/profile');
      } catch (_) {
        Navigator.of(context).pushNamed('/profile');
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    UserProvider? userProvider;
    try {
      userProvider = context.watch<UserProvider>();
    } catch (_) {
      userProvider = null;
    }

    final user = userProvider?.user;
    final profileImageUrl = user?.profileImageUrl;

    return AppBar(
      backgroundColor: scheme.surface,
      surfaceTintColor: Colors.transparent,
      elevation: 0,
      scrolledUnderElevation: 0,
      leading: IconButton(
        key: const Key('aveline_header_menu_button'),
        icon: const Icon(Icons.menu_rounded),
        color: scheme.onSurfaceVariant,
        tooltip: 'Navigation menu',
        onPressed: () => _handleMenu(context),
      ),
      actions: [
        IconButton(
          key: const Key('aveline_header_search_button'),
          icon: const Icon(Icons.search_rounded),
          color: scheme.onSurfaceVariant,
          tooltip: 'Search',
          onPressed: () => _handleSearch(context),
        ),
        IconButton(
          key: const Key('aveline_header_notifications_button'),
          icon: const NotificationBadge(
            child: Icon(Icons.notifications_outlined),
          ),
          color: scheme.onSurfaceVariant,
          tooltip: 'Notifications',
          onPressed: () => _handleNotifications(context),
        ),
        Padding(
          padding: const EdgeInsets.only(right: 12, left: 4),
          child: InkWell(
            key: const Key('aveline_header_profile_button'),
            borderRadius: BorderRadius.circular(18),
            onTap: () => _handleProfile(context),
            child: Padding(
              padding: const EdgeInsets.all(4),
              child: CircleAvatar(
                radius: 14,
                backgroundColor: scheme.primary.withValues(alpha: 0.12),
                backgroundImage:
                    profileImageUrl != null && profileImageUrl.isNotEmpty
                        ? NetworkImage(profileImageUrl)
                        : null,
                child: profileImageUrl == null || profileImageUrl.isEmpty
                    ? Icon(
                        Icons.person_outline_rounded,
                        size: 18,
                        color: scheme.primary,
                      )
                    : null,
              ),
            ),
          ),
        ),
      ],
    );
  }
}
