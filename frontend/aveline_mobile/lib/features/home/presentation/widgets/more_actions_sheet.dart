import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/app_toast.dart';

/// The quick-action overflow: destinations that do not earn a permanent slot in
/// the row under the greeting.
Future<void> showMoreActionsSheet(BuildContext context) {
  return showModalBottomSheet<void>(
    context: context,
    backgroundColor: Colors.transparent,
    builder: (context) => const _MoreActionsSheet(),
  );
}

/// One row in the overflow sheet: a route when the slice exists, honest copy
/// when it does not.
class _MoreAction {
  const _MoreAction(
    this.label,
    this.icon, {
    this.route,
    this.unavailableMessage,
  });

  final String label;
  final IconData icon;
  final String? route;
  final String? unavailableMessage;
}

class _MoreActionsSheet extends StatelessWidget {
  const _MoreActionsSheet();

  static const List<_MoreAction> _actions = [
    _MoreAction(
      'Notifications',
      Icons.notifications_none_rounded,
      route: AppRoutes.notifications,
    ),
    _MoreAction(
      'Your profile',
      Icons.person_outline_rounded,
      route: AppRoutes.profile,
    ),
    _MoreAction(
      'New order',
      Icons.receipt_long_outlined,
      route: AppRoutes.createOrder,
    ),
    _MoreAction(
      'Orders',
      Icons.receipt_outlined,
      route: AppRoutes.orders,
    ),
    _MoreAction(
      'Settings',
      Icons.tune_rounded,
      route: AppRoutes.settings,
    ),
  ];

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return SafeArea(
      top: false,
      child: Container(
        margin: const EdgeInsets.all(12),
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 16),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: BorderRadius.circular(24),
          boxShadow: [
            BoxShadow(
              color: const Color(0xFF8B2E42).withValues(alpha: 0.12),
              blurRadius: 28,
              offset: const Offset(0, 10),
            ),
          ],
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Center(
              child: Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: scheme.outlineVariant,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 18),
            Text('More actions', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 12),
            for (final action in _actions) _MoreActionTile(action: action),
          ],
        ),
      ),
    );
  }
}

class _MoreActionTile extends StatelessWidget {
  const _MoreActionTile({required this.action});

  final _MoreAction action;

  void _handle(BuildContext context) {
    final router = GoRouter.maybeOf(context);
    final route = action.route;

    if (route != null && router != null) {
      Navigator.of(context).pop();
      router.push(route);
      return;
    }

    AppToast.show(context, action.unavailableMessage ?? 'Not on mobile yet.');
    Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      type: MaterialType.transparency,
      child: InkWell(
        onTap: () => _handle(context),
        borderRadius: BorderRadius.circular(16),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 10),
          child: Row(
            children: [
              Container(
                width: 42,
                height: 42,
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLow,
                  shape: BoxShape.circle,
                ),
                child: Icon(action.icon, size: 20, color: scheme.primary),
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Text(action.label, style: theme.textTheme.titleMedium),
              ),
              Icon(
                Icons.chevron_right_rounded,
                size: 20,
                color: scheme.outline,
              ),
            ],
          ),
        ),
      ),
    );
  }
}
