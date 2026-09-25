import 'package:flutter/material.dart';

const List<_PaletteColor> _presetColors = [
  _PaletteColor('Wine', '#8B2E42', Color(0xFF8B2E42)),
  _PaletteColor('Emerald', '#2F6B52', Color(0xFF2F6B52)),
  _PaletteColor('Midnight', '#2B2F43', Color(0xFF2B2F43)),
  _PaletteColor('Champagne', '#E4D2AE', Color(0xFFE4D2AE)),
  _PaletteColor('Rose Quartz', '#E0A9B4', Color(0xFFE0A9B4)),
  _PaletteColor('Saffron', '#D08A2C', Color(0xFFD08A2C)),
  _PaletteColor('Ivory', '#F3ECE3', Color(0xFFF3ECE3)),
  _PaletteColor('Lilac', '#B49CC9', Color(0xFFB49CC9)),
  _PaletteColor('Gold', '#D4AF37', Color(0xFFD4AF37)),
  _PaletteColor('Ruby Red', '#9B111E', Color(0xFF9B111E)),
  _PaletteColor('Black', '#000000', Color(0xFF000000)),
];

class _PaletteColor {
  const _PaletteColor(this.name, this.hex, this.color);
  final String name;
  final String hex;
  final Color color;
}

/// A luxury boutique color swatch selector.
class ColorSwatchPicker extends StatelessWidget {
  const ColorSwatchPicker({
    super.key,
    required this.selectedColor,
    this.selectedHex,
    required this.onColorSelected,
  });

  final String selectedColor;
  final String? selectedHex;
  final void Function(String name, String hex) onColorSelected;

  @override
  Widget build(BuildContext context) {
    return SingleChildScrollView(
      scrollDirection: Axis.horizontal,
      child: Row(
        children: [
          for (final swatch in _presetColors) ...[
            _SwatchItem(
              swatch: swatch,
              isSelected: selectedColor.trim().toLowerCase() == swatch.name.toLowerCase() ||
                  (selectedHex != null && selectedHex!.toLowerCase() == swatch.hex.toLowerCase()),
              onTap: () => onColorSelected(swatch.name, swatch.hex),
            ),
            const SizedBox(width: 10),
          ],
        ],
      ),
    );
  }
}

class _SwatchItem extends StatelessWidget {
  const _SwatchItem({
    required this.swatch,
    required this.isSelected,
    required this.onTap,
  });

  final _PaletteColor swatch;
  final bool isSelected;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return InkWell(
      onTap: onTap,
      borderRadius: BorderRadius.circular(20),
      child: Container(
        padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(20),
          color: isSelected ? scheme.primary.withValues(alpha: 0.12) : scheme.surfaceContainerLow,
          border: Border.all(
            color: isSelected ? scheme.primary : scheme.outlineVariant,
            width: isSelected ? 1.5 : 1,
          ),
        ),
        child: Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            Container(
              width: 16,
              height: 16,
              decoration: BoxDecoration(
                color: swatch.color,
                shape: BoxShape.circle,
                border: Border.all(color: scheme.outlineVariant),
              ),
              child: isSelected
                  ? Icon(
                      Icons.check,
                      size: 11,
                      color: swatch.color.computeLuminance() > 0.5 ? Colors.black87 : Colors.white,
                    )
                  : null,
            ),
            const SizedBox(width: 8),
            Text(
              swatch.name,
              style: Theme.of(context).textTheme.labelMedium?.copyWith(
                    fontWeight: isSelected ? FontWeight.w600 : FontWeight.normal,
                    color: isSelected ? scheme.primary : scheme.onSurface,
                  ),
            ),
          ],
        ),
      ),
    );
  }
}
