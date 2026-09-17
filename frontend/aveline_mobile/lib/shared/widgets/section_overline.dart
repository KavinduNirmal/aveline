import 'package:flutter/material.dart';

/// The small uppercase label that opens a Home section.
///
/// `DESIGN.md` asks for `label-md` with uppercase styling on category headers
/// and overlines. Keeping it as one widget stops the Home sections from
/// drifting apart as more are added.
class SectionOverline extends StatelessWidget {
  const SectionOverline(this.label, {super.key});

  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Text(
      label.toUpperCase(),
      style: theme.textTheme.labelMedium?.copyWith(
        color: theme.colorScheme.onSurfaceVariant,
        letterSpacing: 1.4,
      ),
    );
  }
}
