import 'package:flutter/material.dart';

import '../lookbooks_controller.dart';

/// Horizontally scrollable capsule filter bar for lookbook ceremonial occasions.
class OccasionFilterChips extends StatelessWidget {
  const OccasionFilterChips({
    super.key,
    required this.selectedOccasion,
    required this.onOccasionSelected,
    this.occasions = lookbookOccasions,
  });

  final String selectedOccasion;
  final ValueChanged<String> onOccasionSelected;
  final List<String> occasions;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (final occ in occasions) ...[
            _OccasionChip(
              occasion: occ,
              isSelected: selectedOccasion.toLowerCase() == occ.toLowerCase(),
              onTap: () => onOccasionSelected(occ),
              scheme: scheme,
            ),
            const SizedBox(width: 8),
          ],
        ],
      ),
    );
  }
}

class _OccasionChip extends StatelessWidget {
  const _OccasionChip({
    required this.occasion,
    required this.isSelected,
    required this.onTap,
    required this.scheme,
  });

  final String occasion;
  final bool isSelected;
  final VoidCallback onTap;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Material(
      color: Colors.transparent,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(20),
        child: AnimatedContainer(
          duration: const Duration(milliseconds: 200),
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 7),
          decoration: BoxDecoration(
            color: isSelected ? scheme.primary : scheme.surfaceContainerHighest.withValues(alpha: 0.6),
            borderRadius: BorderRadius.circular(20),
            border: Border.all(
              color: isSelected ? scheme.primary : scheme.outlineVariant.withValues(alpha: 0.7),
              width: 1,
            ),
          ),
          child: Text(
            occasion,
            style: TextStyle(
              fontSize: 12,
              fontWeight: isSelected ? FontWeight.w600 : FontWeight.w500,
              color: isSelected ? scheme.onPrimary : scheme.onSurfaceVariant,
            ),
          ),
        ),
      ),
    );
  }
}
