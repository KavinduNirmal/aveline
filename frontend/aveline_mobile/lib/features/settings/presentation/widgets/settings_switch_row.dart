import 'package:flutter/material.dart';

/// One switchable setting: what it is, what it is set to, and a line explaining
/// what flipping it means.
///
/// The switch reads [value] rather than holding a state of its own, so a save
/// that the server refuses puts the switch back with nothing to undo: the screen
/// rebuilds from the record the app holds. [onChanged] is `null` while that save
/// is in flight, which is what stops a second flip racing the first.
class SettingsSwitchRow extends StatelessWidget {
  const SettingsSwitchRow({
    super.key,
    required this.icon,
    required this.label,
    required this.value,
    required this.onChanged,
    this.description,
  });

  /// The line's own mark, drawn at the same size as the side panel's rows.
  final IconData icon;

  /// What the setting is called, e.g. `Push notifications`.
  final String label;

  /// Whether it is currently on.
  final bool value;

  /// Reports the value the associate asked for, or `null` to refuse the control
  /// while a change is already on its way.
  final ValueChanged<bool>? onChanged;

  /// The line under the label, for what the switch means in practice.
  final String? description;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final change = onChanged;
    final detail = description;

    return MergeSemantics(
      child: Semantics(
        toggled: value,
        child: Material(
          color: Colors.transparent,
          child: InkWell(
            // The switch claims a tap that lands on it, so this only answers the
            // taps beside it: a wide target without two changes from one tap.
            onTap: change == null ? null : () => change(!value),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(16, 12, 12, 12),
              child: Row(
                children: [
                  Icon(icon, size: 20, color: scheme.onSurfaceVariant),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          label,
                          style: theme.textTheme.bodyLarge,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                        if (detail != null) ...[
                          const SizedBox(height: 2),
                          Text(
                            detail,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: scheme.onSurfaceVariant,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  Switch(
                    value: value,
                    onChanged: change,
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}
