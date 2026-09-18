import 'package:flutter/material.dart';

import '../../../../shared/widgets/section_overline.dart';

/// One named group of settings, drawn as a single card.
///
/// `DESIGN.md` groups related items into stacked cards with a tight gap between
/// them, and reserves the serif for emotive headings: a settings page is a page
/// of dockets, so its groups are announced by an overline rather than a title.
class SettingsSection extends StatelessWidget {
  const SettingsSection({
    super.key,
    required this.title,
    required this.children,
    this.note,
  });

  /// The overline that names the group, e.g. `Account`.
  final String title;

  /// The rows inside the card, in the order they are read.
  final List<Widget> children;

  /// A closing line under the rows, for what the rows cannot say themselves:
  /// where a value comes from, or what is not built yet.
  final String? note;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final text = note;

    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(left: 4, bottom: 8),
            child: SectionOverline(title),
          ),
          Container(
            clipBehavior: Clip.antiAlias,
            decoration: BoxDecoration(
              color: scheme.surfaceContainerLowest,
              borderRadius: BorderRadius.circular(16),
              boxShadow: [
                BoxShadow(
                  color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
                  blurRadius: 20,
                  offset: const Offset(0, 4),
                ),
              ],
            ),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                // The hairline belongs to the group rather than to any one row,
                // so the rows stay unaware of what they sit next to. Clipped to
                // the card's corners with it, which is why they read as one
                // collection rather than as a stack.
                for (var index = 0; index < children.length; index++) ...[
                  if (index > 0)
                    const Divider(height: 1, indent: 16, endIndent: 16),
                  children[index],
                ],
                if (text != null)
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
                    child: Text(
                      text,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
