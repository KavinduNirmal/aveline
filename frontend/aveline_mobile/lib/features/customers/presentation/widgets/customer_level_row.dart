import 'package:flutter/material.dart';

import '../../../../shared/widgets/filter_pill.dart';
import '../../../../shared/widgets/trailing_fade.dart';
import '../../domain/customer_level.dart';

/// The boutique's client levels, as a horizontally scrolling row of pills.
///
/// One level at a time: a client holds exactly one grade, so the row behaves
/// like the availability group on the catalog's filter screen rather than like
/// its tag row, where a piece can carry several tags. There is no "All" pill for
/// the same reason there is none on the tag row — tapping the chosen level again
/// clears it, which the screen owns.
///
/// The row bleeds to both screen edges and is always wider than a phone, so the
/// fade on the trailing edge says "there is more" and is dropped once the row is
/// scrolled to its end.
class CustomerLevelRow extends StatefulWidget {
  const CustomerLevelRow({
    super.key,
    required this.selected,
    required this.onToggled,
  });

  /// The level narrowing the book, or `null` for every level.
  final CustomerLevel? selected;

  /// Called with the tapped level. The screen owns the toggle, because tapping
  /// the chosen level clears the narrowing rather than re-choosing it.
  final ValueChanged<CustomerLevel> onToggled;

  /// The row's height: fits the pills plus their border, matching the catalog's
  /// tag row so the two screens' headers line up.
  static const double height = 40;

  @override
  State<CustomerLevelRow> createState() => _CustomerLevelRowState();
}

class _CustomerLevelRowState extends State<CustomerLevelRow> {
  final ScrollController _controller = ScrollController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: CustomerLevelRow.height,
      child: TrailingFade(
        controller: _controller,
        child: ListView.separated(
          controller: _controller,
          scrollDirection: Axis.horizontal,
          padding: TrailingFade.endPadding(context),
          itemCount: CustomerLevel.values.length,
          separatorBuilder: (context, index) => const SizedBox(width: 8),
          itemBuilder: (context, index) {
            final level = CustomerLevel.values[index];
            // A horizontal row lays its children out at the row's full height,
            // so the pill is centred rather than stretched to it.
            return Center(
              child: FilterPill(
                key: ValueKey('customer_level_${level.name}'),
                label: level.label,
                selected: widget.selected == level,
                onTap: () => widget.onToggled(level),
              ),
            );
          },
        ),
      ),
    );
  }
}
