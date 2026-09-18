/// The catalog's narrowing options, grouped.
///
/// The options are placeholders until the boutique's own inventory taxonomy is
/// wired; the grouping and the single/multi-select behaviour are what the filter
/// screen and its tests are built on.
enum CatalogFilterGroup {
  availability('Availability', singleSelect: true),
  category('Category'),
  fabric('Fabric'),
  size('Size'),
  price('Price');

  const CatalogFilterGroup(this.label, {this.singleSelect = false});

  /// The overline the filter screen prints above the group.
  final String label;

  /// Whether the group holds one value at a time.
  final bool singleSelect;

  /// The choices the group offers.
  List<String> get options => switch (this) {
    CatalogFilterGroup.availability => const [
      'Available',
      'On hold',
      'Unavailable',
      'Sold out',
      'Archived',
    ],
    CatalogFilterGroup.category => const [
      'Sarees',
      'Lehengas',
      'Gowns',
      'Outerwear',
      'Silk blouses',
      'Accessories',
    ],
    CatalogFilterGroup.fabric => const [
      'Raw silk',
      'Chiffon',
      'Handloom cotton',
      'Velvet',
      'Organza',
    ],
    CatalogFilterGroup.size => const ['36', '38', '40', '42', 'Custom'],
    CatalogFilterGroup.price => const [
      'Under 25k',
      '25k - 75k',
      '75k - 150k',
      'Over 150k',
    ],
  };
}

/// The filter options a user has applied to the catalog search.
///
/// Immutable: every change comes back as a new value, which is what lets the
/// filter screen hand its draft back to the catalog through a route result
/// rather than sharing mutable state between two screens.
class CatalogFilters {
  const CatalogFilters(this._selected);

  /// Nothing applied: the whole catalog is in scope.
  const CatalogFilters.none()
    : _selected = const <CatalogFilterGroup, Set<String>>{};

  final Map<CatalogFilterGroup, Set<String>> _selected;

  /// The values applied in [group].
  Set<String> selectedIn(CatalogFilterGroup group) =>
      _selected[group] ?? const <String>{};

  bool isSelected(CatalogFilterGroup group, String value) =>
      selectedIn(group).contains(value);

  /// How many options are applied across every group.
  int get activeCount =>
      _selected.values.fold(0, (total, values) => total + values.length);

  bool get isEmpty => activeCount == 0;

  /// The filters with [value] toggled in [group].
  ///
  /// A single-select group swaps its value; choosing the chosen value again
  /// clears the group rather than leaving it stuck on.
  CatalogFilters toggle(CatalogFilterGroup group, String value) {
    final next = <CatalogFilterGroup, Set<String>>{
      for (final entry in _selected.entries) entry.key: {...entry.value},
    };

    final values = next.putIfAbsent(group, () => <String>{});
    if (group.singleSelect) {
      final wasSelected = values.contains(value);
      values.clear();
      if (!wasSelected) {
        values.add(value);
      }
    } else if (!values.remove(value)) {
      values.add(value);
    }

    next.removeWhere((_, values) => values.isEmpty);
    return CatalogFilters(next);
  }
}
