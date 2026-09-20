import 'package:aveline_mobile/features/catalog/data/api_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_filters.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ApiCatalogProductRepository', () {
    late Dio dio;
    late List<Map<String, dynamic>> recordedRequests;

    setUp(() {
      dio = Dio();
      recordedRequests = [];

      dio.interceptors.add(
        InterceptorsWrapper(
          onRequest: (options, handler) {
            recordedRequests.add({
              'path': options.path,
              'method': options.method,
              'data': options.data,
              'queryParameters': options.queryParameters,
            });

            if (options.path.endsWith('/catalog/items/query')) {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'items': [
                      {
                        'id': 'prod-1',
                        'orgId': 'org-123',
                        'itemName': 'Banarasi Silk Saree',
                        'category': 'Sarees',
                        'color': 'Crimson',
                        'sizes': ['36', '38'],
                        'price': 45000.0,
                        'cost': 20000.0,
                        'quantity': 5,
                        'status': 'available',
                        'isAvailable': true,
                        'createdAtUtc': '2026-09-01T00:00:00Z',
                        'fabric': 'Raw silk',
                        'style': 'Traditional',
                        'sku': 'BAN-001',
                      }
                    ],
                    'total': 15,
                    'page': 1,
                    'pageSize': 8,
                    'generatedAt': '2026-09-19T20:00:00Z',
                  },
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/items/prod-1')) {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'prod-1',
                    'orgId': 'org-123',
                    'itemName': 'Banarasi Silk Saree',
                    'category': 'Sarees',
                    'color': 'Crimson',
                    'sizes': ['36', '38'],
                    'price': 45000.0,
                    'cost': 20000.0,
                    'quantity': 5,
                    'status': 'available',
                    'isAvailable': true,
                    'createdAtUtc': '2026-09-01T00:00:00Z',
                    'fabric': 'Raw silk',
                  },
                ),
              );
              return;
            }

            handler.next(options);
          },
        ),
      );
    });

    test('returns empty page when organizationId is missing', () async {
      final repo = ApiCatalogProductRepository(
        dio,
        organizationId: () => null,
      );

      final page = await repo.fetchPage(
        page: 0,
        pageSize: 8,
        query: const CatalogProductQuery(),
      );

      expect(page.products, isEmpty);
      expect(page.hasMore, isFalse);
      expect(recordedRequests, isEmpty);
    });

    test('converts 0-based page to 1-based server page and calculates hasMore', () async {
      final repo = ApiCatalogProductRepository(
        dio,
        organizationId: () => 'org-123',
      );

      final page = await repo.fetchPage(
        page: 0,
        pageSize: 8,
        query: const CatalogProductQuery(),
      );

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.first;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items/query');
      expect(req['method'], 'POST');
      final data = req['data'] as Map<String, dynamic>;
      expect(data['page'], 1);
      expect(data['pageSize'], 8);

      expect(page.products, hasLength(1));
      expect(page.products.first.name, 'Banarasi Silk Saree');
      expect(page.products.first.category, 'Sarees');
      expect(page.products.first.fabric, 'Raw silk');
      expect(page.hasMore, isTrue); // (0 * 8 + 1) < 15
    });

    test('serializes multi-select filters and wire values correctly', () async {
      final repo = ApiCatalogProductRepository(
        dio,
        organizationId: () => 'org-123',
      );

      final filters = const CatalogFilters.none()
          .toggle(CatalogFilterGroup.category, 'Sarees')
          .toggle(CatalogFilterGroup.category, 'Gowns')
          .toggle(CatalogFilterGroup.fabric, 'Raw silk')
          .toggle(CatalogFilterGroup.availability, 'On hold')
          .toggle(CatalogFilterGroup.price, '25k - 75k');

      await repo.fetchPage(
        page: 1,
        pageSize: 8,
        query: CatalogProductQuery(
          search: 'bridal',
          filters: filters,
          tagIds: {'new-in'},
        ),
      );

      expect(recordedRequests, hasLength(1));
      final data = recordedRequests.first['data'] as Map<String, dynamic>;
      expect(data['page'], 2);
      expect(data['pageSize'], 8);
      expect(data['search'], 'bridal');
      expect(data['tagIds'], ['new-in']);
      expect(data['categories'], containsAll(['Sarees', 'Gowns']));
      expect(data['fabrics'], ['Raw silk']);
      expect(data['statuses'], ['on_hold']);
      expect(data['priceBands'], ['25k_to_75k']);
    });

    test('fetchProduct resolves item by id', () async {
      final repo = ApiCatalogProductRepository(
        dio,
        organizationId: () => 'org-123',
      );

      final product = await repo.fetchProduct('prod-1');
      expect(product, isNotNull);
      expect(product!.id, 'prod-1');
      expect(product.name, 'Banarasi Silk Saree');
      expect(product.status, CatalogItemStatus.available);
    });
  });
}
