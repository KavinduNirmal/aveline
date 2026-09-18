import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../core/auth/app_roles.dart';
import '../../core/auth/permissions.dart';
import '../../core/navigation/screen_config.dart';
import '../../core/providers/user_provider.dart';
import '../../features/auth/domain/auth_repository.dart';
import 'blossom.dart';

/// Side navigation drawer for Aveline app shells.
///
/// Filters the provided [screens] based on the current user's role and permissions,
/// displays user identity in the header, highlights the current route, and provides
/// a sign-out action.
///
/// Rows are fully rounded pill buttons, matching the stadium button shape the
/// app theme applies to its buttons. Only the active row carries a hairline
/// outline and the primary tint; every other row stays quiet until it is
/// touched. Rows are stacked with the tight rhythm in [navItemGap].
class AvelineDrawer extends StatelessWidget {
  /// Vertical gap between consecutive navigation rows.
  ///
  /// Tighter than the 8px `.agents/brain/DESIGN.md` reserves between grouped
  /// cards: with the outline reserved for the active row, the list should read
  /// as one stack rather than as a set of separate cards.
  static const double navItemGap = 4;

  /// Horizontal inset shared by the nav list, the dividers and the sign-out
  /// action so every row lines up on the same left edge.
  static const double _horizontalInset = 12;

  const AvelineDrawer({
    super.key,
    required this.screens,
    this.currentRoute,
    this.onNavigate,
    this.onSignOut,
  });

  /// The list of screen configurations to display (e.g. [staffScreens]).
  final List<ScreenConfig> screens;

  /// The currently active route path (e.g. '/catalog'). If `null`, will attempt
  /// to read from [GoRouterState.of(context).matchedLocation].
  final String? currentRoute;

  /// Optional navigation callback override. When `null`, navigates via [context.go].
  final ValueChanged<String>? onNavigate;

  /// Optional sign-out callback override. When `null`, calls [AuthRepository.signOut].
  final VoidCallback? onSignOut;

  void _handleNavigation(BuildContext context, String route) {
    Navigator.of(context).pop(); // Close drawer
    if (onNavigate != null) {
      onNavigate!(route);
    } else {
      try {
        context.go(route);
      } catch (_) {
        Navigator.of(context).pushNamed(route);
      }
    }
  }

