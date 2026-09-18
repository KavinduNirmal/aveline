import 'package:flutter/material.dart';

import '../../domain/customer_level.dart';

/// The level ramp: VIP wears the brand wine, and the levels run dark to light so
/// the stronger grade reads as the heavier mark.
///
/// This mirrors the ramp Home's client tiles use — both are the brand's own value
/// ramp, and the customers slice keeps its own copy rather than importing another
/// feature's presentation layer (see `features/customers/README.md`).
Color customerLevelColor(CustomerLevel level, ColorScheme scheme) =>
    switch (level) {
      CustomerLevel.vip => scheme.primary,
      CustomerLevel.level3 => const Color(0xFF3B3030),
      CustomerLevel.level2 => const Color(0xFF6E6363),
      CustomerLevel.level1 => const Color(0xFF7A6F6F),
    };

/// The grade a client holds, as a compact badge for the end of their row.
///
/// Tinted rather than filled, matching the value chip on Home: on a list of
/// two hundred clients, a solid block of colour on every row would shout, and the
/// ramp's tone then reads as the weight of the grade instead of as an alarm.
class CustomerLevelBadge extends StatelessWidget {
  const CustomerLevelBadge({super.key, required this.level});

  final CustomerLevel level;

  /// The badge's height, so the row can centre it against the name and the id
  /// rather than making it a third line.
  static const double height = 22;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colour = customerLevelColor(level, theme.colorScheme);

    return Semantics(
      label: 'Level ${level.label}',
      child: Container(
        height: height,
        padding: const EdgeInsets.symmetric(horizontal: 9),
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colour.withValues(alpha: 0.10),
          borderRadius: BorderRadius.circular(999),
        ),
        child: Text(
          level.label,
          style: theme.textTheme.labelSmall?.copyWith(
            color: colour,
            fontWeight: FontWeight.w700,
            letterSpacing: 0.6,
          ),
        ),
      ),
    );
  }
}
