import 'package:flutter/widgets.dart';
import 'package:provider/provider.dart';

import '../providers/user_provider.dart';
import 'permissions.dart';

/// Conditionally renders [child] if the current authenticated user holds
/// [permission] via their `userRole` or `organizationRole`.
///
/// If [permission] is `null`, [child] is always rendered.
/// If the permission is not granted, [fallback] is rendered instead (defaults
/// to [SizedBox.shrink]).
class PermissionGuard extends StatelessWidget {
  const PermissionGuard({
    super.key,
    required this.permission,
    required this.child,
    this.fallback = const SizedBox.shrink(),
  });

  /// The permission required. If `null`, [child] is always shown.
  final String? permission;

  /// The widget to render when authorized.
  final Widget child;

  /// The widget to render when unauthorized. Defaults to empty box.
  final Widget fallback;

  @override
  Widget build(BuildContext context) {
    final requiredPermission = permission;
    if (requiredPermission == null) {
      return child;
    }

    UserProvider? userProvider;
    try {
      userProvider = context.watch<UserProvider>();
    } catch (_) {
      userProvider = null;
    }

    final user = userProvider?.user;
    if (user == null) {
      return fallback;
    }

    final hasPermission = Permissions.anyGranted(
      [user.userRole, user.organizationRole],
      requiredPermission,
    );

    return hasPermission ? child : fallback;
  }
}
