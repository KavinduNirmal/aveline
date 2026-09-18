import 'package:flutter/material.dart';

/// The swatch a piece's `Color` name paints as.
///
/// `InventoryItemDto.Color` is free text the boutique types, so this resolves
/// the names the catalog knows and falls back to a neutral rose-grey rather
/// than throwing on a colour the palette has never seen.
Color catalogColorValue(String color) {
  return switch (color.trim().toLowerCase()) {
    'wine' => const Color(0xFF8B2E42),
    'rose quartz' => const Color(0xFFE0A9B4),
    'champagne' => const Color(0xFFE4D2AE),
    'emerald' => const Color(0xFF2F6B52),
    'midnight' => const Color(0xFF2B2F43),
    'ivory' => const Color(0xFFF3ECE3),
    'saffron' => const Color(0xFFD08A2C),
    'lilac' => const Color(0xFFB49CC9),
    _ => const Color(0xFFB9A6A9),
  };
}

/// A piece's colour as the one swatch the catalog shows.
class CatalogColorDot extends StatelessWidget {
  const CatalogColorDot({super.key, required this.colorName, this.size = 16});

  final String colorName;
  final double size;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Semantics(
      label: colorName,
      child: Container(
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: catalogColorValue(colorName),
          shape: BoxShape.circle,
          border: Border.all(color: scheme.outlineVariant),
        ),
      ),
    );
  }
}
