import 'package:aveline_mobile/features/catalog/domain/catalog_filters.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('CatalogFilters', () {
    test('starts empty', () {
      const filters = CatalogFilters.none();

      expect(filters.isEmpty, isTrue);
      expect(filters.activeCount, 0);
      expect(
        filters.isSelected(CatalogFilterGroup.fabric, 'Raw silk'),
        isFalse,
      );
    });

    test('adds and removes a multi-select value', () {
      var filters = const CatalogFilters.none();

      filters = filters.toggle(CatalogFilterGroup.fabric, 'Raw silk');
      expect(filters.isSelected(CatalogFilterGroup.fabric, 'Raw silk'), isTrue);
      expect(filters.activeCount, 1);

      filters = filters.toggle(CatalogFilterGroup.fabric, 'Chiffon');
      expect(filters.activeCount, 2);

      filters = filters.toggle(CatalogFilterGroup.fabric, 'Raw silk');
      expect(
        filters.isSelected(CatalogFilterGroup.fabric, 'Raw silk'),
        isFalse,
      );
      expect(filters.isSelected(CatalogFilterGroup.fabric, 'Chiffon'), isTrue);
      expect(filters.activeCount, 1);
    });

    test('keeps a single-select group to one value', () {
      var filters = const CatalogFilters.none();

      filters = filters.toggle(CatalogFilterGroup.availability, 'In stock');
      filters = filters.toggle(CatalogFilterGroup.availability, 'Reserved');

      expect(
        filters.isSelected(CatalogFilterGroup.availability, 'In stock'),
        isFalse,
      );
      expect(
        filters.isSelected(CatalogFilterGroup.availability, 'Reserved'),
        isTrue,
      );
      expect(filters.activeCount, 1);
    });

    test('choosing the chosen single value again clears the group', () {
      var filters = const CatalogFilters.none();

      filters = filters.toggle(CatalogFilterGroup.availability, 'In stock');
      filters = filters.toggle(CatalogFilterGroup.availability, 'In stock');

      expect(filters.isEmpty, isTrue);
    });

    test('counts across groups and drops emptied ones', () {
      var filters = const CatalogFilters.none();

      filters = filters
          .toggle(CatalogFilterGroup.availability, 'In stock')
          .toggle(CatalogFilterGroup.category, 'Sarees')
          .toggle(CatalogFilterGroup.size, '38');

      expect(filters.activeCount, 3);

      filters = filters.toggle(CatalogFilterGroup.availability, 'In stock');
      expect(filters.activeCount, 2);
      expect(filters.selectedIn(CatalogFilterGroup.availability), isEmpty);
    });

    test('leaves the original value untouched when toggling', () {
      final original = const CatalogFilters.none();

      final changed = original.toggle(CatalogFilterGroup.fabric, 'Velvet');

      // The screen hands its draft back through a route result, so a toggle has
      // to produce a new value rather than mutate the one already applied.
      expect(original.isEmpty, isTrue);
      expect(changed.isSelected(CatalogFilterGroup.fabric, 'Velvet'), isTrue);
    });

    test('value equality and hashCode work across identical selections', () {
      final a = const CatalogFilters.none()
          .toggle(CatalogFilterGroup.category, 'Sarees')
          .toggle(CatalogFilterGroup.size, '38');
      final b = const CatalogFilters.none()
          .toggle(CatalogFilterGroup.size, '38')
          .toggle(CatalogFilterGroup.category, 'Sarees');

      expect(a, equals(b));
      expect(a.hashCode, equals(b.hashCode));
    });

    test('round-trips through query parameters', () {
      final filters = const CatalogFilters.none()
          .toggle(CatalogFilterGroup.category, 'Sarees')
          .toggle(CatalogFilterGroup.category, 'Gowns')
          .toggle(CatalogFilterGroup.price, '25k - 75k');

      final params = filters.toQueryParameters();
      final restored = CatalogFilters.fromQueryParameters(params);

      expect(restored, equals(filters));
    });
  });

  group('CatalogFilterGroup', () {
    test('offers placeholder options for every group', () {
      for (final group in CatalogFilterGroup.values) {
        expect(
          group.options,
          isNotEmpty,
          reason: '${group.name} has no options',
        );
      }
    });

    test('only availability is single-select', () {
      final single = CatalogFilterGroup.values
          .where((group) => group.singleSelect)
          .toList();

      expect(single, [CatalogFilterGroup.availability]);
    });
  });
}
