import 'dart:convert';
import 'package:flutter_test/flutter_test.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/domain/item_qr_payload.dart';

void main() {
  final testProduct = CatalogProduct(
    id: 'prod-123',
    organizationId: 'org-456',
    name: 'Kanjeevaram Silk Saree',
    category: 'Sarees',
    color: 'Emerald Green',
    sizes: const ['One size'],
    price: 125000,
    cost: 65000,
    quantity: 4,
    status: CatalogItemStatus.available,
    isAvailable: true,
    createdAtUtc: DateTime.utc(2026, 9, 24),
    sku: 'AVL-SAR-001',
    fabric: 'Pure Silk',
  );

  group('ItemQrPayloadBuilder', () {
    test('builds URL with slug when slug is present', () {
      final url = ItemQrPayloadBuilder.buildUrl(
        product: testProduct,
        organizationSlug: 'maison-aveline',
      );

      expect(url, 'https://aveline.app/app/b/maison-aveline/catalog/prod-123');
    });

    test('builds URL with org ID query parameter when slug is absent', () {
      final url = ItemQrPayloadBuilder.buildUrl(
        product: testProduct,
        organizationId: 'org-999',
      );

      expect(url, 'https://aveline.app/catalog/items/prod-123?org=org-999');
    });

    test('builds structured JSON payload with expected schema', () {
      final jsonStr = ItemQrPayloadBuilder.buildJson(
        product: testProduct,
        organizationId: 'org-456',
        organizationSlug: 'boutique-1',
      );

      final decoded = jsonDecode(jsonStr) as Map<String, dynamic>;
      expect(decoded['type'], 'aveline_inventory_item');
      expect(decoded['orgId'], 'org-456');
      expect(decoded['itemId'], 'prod-123');
      expect(decoded['sku'], 'AVL-SAR-001');
      expect(decoded['url'], contains('/app/b/boutique-1/catalog/prod-123'));
      expect(decoded['v'], 1);
    });

    test('builds raw SKU string', () {
      final sku = ItemQrPayloadBuilder.buildSku(testProduct);
      expect(sku, 'AVL-SAR-001');
    });

    test('resolvePayload handles all format types', () {
      final jsonRes = ItemQrPayloadBuilder.resolvePayload(
        format: QrFormatType.json,
        product: testProduct,
      );
      final urlRes = ItemQrPayloadBuilder.resolvePayload(
        format: QrFormatType.url,
        product: testProduct,
      );
      final skuRes = ItemQrPayloadBuilder.resolvePayload(
        format: QrFormatType.sku,
        product: testProduct,
      );

      expect(jsonRes, startsWith('{"type":"aveline_inventory_item"'));
      expect(urlRes, startsWith('https://aveline.app/'));
      expect(skuRes, 'AVL-SAR-001');
    });
  });
}
