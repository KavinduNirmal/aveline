import 'package:flutter/material.dart';

import '../../domain/catalog_product.dart';
import '../catalog_colors.dart';

/// The `Accept` header that makes Cloudinary's `f_auto` deliver WebP.
///
/// `f_auto` chooses the format from the request's `Accept` header, and a
/// non-browser client sends none: without this the CDN quietly serves JPEG and
/// the delivery saving (roughly half the bytes, strategy 3.7 and 6.11) is lost
/// with no visible failure. The JPEG fallback stays in the list so a decoder
/// that cannot handle WebP still renders.
const Map<String, String> _acceptHeader = {
  'Accept': 'image/webp,image/jpeg,*/*',
};

/// The width of the catalog delivery bitmap (`Media:CatalogDisplayWidth`).
///
/// A decode hint must never exceed what the server delivers: past this the
/// decoder would be upscaling bytes that were already thrown away.
const int _deliveryWidth = 800;

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

    // The delivered bitmap is up to 800 px wide (or the original when smaller),
    // so decoding it in full costs far more memory than either surface paints:
    // the hero is 320 dp x 400 dp = 640 x 800 physical at 2x, and a 360 dp grid
    // tile is about 153 dp x 121 dp = 306 x 242. Sizing the decode from this
    // widget's own constraints covers both without a second hard-coded size.
    return LayoutBuilder(
      builder: (context, constraints) {
        final devicePixelRatio = MediaQuery.devicePixelRatioOf(context);
        final cacheWidth = _physicalPixels(
          constraints.maxWidth,
          devicePixelRatio,
          max: _deliveryWidth,
        );
        final cacheHeight = _physicalPixels(
          constraints.maxHeight,
          devicePixelRatio,
        );

        Widget placeholder(BuildContext context, Object error,
                StackTrace? stackTrace) =>
            _Placeholder(product: product, iconSize: iconSize);

        if (cacheWidth == null && cacheHeight == null) {
          // Unbounded in both axes, so there is no destination to size
          // against. `ResizeImage` requires at least one dimension.
          return Image.network(
            url,
            fit: BoxFit.cover,
            headers: _acceptHeader,
            errorBuilder: placeholder,
          );
        }

        return Image(
          // Named on purpose rather than using `Image.network`'s
          // `cacheWidth`/`cacheHeight` shorthand: that shorthand builds a
          // `ResizeImage` with the default `ResizeImagePolicy.exact`, which
          // stretches a non-4:5 photograph to fill the box (the grid tile is
          // landscape, the hero portrait). `ResizeImagePolicy.fit` keeps the
          // source aspect ratio while still bounding the decode inside the
          // destination box.
          image: ResizeImage(
            NetworkImage(url, headers: _acceptHeader),
            width: cacheWidth,
            height: cacheHeight,
            policy: ResizeImagePolicy.fit,
          ),
          fit: BoxFit.cover,
          errorBuilder: placeholder,
        );
      },
    );
  }

  /// [logical] dp at [devicePixelRatio], clamped to [max] physical pixels.
  ///
  /// Returns null when there is nothing to size against (unbounded or empty),
  /// so an unbounded context falls back to the natural decode.
  static int? _physicalPixels(
    double logical,
    double devicePixelRatio, {
    int? max,
  }) {
    if (!logical.isFinite || logical <= 0) {
      return null;
    }
    final physical = (logical * devicePixelRatio).round();
    if (physical <= 0) {
      return null;
    }
    return max != null && physical > max ? max : physical;
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
