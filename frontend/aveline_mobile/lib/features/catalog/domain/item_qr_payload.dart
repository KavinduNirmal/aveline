import 'dart:convert';
import 'catalog_product.dart';

/// Supported formats for encoding garment physical floor tags.
enum QrFormatType {
  json('Structured JSON', 'Full metadata for scanner & boutique resolution'),
  url('Boutique URL', 'Deep link opening the piece in the boutique'),
  sku('Raw SKU', 'SKU barcode for existing point-of-sale registers');

  const QrFormatType(this.label, this.hint);

  final String label;
  final String hint;
}

/// Helper utilities for serializing floor tag QR payloads.
class ItemQrPayloadBuilder {
  static const String defaultOrigin = 'https://aveline.app';

  /// Builds a canonical deep link URL for a piece.
  static String buildUrl({
    required CatalogProduct product,
    String? organizationId,
    String? organizationSlug,
    String origin = defaultOrigin,
  }) {
    final slug = organizationSlug?.trim();
    if (slug != null && slug.isNotEmpty) {
      return '$origin/app/b/${Uri.encodeComponent(slug)}/catalog/${product.id}';
    }

    final orgId = organizationId?.trim() ?? product.organizationId.trim();
    final query = orgId.isNotEmpty ? '?org=${Uri.encodeComponent(orgId)}' : '';
    return '$origin/catalog/items/${product.id}$query';
  }

  /// Builds structured JSON containing full boutique context.
  static String buildJson({
    required CatalogProduct product,
    String? organizationId,
    String? organizationSlug,
    String origin = defaultOrigin,
  }) {
    final payloadMap = <String, dynamic>{
      'type': 'aveline_inventory_item',
      if (organizationId != null && organizationId.trim().isNotEmpty)
        'orgId': organizationId.trim()
      else if (product.organizationId.trim().isNotEmpty)
        'orgId': product.organizationId.trim(),
      'itemId': product.id,
      'sku': product.sku ?? 'AVL-000',
      'url': buildUrl(
        product: product,
        organizationId: organizationId,
        organizationSlug: organizationSlug,
        origin: origin,
      ),
      'v': 1,
    };

    return jsonEncode(payloadMap);
  }

  /// Returns raw SKU.
  static String buildSku(CatalogProduct product) {
    return product.sku ?? 'AVL-000';
  }

  /// Resolves the payload string according to the selected format type.
  static String resolvePayload({
    required QrFormatType format,
    required CatalogProduct product,
    String? organizationId,
    String? organizationSlug,
  }) {
    return switch (format) {
      QrFormatType.json => buildJson(
          product: product,
          organizationId: organizationId,
          organizationSlug: organizationSlug,
        ),
      QrFormatType.url => buildUrl(
          product: product,
          organizationId: organizationId,
          organizationSlug: organizationSlug,
        ),
      QrFormatType.sku => buildSku(product),
    };
  }
}
