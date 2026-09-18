import 'package:flutter/material.dart';

/// One line of settings: what the line is about, what it is set to, and where it
/// leads.
///
/// A row that leads somewhere carries a chevron; a row that only reports a value
/// does not. The chevron is the promise that a tap does something, so drawing one
/// on a row that does nothing would be the row lying about itself.
class SettingsRow extends StatelessWidget {
  const SettingsRow({
    super.key,
    required this.icon,
    required this.label,
    this.value,
    this.onTap,
    this.destructive = false,
    this.trailing,
  });

  /// The line's own mark, drawn at the same size as the side panel's rows.
  final IconData icon;

  /// What the line is about, e.g. `Preferred contact`.
  final String label;

  /// What it is currently set to, drawn quietly to the right of the label.
  final String? value;

  /// What a tap does. When `null` the row is inert and reads as a statement.
  final VoidCallback? onTap;

  /// Whether the row performs something destructive, such as signing out.
  final bool destructive;

  /// Drawn instead of the chevron, for a row that ends in a control.
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final foreground = destructive ? scheme.error : scheme.onSurface;
    final current = value;

    final line = Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
      child: Row(
        children: [
          Icon(
            icon,
            size: 20,
            color: destructive ? scheme.error : scheme.onSurfaceVariant,
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Text(
              label,
              style: theme.textTheme.bodyLarge?.copyWith(color: foreground),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
            ),
          ),
          if (current != null) ...[
            const SizedBox(width: 12),
            Flexible(
              child: Text(
                current,
                textAlign: TextAlign.right,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
              ),
            ),
          ],
          if (trailing case final control?) ...[
            const SizedBox(width: 8),
            control,
          ] else if (onTap != null) ...[
            const SizedBox(width: 4),
            Icon(
              Icons.chevron_right_rounded,
              size: 20,
              color: scheme.onSurfaceVariant,
            ),
          ],
        ],
      ),
    );

    return MergeSemantics(
      child: Semantics(
        button: onTap != null,
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            onTap: onTap,
            child: line,
          ),
        ),
      ),
    );
  }
}
