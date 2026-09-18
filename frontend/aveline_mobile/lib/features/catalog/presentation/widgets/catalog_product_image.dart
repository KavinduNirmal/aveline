import 'package:flutter/material.dart';

import '../../domain/catalog_product.dart';
import '../catalog_colors.dart';

/// A piece's photograph, or the atelier's stand-in while it has none.
///
/// The stand-in is built from the piece's own dominant colour, so an
/// unphotographed piece still reads as itself in the grid rather than as a
/// hole. A photograph that fails to load falls back to the same place.
class CatalogProductImage extends StatelessWidget {
  const CatalogProductImage({
    super.key,
    required this.product,
    this.iconSize = 34,
  });

  final CatalogProduct product;

  /// Size of the garment mark in the stand-in.
  final double iconSize;

  @override
  Widget build(BuildContext context) {
    final url = product.imageUrl;
    if (url == null || url.isEmpty) {
      return _Placeholder(product: product, iconSize: iconSize);
    }

    return Image.network(
      url,
      fit: BoxFit.cover,
      errorBuilder: (context, error, stackTrace) =>
          _Placeholder(product: product, iconSize: iconSize),
    );
  }
}

class _Placeholder extends StatelessWidget {
  const _Placeholder({required this.product, required this.iconSize});

  final CatalogProduct product;
  final double iconSize;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final colour = catalogColorValue(product.color);

    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [colour.withValues(alpha: 0.24), scheme.surfaceContainerLow],
        ),
      ),
      child: Center(
        child: Icon(
          Icons.checkroom_outlined,
          size: iconSize,
          color: colour.withValues(alpha: 0.7),
        ),
      ),
    );
  }
}
