import 'package:flutter/material.dart';

/// The letter a stretch of the book files under, pinned to the top of the list
/// while that letter is on screen.
///
/// Pinned rather than scrolled away, because it is the only thing that says which
/// letter the associate is in once the list is hundreds of rows long, and it is
/// what the index is scrolling to.
class CustomerSectionHeader extends StatelessWidget {
  const CustomerSectionHeader({super.key, required this.letter});

  final String letter;

  /// The header's height, which the index's offset arithmetic depends on.
  static const double height = 32;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      height: height,
      // Pinned headers sit over the rows as they pass, so this cannot be fully
      // transparent; a hint of the page's own surface keeps the rows legible
      // just under it without reading as a solid band.
      color: scheme.surface.withValues(alpha: 0.92),
      padding: const EdgeInsets.symmetric(horizontal: 20),
      alignment: Alignment.centerLeft,
      child: Text(
        letter,
        key: ValueKey('customer_section_$letter'),
        style: theme.textTheme.labelMedium?.copyWith(
          color: scheme.primary,
          fontWeight: FontWeight.w700,
          letterSpacing: 1.2,
        ),
      ),
    );
  }
}

/// Pins [CustomerSectionHeader] to the top of the list.
class CustomerSectionHeaderDelegate extends SliverPersistentHeaderDelegate {
  const CustomerSectionHeaderDelegate({required this.letter});

  final String letter;

  @override
  double get minExtent => CustomerSectionHeader.height;

  @override
  double get maxExtent => CustomerSectionHeader.height;

  @override
  Widget build(BuildContext context, double shrinkOffset, bool overlapsContent) {
    return CustomerSectionHeader(letter: letter);
  }

  @override
  bool shouldRebuild(CustomerSectionHeaderDelegate oldDelegate) =>
      oldDelegate.letter != letter;
}
