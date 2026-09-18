import 'package:flutter/material.dart';

import '../../../auth/domain/contact_preference.dart';

/// Asks how the associate would rather be reached.
///
/// Returns the preference they chose, or `null` when the sheet was dismissed -
/// so a dismissal leaves the stored preference alone rather than being read as a
/// choice of `None`.
Future<ContactPreference?> showContactPreferenceSheet(
  BuildContext context, {
  required ContactPreference current,
}) {
  final scheme = Theme.of(context).colorScheme;

  return showModalBottomSheet<ContactPreference>(
    context: context,
    backgroundColor: scheme.surfaceContainerLowest,
    shape: const RoundedRectangleBorder(
      borderRadius: BorderRadius.vertical(top: Radius.circular(24)),
    ),
    builder: (context) => _ContactPreferenceSheet(current: current),
  );
}

class _ContactPreferenceSheet extends StatelessWidget {
  const _ContactPreferenceSheet({required this.current});

  final ContactPreference current;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return SafeArea(
      key: const Key('contact_preference_sheet'),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 20, 20, 4),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('Preferred contact', style: theme.textTheme.headlineSmall),
                const SizedBox(height: 6),
                Text(
                  'Where the boutique should reach you first.',
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
          ),
          // The list scrolls rather than the sheet growing past the screen: a
          // choice has to stay reachable at a large text scale, and hiding one
          // under the edge would defeat the point of asking.
          Flexible(
            child: SingleChildScrollView(
              padding: const EdgeInsets.symmetric(horizontal: 20),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (final preference in ContactPreference.values)
                    _PreferenceOption(
                      preference: preference,
                      selected: preference == current,
                      onTap: () => Navigator.of(context).pop(preference),
                    ),
                ],
              ),
            ),
          ),
          // Outside the scroll view, so leaving the picker never means scrolling
          // to find the way out.
          Padding(
            padding: const EdgeInsets.fromLTRB(20, 4, 20, 12),
            child: Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                key: const Key('contact_preference_cancel'),
                onPressed: () => Navigator.of(context).pop(),
                child: const Text('Cancel'),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// One choice in the picker.
///
/// Drawn as a row with a tick rather than a round radio: the tick reads as "this
/// is the one" without a second control sitting inside a sheet of plain rows.
class _PreferenceOption extends StatelessWidget {
  const _PreferenceOption({
    required this.preference,
    required this.selected,
    required this.onTap,
  });

  final ContactPreference preference;
  final bool selected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      color: Colors.transparent,
      child: InkWell(
        key: Key('contact_preference_option_${preference.name}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 12),
          child: Row(
            children: [
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      preference.label,
                      style: theme.textTheme.bodyLarge?.copyWith(
                        color: selected ? scheme.primary : scheme.onSurface,
                        fontWeight: selected ? FontWeight.w600 : FontWeight.w400,
                      ),
                    ),
                    const SizedBox(height: 2),
                    Text(
                      preference.description,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
              if (selected)
                Icon(Icons.check_rounded, size: 20, color: scheme.primary),
            ],
          ),
        ),
      ),
    );
  }
}
