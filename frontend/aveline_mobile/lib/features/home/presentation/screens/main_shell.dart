import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/auth/app_roles.dart';
import '../../../../core/providers/user_provider.dart';
import 'home_screen.dart';
import 'staff_app_shell.dart';

/// Post-auth root shell that delegates to the role-appropriate app loader:
/// - [StaffAppShell] for staff and operational roles
/// - Owner UI shell for owner roles (falls back to [StaffAppShell] in this phase)
class MainShell extends StatelessWidget {
  const MainShell({
    super.key,
    this.child,
    this.showHeader = true,
  });

  /// Optional child screen to render inside the shell.
  /// If `null`, defaults to [HomeScreen].
  final Widget? child;

  /// Whether the universal header is displayed.
  final bool showHeader;

  @override
  Widget build(BuildContext context) {
    final screen = child ?? const HomeScreen();

    UserProvider? userProvider;
    try {
      userProvider = context.watch<UserProvider>();
    } catch (_) {
      userProvider = null;
    }

    final user = userProvider?.user;
    final role = user?.userRole.isNotEmpty == true
        ? user!.userRole
        : (user?.organizationRole ?? '');

    // Only the bottom of the router's stack treats a back press as "leave the
    // app", so deeper screens keep returning to the previous screen. GoRouter is
    // resolved through `maybeOf` because the shell is also driven directly by
    // widget tests that mount it without a router.
    final isAtRoot = !(GoRouter.maybeOf(context)?.canPop() ?? false);

    final isOwner = AppRoles.isOwnerRole(role);
    if (isOwner) {
      // Future: OwnerAppShell(showHeader: showHeader, child: screen);
      return StaffAppShell(
        showHeader: showHeader,
        isAtRoot: isAtRoot,
        child: screen,
      );
    }

    return StaffAppShell(
      showHeader: showHeader,
      isAtRoot: isAtRoot,
      child: screen,
    );
  }
}
