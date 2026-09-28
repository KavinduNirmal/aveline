import 'package:aveline_mobile/features/catalog/domain/outfit_composition.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_item.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_payloads.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('OutfitItem', () {
    test('parses from backend json properly', () {
      final json = {
        'id': 'oi-1',
        'itemId': 'item-101',
        'name': 'Royal Emerald Saree',
        'category': 'Sarees',
        'price': 45000.0,
        'imageUrl': 'https://images.unsplash.com/photo-saree',
        'position': 'top',
        'notes': 'Hero piece with gold embroidery',
      };

      final item = OutfitItem.fromJson(json);

      expect(item.id, 'oi-1');
      expect(item.itemId, 'item-101');
      expect(item.name, 'Royal Emerald Saree');
      expect(item.category, 'Sarees');
      expect(item.price, 45000.0);
      expect(item.imageUrl, 'https://images.unsplash.com/photo-saree');
      expect(item.position, 'top');
      expect(item.notes, 'Hero piece with gold embroidery');
    });

    test('serializes to json map correctly', () {
      const item = OutfitItem(
        id: 'oi-2',
        itemId: 'item-202',
        name: 'Kundhan Choker',
        category: 'Jewelry',
        price: 18500.0,
        imageUrl: 'https://images.unsplash.com/photo-jewelry',
        position: 'accessory',
        notes: 'Accent piece',
      );

      final json = item.toJson();
      expect(json['id'], 'oi-2');
      expect(json['itemId'], 'item-202');
      expect(json['name'], 'Kundhan Choker');
      expect(json['category'], 'Jewelry');
      expect(json['price'], 18500.0);
      expect(json['position'], 'accessory');
      expect(json['notes'], 'Accent piece');
    });
  });

  group('OutfitComposition', () {
    test('parses from backend json with items and total price', () {
      final json = {
        'id': 'comp-101',
        'name': 'Royal Sangeet Ensemble',
        'occasion': 'Sangeet & Reception',
        'totalPrice': 85000.0,
        'styleNotes': 'Elle suggests pairing with gold jewelry.',
        'heroImageUrl': 'https://images.unsplash.com/photo-hero',
        'createdAt': '2026-09-24T12:00:00.000Z',
        'items': [
          {
            'id': 'oi-1',
            'itemId': 'item-1',
            'name': 'Banarasi Brocade Saree',
            'category': 'Sarees',
            'price': 60000.0,
            'imageUrl': 'https://images.unsplash.com/photo-saree',
            'position': 'top',
          },
          {
            'id': 'oi-2',
            'itemId': 'item-2',
            'name': 'Heritage Gold Choker',
            'category': 'Jewelry',
            'price': 25000.0,
            'imageUrl': 'https://images.unsplash.com/photo-jewelry',
            'position': 'accessory',
          },
        ],
        'organizationId': 'org-1',
      };

      final composition = OutfitComposition.fromJson(json);

      expect(composition.id, 'comp-101');
      expect(composition.name, 'Royal Sangeet Ensemble');
      expect(composition.occasion, 'Sangeet & Reception');
      expect(composition.totalPrice, 85000.0);
      expect(composition.styleNotes, contains('gold jewelry'));
      expect(composition.heroImageUrl, 'https://images.unsplash.com/photo-hero');
      expect(composition.items.length, 2);
      expect(composition.items[0].name, 'Banarasi Brocade Saree');
      expect(composition.items[1].name, 'Heritage Gold Choker');
      expect(composition.organizationId, 'org-1');
      expect(composition.effectiveHeroImageUrl, 'https://images.unsplash.com/photo-hero');
    });

    test('falls back to first item image when heroImageUrl is empty', () {
      final json = {
        'id': 'comp-102',
        'name': 'Cocktail Evening Look',
        'occasion': 'Cocktail Reception & Gala',
        'totalPrice': 40000.0,
        'styleNotes': 'Contemporary draped silhouette.',
        'heroImageUrl': '',
        'items': [
          {
            'id': 'oi-1',
            'itemId': 'item-1',
            'name': 'Silk Gown',
            'category': 'Indo-Western',
            'price': 40000.0,
            'imageUrl': 'https://images.unsplash.com/photo-first-item',
            'position': 'top',
          }
        ],
      };

      final composition = OutfitComposition.fromJson(json);
      expect(composition.effectiveHeroImageUrl, 'https://images.unsplash.com/photo-first-item');
    });
  });

  group('Outfit Payloads', () {
    test('ComposeOutfitPayload serializes correctly', () {
      const payload = ComposeOutfitPayload(
        name: 'Bridal Reception Ensemble',
        primaryItemId: 'item-888',
        notes: 'Pair with heavy zari drape',
        occasion: 'Bridal Heirloom',
      );

      final json = payload.toJson();
      expect(json['name'], 'Bridal Reception Ensemble');
      expect(json['primaryItemId'], 'item-888');
      expect(json['notes'], 'Pair with heavy zari drape');
      expect(json['occasion'], 'Bridal Heirloom');
    });

    test('UpdateLookbookPayload serializes only provided non-empty fields', () {
      const payload = UpdateLookbookPayload(
        name: 'Renamed Lookbook',
        styleNotes: 'Updated stylist guidelines.',
      );

      final json = payload.toJson();
      expect(json['name'], 'Renamed Lookbook');
      expect(json['styleNotes'], 'Updated stylist guidelines.');
      expect(json.containsKey('occasion'), isFalse);
    });
  });
}