  void _handleSignOut(BuildContext context) {
    Navigator.of(context).pop(); // Close drawer
    if (onSignOut != null) {
      onSignOut!();
    } else {
      try {
        context.read<AuthRepository>().signOut();
      } catch (_) {}
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
    final userRole = user?.userRole.isNotEmpty == true
        ? user!.userRole
        : (user?.organizationRole ?? '');

    // Determine active route
    String activeRoute = currentRoute ?? '/';
    if (currentRoute == null) {
      try {
        activeRoute = GoRouterState.of(context).matchedLocation;
      } catch (_) {
        activeRoute = '/';
      }
    }

    // Filter screens based on permissions
    final accessibleScreens = screens.where((screen) {
      if (screen.permission == null) return true;
      if (user == null) return false;
      return Permissions.anyGranted(
        [user.userRole, user.organizationRole],
        screen.permission!,
      );
    }).toList();

    // The same rule the account card reads, so a name cannot be one thing in the
    // panel and another on the page it opens. Only the words for an account with
    // no name at all are the panel's own.
    final displayName =
        user?.nameForDisplay(fallback: 'Staff Member') ?? 'Staff Member';
    final profileImageUrl = user?.profileImageUrl;

    return Drawer(
      backgroundColor: scheme.surfaceContainerLowest,
      child: SafeArea(
        child: Column(
          children: [
            // User Header
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 24, 20, 16),
              child: Row(
                children: [
                  if (profileImageUrl != null && profileImageUrl.isNotEmpty)
                    CircleAvatar(
                      radius: 26,
                      backgroundImage: NetworkImage(profileImageUrl),
                    )
                  else
                    CircleAvatar(
                      radius: 26,
                      backgroundColor: scheme.primary.withValues(alpha: 0.1),
                      child: Blossom(
                        size: 26,
                        color: scheme.primary,
                      ),
                    ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          displayName,
                          style: theme.textTheme.titleMedium?.copyWith(
                            fontWeight: FontWeight.w600,
                          ),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        if (userRole.isNotEmpty) ...[
                          const SizedBox(height: 4),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 8,
                              vertical: 3,
                            ),
                            decoration: BoxDecoration(
                              color: scheme.primary.withValues(alpha: 0.08),
                              borderRadius: BorderRadius.circular(999),
                            ),
                            child: Text(
                              // Named the same way the account card names it, so a
                              // role reads the same in the panel as on the page.
                              AppRoles.labelFor(userRole),
                              style: theme.textTheme.labelSmall?.copyWith(
                                color: scheme.primary,
                                fontWeight: FontWeight.w500,
                              ),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ],
              ),
            ),
            const Divider(height: 1, indent: 16, endIndent: 16),
            const SizedBox(height: 12),

            // Navigation items
            Expanded(
              child: ListView(
                padding: const EdgeInsets.symmetric(
                  horizontal: _horizontalInset,
                  vertical: 2,
                ),
                children: [
                  for (final screen in accessibleScreens)
                    Padding(
                      padding: const EdgeInsets.only(bottom: navItemGap),
                      child: Builder(builder: (context) {
                        final isSelected = activeRoute == screen.route;
                        final itemColor = isSelected
                            ? scheme.primary
                            : scheme.onSurface;
                        return _DrawerNavItem(
                          label: screen.label,
                          icon: isSelected ? screen.activeIcon : screen.icon,
                          selected: isSelected,
                          iconColor: isSelected
                              ? scheme.primary
                              : scheme.onSurfaceVariant,
                          labelColor: itemColor,
                          onTap: () => _handleNavigation(context, screen.route),
                        );
                      }),
                    ),
                ],
              ),
            ),

            // Sign out action at bottom
            const Divider(height: 1, indent: 16, endIndent: 16),
            Padding(
              padding: const EdgeInsets.fromLTRB(
                _horizontalInset,
                _horizontalInset,
                _horizontalInset,
                12,
              ),
              child: _DrawerNavItem(
                label: 'Sign Out',
                icon: Icons.logout_rounded,
                iconColor: scheme.error,
                labelColor: scheme.error,
                labelWeight: FontWeight.w500,
                onTap: () => _handleSignOut(context),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// A single fully rounded (pill) drawer row: icon and label inside one
/// [StadiumBorder].
///
/// The outline is drawn only for the selected row. Unselected rows keep a
/// transparent side of the same width, so their geometry — the ripple clip and
/// the row height — is identical either way.
///
/// Kept as its own widget rather than a [ListTile] because [ListTile] cannot
/// express a stadium shape at a compact height: its minimum tile height would
/// stretch the shared corner radius into an oval.
class _DrawerNavItem extends StatelessWidget {
  const _DrawerNavItem({
    required this.label,
    required this.icon,
    required this.iconColor,
    required this.labelColor,
    required this.onTap,
    this.selected = false,
    this.labelWeight,
  });

  final String label;
  final IconData icon;
  final Color iconColor;
  final Color labelColor;
  final VoidCallback onTap;

  /// Whether this row marks the active route.
  final bool selected;

  /// Overrides the label weight; defaults to a heavier weight when selected.
  final FontWeight? labelWeight;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final shape = StadiumBorder(
      side: BorderSide(
        // Invisible rather than absent, so an unselected row keeps the same
        // geometry as the selected one and nothing shifts when the route changes.
        color: selected
            ? scheme.primary.withValues(alpha: 0.28)
            : Colors.transparent,
      ),
    );

    return Semantics(
      button: true,
      selected: selected,
      label: label,
      child: Material(
        color: selected
            ? scheme.primary.withValues(alpha: 0.10)
            : Colors.transparent,
        shape: shape,
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          customBorder: shape,
          splashColor: scheme.primary.withValues(alpha: 0.08),
          highlightColor: scheme.primary.withValues(alpha: 0.04),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            child: Row(
              children: [
                Icon(icon, color: iconColor, size: 20),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    label,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: labelColor,
                      fontWeight: labelWeight ??
                          (selected ? FontWeight.w600 : FontWeight.w400),
                    ),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
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
