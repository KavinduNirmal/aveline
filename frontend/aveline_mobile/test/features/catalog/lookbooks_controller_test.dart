import 'package:aveline_mobile/features/catalog/data/demo_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_payloads.dart';
import 'package:aveline_mobile/features/catalog/presentation/lookbooks_controller.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('LookbooksController', () {
    late DemoCatalogProductRepository repository;
    late LookbooksController controller;

    setUp(() {
      repository = DemoCatalogProductRepository(pageDelay: Duration.zero);
      controller = LookbooksController(repository);
    });

    test('initial state has empty lookbooks and default All occasion', () {
      expect(controller.lookbooks, isEmpty);
      expect(controller.selectedOccasion, 'All');
      expect(controller.isLoading, isFalse);
      expect(controller.error, isNull);
    });

    test('loadLookbooks loads lookbooks and populates list', () async {
      await controller.loadLookbooks();

      expect(controller.isLoading, isFalse);
      expect(controller.lookbooks, isNotEmpty);
      expect(controller.lookbooks.length, greaterThanOrEqualTo(4));
      expect(controller.filteredLookbooks.length, controller.lookbooks.length);
    });

    test('filters lookbooks by selected occasion', () async {
      await controller.loadLookbooks();

      controller.setOccasion('Bridal Heirloom');
      expect(controller.selectedOccasion, 'Bridal Heirloom');

      for (final look in controller.filteredLookbooks) {
        expect(look.occasion.toLowerCase(), contains('bridal heirloom'));
      }
    });

    test('filters lookbooks by search query across title, notes, and items', () async {
      await controller.loadLookbooks();

      controller.setSearchQuery('Emerald');
      expect(controller.filteredLookbooks, isNotEmpty);
      for (final look in controller.filteredLookbooks) {
        final matches = look.name.toLowerCase().contains('emerald') ||
            look.styleNotes.toLowerCase().contains('emerald') ||
            look.items.any((i) => i.name.toLowerCase().contains('emerald'));
        expect(matches, isTrue);
      }
    });

    test('composeLookbook creates new outfit and prepends to list', () async {
      await controller.loadLookbooks();
      final initialCount = controller.lookbooks.length;

      const payload = ComposeOutfitPayload(
        name: 'Curated Reception Saree Look',
        primaryItemId: 'piece-001',
        occasion: 'Sangeet & Reception',
        notes: 'Pair with heavy kundan.',
      );

      final composed = await controller.composeLookbook(payload);
      expect(composed.name, 'Curated Reception Saree Look');
      expect(controller.lookbooks.length, initialCount + 1);
      expect(controller.lookbooks.first.id, composed.id);
    });

    test('updateLookbook modifies lookbook in place', () async {
      await controller.loadLookbooks();
      final targetId = controller.lookbooks.first.id;

      const payload = UpdateLookbookPayload(
        name: 'Updated Haute Couture Look',
        occasion: 'Cocktail Reception & Gala',
      );

      final updated = await controller.updateLookbook(targetId, payload);
      expect(updated.name, 'Updated Haute Couture Look');
      expect(updated.occasion, 'Cocktail Reception & Gala');
      expect(controller.lookbooks.first.name, 'Updated Haute Couture Look');
    });

    test('deleteLookbook removes lookbook from collection', () async {
      await controller.loadLookbooks();
      final targetId = controller.lookbooks.first.id;
      final initialCount = controller.lookbooks.length;

      await controller.deleteLookbook(targetId);
      expect(controller.lookbooks.length, initialCount - 1);
      expect(controller.lookbooks.any((l) => l.id == targetId), isFalse);
    });
  });
}
