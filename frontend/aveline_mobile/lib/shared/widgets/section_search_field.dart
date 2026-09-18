import 'package:flutter/material.dart';

/// The search field a section owns, scoped to that section's own results.
///
/// It sits on the screen rather than in the header, and holds only its own
/// query: the app-wide search in the header searches every section, while this
/// field narrows the one screen it stands on. Shared by the dock tabs that
/// search their own content (Catalog, Customers) so their fields stay identical.
class SectionSearchField extends StatelessWidget {
  const SectionSearchField({
    super.key,
    required this.controller,
    required this.hintText,
    required this.hasQuery,
    required this.onChanged,
    required this.onClear,
    required this.fieldKey,
    required this.clearKey,
  });

  final TextEditingController controller;

  /// What the empty field says, e.g. `Search this catalog...`.
  final String hintText;

  /// Whether a query is in force, which is what reveals the clear affordance.
  final bool hasQuery;

  final ValueChanged<String> onChanged;
  final VoidCallback onClear;

  /// Rides the `TextField` itself, so a test can drive and read the field.
  final Key fieldKey;

  /// Rides the clear affordance, which only exists while [hasQuery].
  final Key clearKey;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    // The pill fill and border come from `AppTheme.inputDecorationTheme`; only
    // the hint and the two icons are added here.
    return TextField(
      key: fieldKey,
      controller: controller,
      onChanged: onChanged,
      textInputAction: TextInputAction.search,
      style: theme.textTheme.bodyMedium?.copyWith(color: scheme.onSurface),
      decoration: InputDecoration(
        hintText: hintText,
        hintStyle: theme.textTheme.bodyMedium?.copyWith(
          color: scheme.onSurfaceVariant.withValues(alpha: 0.7),
        ),
        prefixIcon: Icon(
          Icons.search_rounded,
          size: 20,
          color: scheme.onSurfaceVariant,
        ),
        suffixIcon: hasQuery
            ? IconButton(
                key: clearKey,
                icon: const Icon(Icons.clear_rounded, size: 18),
                color: scheme.onSurfaceVariant,
                tooltip: 'Clear search',
                onPressed: onClear,
              )
            : null,
      ),
    );
  }
}
