import 'package:flutter/material.dart';

import '../../../../shared/widgets/filter_pill.dart';
import '../../../../shared/widgets/trailing_fade.dart';
import '../../domain/catalog_tag.dart';

/// The shop's own tags, as a horizontally scrolling row of selectable pills.
///
/// Every boutique curates its own vocabulary, so this row is fed by the shop's
/// tag list rather than a shared set. The row bleeds to both screen edges and is
/// always wider than a phone, so the fade on the trailing edge says "there is
/// more" and is dropped once the row is scrolled to its end.
class CatalogTagRow extends StatefulWidget {
  const CatalogTagRow({
    super.key,
    required this.tags,
    required this.selectedIds,
    required this.onToggled,
  });

  /// The shop's tags, in the order the shop lists them.
  final List<CatalogTag> tags;

  /// Ids of the tags currently narrowing the catalog.
  final Set<String> selectedIds;

  /// Called with a tag id when its pill is tapped.
  final ValueChanged<String> onToggled;

  /// Fits the pills plus their border.
  static const double height = 40;

  @override
  State<CatalogTagRow> createState() => _CatalogTagRowState();
}

class _CatalogTagRowState extends State<CatalogTagRow> {
  final ScrollController _controller = ScrollController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return SizedBox(
      height: CatalogTagRow.height,
      child: TrailingFade(
        controller: _controller,
        child: ListView.separated(
          controller: _controller,
          scrollDirection: Axis.horizontal,
          padding: TrailingFade.endPadding(context),
          itemCount: widget.tags.length,
          separatorBuilder: (context, index) => const SizedBox(width: 8),
          itemBuilder: (context, index) {
            final tag = widget.tags[index];
            // A horizontal row lays its children out at the row's full height,
            // so the pill is centred rather than stretched to it.
            return Center(
              child: FilterPill(
                key: ValueKey('catalog_tag_${tag.id}'),
                label: tag.label,
                selected: widget.selectedIds.contains(tag.id),
                onTap: () => widget.onToggled(tag.id),
              ),
            );
          },
        ),
      ),
    );
  }
}
