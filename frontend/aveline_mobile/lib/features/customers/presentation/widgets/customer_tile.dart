import 'package:flutter/material.dart';

import '../../domain/customer.dart';
import 'customer_avatar.dart';
import 'customer_level_badge.dart';

/// One client in the book, laid out like a phone's contact list: the circle, the
/// name, the id the shop keys them by, and the grade they hold.
///
/// The row is a fixed height because the alphabet index computes where each
/// letter starts rather than measuring it; see `CustomerSectionOffsets`.
class CustomerTile extends StatelessWidget {
  const CustomerTile({super.key, required this.customer, this.onTap});

  final Customer customer;

  /// `null` leaves the row inert. The book is read-only until a client's profile
  /// screen exists, and a tap does not do nothing — the screen answers it.
  final VoidCallback? onTap;

  /// The row's height, which the index's offset arithmetic depends on.
  static const double height = 72;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return SizedBox(
      height: height,
      child: Semantics(
        button: onTap != null,
        label: _semanticLabel,
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            onTap: onTap,
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Row(
                children: [
                  CustomerAvatar(customer: customer),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      mainAxisAlignment: MainAxisAlignment.center,
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          customer.displayName,
                          key: ValueKey('customer_name_${customer.id}'),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.titleMedium?.copyWith(
                            color: scheme.onSurface,
                            fontWeight: FontWeight.w600,
                          ),
                        ),
                        const SizedBox(height: 3),
                        Text(
                          customer.idLabel,
                          key: ValueKey('customer_id_${customer.id}'),
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: theme.textTheme.labelSmall?.copyWith(
                            color: scheme.onSurfaceVariant,
                            letterSpacing: 0.4,
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 12),
                  CustomerLevelBadge(
                    key: ValueKey('customer_level_badge_${customer.id}'),
                    level: customer.level,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// What a screen reader hears: the name, the grade, and whether the name is
  /// the nickname the floor uses.
  String get _semanticLabel {
    final nickname = customer.isKnownByNickname ? ', nickname' : '';
    return '${customer.displayName}$nickname, level ${customer.level.label}';
  }
}
