import 'package:flutter/material.dart';

/// `{boutique} - {section}`, set in the brand serif with a trailing fade.
///
/// A name too long for the line dissolves at the trailing edge rather than being
/// cut mid-letter, so the title keeps reading as a title at any width. The fade
/// is applied only when the name genuinely overflows: the band is a fixed share
/// of the line, so masking a name that fits would dim its last letters instead
/// of empty space beside it.
///
/// Shared by the dock tabs that name the shop in their own heading (Catalog,
/// Customers), so the two headers cannot drift apart.
class BrandSectionTitle extends StatelessWidget {
  const BrandSectionTitle({
    super.key,
    required this.boutiqueName,
    required this.section,
    required this.titleKey,
  });

  /// The boutique the screen belongs to, e.g. `Ceylon Atelier`.
  final String boutiqueName;

  /// The section the screen owns, e.g. `Catalog`.
  final String section;

  /// Rides the rendered text itself rather than this widget, so a test can read
  /// the wording and the style off the `Text`.
  final Key titleKey;

  /// Where the title starts to dissolve, as a fraction of the line's width.
  static const double fadeStart = 0.78;

  /// `Ceylon Atelier - Catalog`.
  String get label => '$boutiqueName - $section';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final style = theme.textTheme.displayMedium;

    return LayoutBuilder(
      builder: (context, constraints) {
        final title = Text(
          label,
          key: titleKey,
          maxLines: 1,
          softWrap: false,
          overflow: TextOverflow.clip,
          style: style,
        );

        // A name that fits is drawn plainly. The band spans a fixed share of the
        // line, so masking unconditionally dissolved the last letters of a name
        // that fitted perfectly well: `{boutique} - Customers` reaches further
        // along the line than `{boutique} - Catalog` does, and the longer section
        // word was enough to fade the shop's own name into the page.
        if (!_overflows(context, constraints.maxWidth, style)) {
          return title;
        }

        return ShaderMask(
          // `dstIn` keeps the pixel where the gradient is black and erases it
          // where the gradient is transparent, so the name dissolves into the
          // page instead of sitting under a translucent block.
          blendMode: BlendMode.dstIn,
          shaderCallback: (bounds) => const LinearGradient(
            begin: Alignment.centerLeft,
            end: Alignment.centerRight,
            colors: [Colors.black, Colors.black, Colors.transparent],
            stops: [0, fadeStart, 1],
          ).createShader(bounds),
          child: SizedBox(
            // Full width, so the band's own geometry is a property of the line
            // rather than of how long this particular name happens to be.
            width: double.infinity,
            child: title,
          ),
        );
      },
    );
  }

  /// Whether [label] is wider than the line it has to fit on.
  ///
  /// Measured with the style and the text scaling the title will actually be
  /// drawn with, so the two cannot disagree about whether it fits.
  bool _overflows(BuildContext context, double maxWidth, TextStyle? style) {
    if (!maxWidth.isFinite) {
      return false;
    }

    final painter = TextPainter(
      text: TextSpan(text: label, style: style),
      maxLines: 1,
      textDirection: Directionality.of(context),
      textScaler: MediaQuery.textScalerOf(context),
    )..layout();
    final width = painter.width;
    painter.dispose();
    return width > maxWidth;
  }
}
