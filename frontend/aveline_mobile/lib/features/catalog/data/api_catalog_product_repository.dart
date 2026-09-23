import 'package:dio/dio.dart';

import '../domain/catalog_filters.dart';
import '../domain/catalog_product.dart';
import 'catalog_product_repository.dart';

/// Catalog repository backed by the Aveline .NET Backend API.
class ApiCatalogProductRepository implements CatalogProductRepository {
  ApiCatalogProductRepository(
    this._dio, {
    required this.organizationId,
  });

  final Dio _dio;
  final String? Function() organizationId;

  @override
  Future<CatalogProductPage> fetchPage({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      return const CatalogProductPage(products: [], hasMore: false);
    }

    final queryBody = _buildQueryBody(page: page, pageSize: pageSize, query: query);

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/catalog/items/query',
      data: queryBody,
    );

    final data = response.data ?? const {};
    final rawItems = (data['items'] as List<dynamic>?) ?? const [];
    final total = (data['total'] as num?)?.toInt() ?? 0;

    final products = rawItems
        .map((item) => _parseProduct(item as Map<String, dynamic>))
        .toList();

    // In mobile controller, page is 0-based index.
    // Calculate hasMore using total count and loaded count:
    final hasMore = (page * pageSize + products.length) < total;

    return CatalogProductPage(
      products: products,
      hasMore: hasMore,
    );
  }

  @override
  Future<CatalogProduct?> fetchProduct(String id) async {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) return null;

    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$orgId/catalog/items/$id',
      );
      if (response.data == null) return null;
      return _parseProduct(response.data!);
    } on DioException catch (e) {
      if (e.response?.statusCode == 404) return null;
      rethrow;
    }
  }

  @override
  Future<CatalogProduct> updateStatus(
    String id,
    CatalogItemStatus status,
  ) async {
    final orgId = _requireOrganizationId();

    final response = await _dio.patch<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/catalog/items/$id/status',
      data: {'status': status.wireValue},
    );

    final data = response.data;
    if (data == null) {
      throw StateError(
        'The API accepted ${status.wireValue} for $id but returned no piece.',
      );
    }
    return _parseProduct(data);
  }

  @override
  Future<String> requestSupply({
    required CatalogProduct piece,
    int quantityNeeded = 1,
    String urgency = 'medium',
  }) async {
    final orgId = _requireOrganizationId();

    final response = await _dio.post<Map<String, dynamic>>(
      '/api/v1/orgs/$orgId/catalog/sourcing',
      data: {
        'category': piece.category,
        'color': piece.color,
        // The ticket asks for words about the piece. A piece with no description sends
        // its name rather than an empty string, which the route would store as a blank.
        'description': piece.description ?? piece.name,
        'targetPrice': piece.price,
        'quantityNeeded': quantityNeeded,
        'urgency': urgency,
      },
    );

    return response.data?['id']?.toString() ?? '';
  }

  /// The organization every catalog route is scoped to.
  ///
  /// A read with no organization can honestly answer with an empty page, but a mutation has
  /// nowhere to go. Throwing lets the screen say the action did not happen, rather than
  /// toasting a success for a request that was never made.
  String _requireOrganizationId() {
    final orgId = organizationId();
    if (orgId == null || orgId.isEmpty) {
      throw StateError('No organization is selected.');
    }
    return orgId;
  }

  Map<String, dynamic> _buildQueryBody({
    required int page,
    required int pageSize,
    required CatalogProductQuery query,
  }) {
    final body = <String, dynamic>{
      'search': query.search.trim().isEmpty ? null : query.search.trim(),
      'page': page + 1, // Convert 0-based client page to 1-based server page
      'pageSize': pageSize,
    };

    if (query.tagIds.isNotEmpty) {
      body['tagIds'] = query.tagIds.toList();
    }

    final filters = query.filters;
    if (!filters.isEmpty) {
      final categories = filters.selectedIn(CatalogFilterGroup.category);
      if (categories.isNotEmpty) {
        body['categories'] = categories.toList();
      }

      final fabrics = filters.selectedIn(CatalogFilterGroup.fabric);
      if (fabrics.isNotEmpty) {
        body['fabrics'] = fabrics.toList();
      }

      final sizes = filters.selectedIn(CatalogFilterGroup.size);
      if (sizes.isNotEmpty) {
        body['sizes'] = sizes.toList();
      }

      final statuses = filters.selectedIn(CatalogFilterGroup.availability);
      if (statuses.isNotEmpty) {
        // Map display label to wire value
        body['statuses'] = statuses.map((s) {
          return switch (s.trim().toLowerCase()) {
            'available' => 'available',
            'on hold' => 'on_hold',
            'unavailable' => 'unavailable',
            'sold out' => 'sold_out',
            'archived' => 'archived',
            _ => s.toLowerCase().replaceAll(' ', '_'),
          };
        }).toList();
      }

      final priceBands = filters.selectedIn(CatalogFilterGroup.price);
      if (priceBands.isNotEmpty) {
        body['priceBands'] = priceBands.map((b) {
          return switch (b.trim()) {
            'Under 25k' => 'under_25k',
            '25k - 75k' => '25k_to_75k',
            '75k - 150k' => '75k_to_150k',
            'Over 150k' => 'over_150k',
            _ => b.toLowerCase().replaceAll(' ', '_'),
          };
        }).toList();
      }
    }

    return body;
  }

  CatalogProduct _parseProduct(Map<String, dynamic> json) {
    final sizesList = (json['sizes'] as List<dynamic>?)
            ?.map((s) => s.toString())
            .toList() ??
        const <String>[];

    final rawMetadata = json['metadata'] as Map<String, dynamic>?;
    final metadata = rawMetadata != null
        ? rawMetadata.map((k, v) => MapEntry(k, v?.toString() ?? ''))
        : const <String, String>{};

    final rawTags = json['tags'] as List<dynamic>?;
    final tags = rawTags != null
        ? rawTags.map((t) => t.toString()).toSet()
        : const <String>{};

    return CatalogProduct(
      id: json['id']?.toString() ?? '',
      organizationId: json['orgId']?.toString() ?? json['organizationId']?.toString() ?? '',
      name: json['itemName']?.toString() ?? 'Untitled Piece',
      category: json['category']?.toString() ?? 'General',
      color: json['color']?.toString() ?? 'Unspecified',
      sizes: sizesList,
      price: (json['price'] as num?)?.toDouble() ?? 0.0,
      cost: (json['cost'] as num?)?.toDouble() ?? 0.0,
      quantity: (json['quantity'] as num?)?.toInt() ?? 0,
      status: CatalogItemStatus.parse(json['status']?.toString()),
      isAvailable: json['isAvailable'] == true,
      createdAtUtc: DateTime.tryParse(json['createdAtUtc']?.toString() ?? '') ?? DateTime.now(),
      fabric: json['fabric']?.toString(),
      style: json['style']?.toString(),
      imageUrl: json['imageUrl']?.toString(),
      sku: json['sku']?.toString(),
      description: json['description']?.toString(),
      metadata: metadata,
      deletedAt: DateTime.tryParse(json['deletedAt']?.toString() ?? ''),
      sourcedFrom: metadata['sourcedFrom'] ?? metadata['source'],
      discountMinPercent: (json['discountMinPercent'] as num?)?.toInt() ?? 0,
      discountMaxPercent: (json['discountMaxPercent'] as num?)?.toInt() ?? 0,
      tags: tags,
    );
  }
}
