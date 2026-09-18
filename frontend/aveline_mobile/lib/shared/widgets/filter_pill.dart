import 'package:flutter/material.dart';

/// A selectable pill for a filter option, a shop tag, or a client level.
///
/// The selected state is carried by the primary tint plus its border, so a
/// chosen pill still reads as the same shape as its neighbours rather than
/// jumping to a filled button. Shared by the tag row on the Catalog, the groups
/// on its filter screen and the level row on Customers, so the three cannot
/// drift apart.
class FilterPill extends StatelessWidget {
  const FilterPill({
    super.key,
    required this.label,
    required this.selected,
    this.onTap,
  });

  final String label;
  final bool selected;

  /// `null` renders the pill inert, for a future read-only state.
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final shape = StadiumBorder(
      side: BorderSide(
        color: selected
            ? scheme.primary.withValues(alpha: 0.55)
            : scheme.outlineVariant,
      ),
    );

    return Semantics(
      button: true,
      selected: selected,
      label: label,
      // The transparent Material keeps the ink splash working wherever the pill
      // is mounted, without an ambient Material from the caller.
      child: Material(
        color: selected
            ? scheme.primary.withValues(alpha: 0.10)
            : scheme.surfaceContainerLowest,
        shape: shape,
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          customBorder: shape,
          splashColor: scheme.primary.withValues(alpha: 0.08),
          highlightColor: scheme.primary.withValues(alpha: 0.04),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
            child: Text(
              label,
              style: theme.textTheme.labelLarge?.copyWith(
                color: selected ? scheme.primary : scheme.onSurfaceVariant,
                fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
              ),
            ),
          ),
        ),
      ),
    );
  }
}
