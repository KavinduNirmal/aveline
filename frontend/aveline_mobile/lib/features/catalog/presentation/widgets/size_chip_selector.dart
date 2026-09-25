import 'package:flutter/material.dart';

const List<String> _availableSizes = [
  '34',
  '36',
  '38',
  '40',
  '42',
  '44',
  'Free Size',
  'Custom',
];

/// Multi-select size chip selector for luxury boutique garments.
class SizeChipSelector extends StatelessWidget {
  const SizeChipSelector({
    super.key,
    required this.selectedSizes,
    required this.onToggleSize,
  });

  final List<String> selectedSizes;
  final void Function(String size) onToggleSize;

  @override
  Widget build(BuildContext context) {
    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        for (final size in _availableSizes) ...[
          _SizeChip(
            size: size,
            isSelected: selectedSizes.contains(size),
            onTap: () => onToggleSize(size),
          ),
        ],
      ],
    );
  }
}

class _SizeChip extends StatelessWidget {
  const _SizeChip({
    required this.size,
    required this.isSelected,
    required this.onTap,
  });

  final String size;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(10),
      child: AnimatedContainer(
        duration: const Duration(milliseconds: 150),
        padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 8),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(10),
          color: isSelected ? scheme.primary : scheme.surfaceContainerLow,
          border: Border.all(
            color: isSelected ? scheme.primary : scheme.outlineVariant,
          ),
        ),
        child: Text(
          size,
          style: Theme.of(context).textTheme.labelMedium?.copyWith(
                fontWeight: isSelected ? FontWeight.w600 : FontWeight.normal,
                color: isSelected ? scheme.onPrimary : scheme.onSurface,
              ),
        ),
      ),
    );
  }
}
