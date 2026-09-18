import 'package:flutter/material.dart';

import '../../../../shared/widgets/avatar_tints.dart';
import '../../domain/customer.dart';
import '../../domain/customer_level.dart';

/// A client's initials in a tinted circle, ringed in wine for VIPs.
///
/// The tint comes from [avatarTintFor] so the circle is the same colour here as
/// it is in the message inbox.
class CustomerAvatar extends StatelessWidget {
  const CustomerAvatar({super.key, required this.customer, this.size = 44});

  final Customer customer;
  final double size;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final isVip = customer.level == CustomerLevel.vip;

    return Semantics(
      // The circle is the client's identity, not their name, which the row
      // prints beside it; the label keeps a screen reader from reading the
      // initials as a word.
      label: 'Profile',
      excludeSemantics: true,
      child: Container(
        width: size,
        height: size,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: avatarTintFor(customer.displayName),
          shape: BoxShape.circle,
          border: isVip
              ? Border.all(color: scheme.primary, width: 1.5)
              : Border.all(color: scheme.outlineVariant.withValues(alpha: 0.5)),
        ),
        child: Text(
          customer.initials,
          key: ValueKey('customer_initials_${customer.id}'),
          style: theme.textTheme.titleSmall?.copyWith(
            color: const Color(0xFF3B3030),
            fontWeight: FontWeight.w600,
            letterSpacing: 0.2,
          ),
        ),
      ),
    );
  }
}
