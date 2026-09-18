import 'package:flutter/material.dart';

import '../../domain/catalog_product.dart';
import '../catalog_colors.dart';
import 'catalog_product_image.dart';

/// One piece in the catalog grid.
///
/// The card is the whole affordance: there is no button on it, and tapping
/// anywhere opens the piece. Everything the associate scans for is on the face
/// — photograph, price, status, copy, colour and sizes — so the grid can be read
/// without opening anything.
class CatalogProductCard extends StatelessWidget {
  const CatalogProductCard({
    super.key,
    required this.product,
    required this.onTap,
  });

  final CatalogProduct product;

  /// Called when anywhere on the card is tapped.
  final VoidCallback onTap;

  /// Two lines of `bodySmall` (12pt at 1.4) plus its leading.
  ///
  /// Fixed so two cards in a grid row stay the same height whatever the copy
  /// they carry, which is what keeps the rows aligned.
  static const double _descriptionHeight = 34;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Semantics(
      button: true,
      label: '${product.name}, ${product.priceLabel}, ${product.stockLabel}',
      child: Material(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
        clipBehavior: Clip.antiAlias,
        child: InkWell(
          onTap: onTap,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Expanded(child: CatalogProductImage(product: product)),
              Padding(
                padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      product.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.titleSmall,
                    ),
                    const SizedBox(height: 4),
                    SizedBox(
                      height: _descriptionHeight,
                      child: Text(
                        product.description ?? '',
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: scheme.onSurfaceVariant,
                        ),
                      ),
                    ),
                    const SizedBox(height: 8),
                    Row(
                      children: [
                        CatalogColorDot(
                          key: ValueKey('catalog_colour_${product.id}'),
                          colorName: product.color,
                        ),
                        const SizedBox(width: 6),
                        // Flexible rather than a spacer: a long price, or a
                        // wider face on a narrow phone, ellipsises instead of
                        // overflowing the card.
                        Expanded(
                          child: Text(
                            product.priceLabel,
                            maxLines: 1,
                            textAlign: TextAlign.right,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleMedium,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 4),
                    Text(
                      product.stockLabel,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: _statusColour(scheme, product.status),
                      ),
                    ),
                    const SizedBox(height: 4),
                    Text(
                      'Sizes ${product.sizesLabel}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// Keeps the status line on the brand's palette rather than inventing a
  /// traffic light: only the states that need the associate to act take an
  /// accent, and the quiet one stays quiet.
  static Color _statusColour(ColorScheme scheme, CatalogItemStatus status) {
    return switch (status) {
      CatalogItemStatus.available => scheme.onSurfaceVariant,
      CatalogItemStatus.onHold => scheme.primary,
      CatalogItemStatus.soldOut => scheme.error,
      CatalogItemStatus.unavailable ||
      CatalogItemStatus.archived => scheme.outline,
    };
  }
}
