import 'package:aveline_mobile/features/catalog/data/api_catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/data/catalog_product_repository.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_filters.dart';
import 'package:aveline_mobile/features/catalog/domain/catalog_product.dart';
import 'package:aveline_mobile/features/catalog/domain/outfit_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/product_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sale_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_payloads.dart';
import 'package:aveline_mobile/features/catalog/domain/sourcing_status.dart';
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

            if (options.path.endsWith('/catalog/tags') && options.method == 'GET') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: [
                    {
                      'id': 'tag-1',
                      'orgId': 'org-123',
                      'slug': 'bridal',
                      'label': 'Bridal',
                      'colorHex': '#D4AF37',
                      'sortOrder': 0,
                      'itemCount': 5,
                    },
                    {
                      'id': 'tag-2',
                      'orgId': 'org-123',
                      'slug': 'festive',
                      'label': 'Festive',
                      'colorHex': '#9E2A2B',
                      'sortOrder': 1,
                      'itemCount': 3,
                    }
                  ],
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/items/prod-1/status')) {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  // The route answers with the row it now holds, so the status in the reply
                  // is the one that was asked for rather than the seeded one.
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
                    'status': body['status'],
                    'isAvailable': body['status'] == 'available',
                    'createdAtUtc': '2026-09-01T00:00:00Z',
                    'fabric': 'Raw silk',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/sourcing') && options.method == 'GET') {
              final statusParam = options.queryParameters['status'];
              final allTickets = [
                {
                  'id': 'src-1',
                  'clientName': 'Mrs. Radhika Merchant',
                  'category': 'Lehengas',
                  'color': 'Peacock Emerald',
                  'itemDescription': 'Custom bridal lehenga',
                  'supplierId': 'sup-1',
                  'supplierName': 'Varanasi Silk Works',
                  'estimatedCost': 950.0,
                  'targetPrice': 2000.0,
                  'status': 'pending',
                },
                {
                  'id': 'src-2',
                  'clientName': 'Devraj Rajput',
                  'category': 'Sherwanis',
                  'color': 'Ivory Cream',
                  'itemDescription': 'Raw silk tailored sherwani',
                  'supplierId': 'sup-2',
                  'supplierName': 'Jaipur Royal Handlooms',
                  'estimatedCost': 650.0,
                  'targetPrice': 1450.0,
                  'status': 'quoted',
                },
              ];

              final filtered = statusParam != null
                  ? allTickets.where((t) => t['status'] == statusParam).toList()
                  : allTickets;

              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: filtered,
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/sourcing/') && options.path.endsWith('/status') && options.method == 'PATCH') {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'src-1',
                    'clientName': 'Mrs. Radhika Merchant',
                    'category': 'Lehengas',
                    'color': 'Peacock Emerald',
                    'itemDescription': 'Custom bridal lehenga',
                    'supplierId': 'sup-1',
                    'supplierName': 'Varanasi Silk Works',
                    'estimatedCost': 950.0,
                    'targetPrice': 2000.0,
                    'status': body['status'] ?? 'approved',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/sourcing') && options.method == 'POST') {
              final body = options.data as Map<String, dynamic>;
              if (body.containsKey('clientName')) {
                handler.resolve(
                  Response(
                    requestOptions: options,
                    statusCode: 201,
                    data: {
                      'id': 'src-created-1',
                      'clientName': body['clientName'],
                      'category': body['category'] ?? 'Lehengas',
                      'color': body['color'] ?? 'Emerald',
                      'itemDescription': body['description'] ?? body['itemDescription'] ?? 'Bespoke piece',
                      'supplierId': body['supplierId'] ?? 'sup-1',
                      'supplierName': body['supplierName'] ?? 'Partner Atelier',
                      'estimatedCost': (body['estimatedCost'] as num?)?.toDouble() ?? 500.0,
                      'targetPrice': (body['targetPrice'] as num?)?.toDouble() ?? 1200.0,
                      'status': 'pending',
                      'urgency': body['urgency'] ?? 'medium',
                    },
                  ),
                );
                return;
              }

              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 201,
                  data: {'id': 'src-9', 'category': 'Sarees'},
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/suppliers') && options.method == 'GET') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: [
                    {
                      'id': 'sup-1',
                      'name': 'Varanasi Silk Works',
                      'location': 'Varanasi, UP',
                      'specialty': 'Pure Katan Silk & Brocade',
                      'rating': 4.9,
                    },
                    {
                      'id': 'sup-2',
                      'name': 'Jaipur Royal Handlooms',
                      'location': 'Jaipur, Rajasthan',
                      'specialty': 'Gota Patti & Bandhani',
                      'rating': 4.8,
                    },
                  ],
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/images/upload')) {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'img-101',
                    'url': '/api/v1/orgs/org-123/catalog/images/img-101/file',
                    'fileName': 'garment.jpg',
                    'size': 1024,
                    'contentType': 'image/jpeg',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/analyze-image')) {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'category': 'Lehengas',
                    'primary_color': 'Emerald Green',
                    'color_hex': '#2F6B52',
                    'fabric': 'Raw Silk',
                    'style': 'Bridal Regal',
                    'pattern': 'Zari Brocade',
                    'suggested_item_name': 'Emerald Zari Lehenga',
                    'confidenceScore': 0.95,
                    'visual_attributes': ['Emerald Green', 'Raw Silk', 'Zari Brocade'],
                    'summary': 'Handcrafted bridal lehenga in rich silk.',
                  },
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/items') && options.method == 'POST') {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 201,
                  data: {
                    'id': 'prod-created-1',
                    'orgId': 'org-123',
                    'itemName': body['itemName'] ?? 'New Piece',
                    'category': body['category'] ?? 'Sarees',
                    'color': body['color'] ?? 'Emerald Green',
                    'sizes': body['sizes'] ?? ['38'],
                    'price': (body['price'] as num?)?.toDouble() ?? 50000.0,
                    'cost': (body['cost'] as num?)?.toDouble() ?? 25000.0,
                    'quantity': (body['quantity'] as num?)?.toInt() ?? 2,
                    'status': 'available',
                    'isAvailable': true,
                    'createdAtUtc': '2026-09-24T12:00:00Z',
                    'fabric': body['fabric'],
                    'style': body['style'],
                    'sku': body['sku'] ?? 'AVL-999',
                  },
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/items/prod-1') && options.method == 'PUT') {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'prod-1',
                    'orgId': 'org-123',
                    'itemName': body['itemName'] ?? 'Updated Name',
                    'category': body['category'] ?? 'Sarees',
                    'color': body['color'] ?? 'Crimson',
                    'sizes': body['sizes'] ?? ['36', '38'],
                    'price': (body['price'] as num?)?.toDouble() ?? 45000.0,
                    'cost': (body['cost'] as num?)?.toDouble() ?? 20000.0,
                    'quantity': (body['quantity'] as num?)?.toInt() ?? 5,
                    'status': body['status'] ?? 'available',
                    'isAvailable': true,
                    'createdAtUtc': '2026-09-01T00:00:00Z',
                    'fabric': body['fabric'],
                  },
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/items/prod-1/sales') && options.method == 'POST') {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'itemId': 'prod-1',
                    'itemName': 'Banarasi Silk Saree',
                    'sku': 'AVL-SAR-001',
                    'quantitySold': body['quantity'] ?? 1,
                    'unitPrice': (body['unitPrice'] as num?)?.toDouble() ?? 45000.0,
                    'totalAmount': ((body['quantity'] as num?) ?? 1) * ((body['unitPrice'] as num?) ?? 45000.0),
                    'remainingStock': 4,
                    'status': 'available',
                    'ledgerEntryId': 'tx-ledger-123',
                    'recordedAtUtc': '2026-09-24T12:00:00Z',
                  },
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/items/prod-1') && options.method == 'DELETE') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 204,
                  data: null,
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

            if (options.path.endsWith('/catalog/lookbooks') && options.method == 'GET') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: [
                    {
                      'id': 'lb-1',
                      'name': 'Emerald Heritage Sangeet Look',
                      'occasion': 'Sangeet & Reception',
                      'totalPrice': 79000.0,
                      'styleNotes': 'Pair with temple gold.',
                      'heroImageUrl': 'https://images.unsplash.com/photo-1',
                      'items': [
                        {
                          'id': 'oi-1',
                          'itemId': 'piece-1',
                          'name': 'Emerald Saree',
                          'category': 'Sarees',
                          'price': 45000.0,
                          'imageUrl': 'https://images.unsplash.com/photo-1',
                          'position': 'top',
                        },
                      ],
                    },
                    {
                      'id': 'lb-2',
                      'name': 'Imperial Crimson Bridal Heirloom',
                      'occasion': 'Bridal Heirloom',
                      'totalPrice': 190000.0,
                      'styleNotes': 'Bridal ensemble.',
                      'heroImageUrl': 'https://images.unsplash.com/photo-2',
                      'items': [],
                    },
                  ],
                ),
              );
              return;
            }

            if (options.path.endsWith('/catalog/lookbooks/compose') && options.method == 'POST') {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'lb-composed-1',
                    'name': body['name'] ?? 'Composed Look',
                    'occasion': body['occasion'] ?? 'Sangeet & Reception',
                    'totalPrice': 65000.0,
                    'styleNotes': 'Curated with Elle AI.',
                    'heroImageUrl': 'https://images.unsplash.com/photo-hero',
                    'items': [
                      {
                        'id': 'oi-10',
                        'itemId': body['primaryItemId'] ?? 'piece-1',
                        'name': 'Primary Silk Saree',
                        'category': 'Sarees',
                        'price': 45000.0,
                        'imageUrl': 'https://images.unsplash.com/photo-hero',
                        'position': 'top',
                      },
                      {
                        'id': 'oi-11',
                        'itemId': 'piece-2',
                        'name': 'Gold Choker',
                        'category': 'Jewelry',
                        'price': 20000.0,
                        'imageUrl': 'https://images.unsplash.com/photo-jewelry',
                        'position': 'accessory',
                      },
                    ],
                  },
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/lookbooks/lb-1') && options.method == 'PUT') {
              final body = options.data as Map<String, dynamic>;
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 200,
                  data: {
                    'id': 'lb-1',
                    'name': body['name'] ?? 'Updated Lookbook Name',
                    'occasion': body['occasion'] ?? 'Bridal Heirloom',
                    'totalPrice': 79000.0,
                    'styleNotes': body['styleNotes'] ?? 'Updated notes',
                    'heroImageUrl': 'https://images.unsplash.com/photo-1',
                    'items': [],
                  },
                ),
              );
              return;
            }

            if (options.path.contains('/catalog/lookbooks/lb-1') && options.method == 'DELETE') {
              handler.resolve(
                Response(
                  requestOptions: options,
                  statusCode: 204,
                  data: null,
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
    test('updateStatus patches the status route and returns the server row', () async {
      final repo = ApiCatalogProductRepository(
        dio,
        organizationId: () => 'org-123',
      );

      final updated = await repo.updateStatus(
        'prod-1',
        CatalogItemStatus.soldOut,
      );

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items/prod-1/status');
      expect(req['method'], 'PATCH');
      expect((req['data'] as Map)['status'], 'sold_out');
      // The row drawn is the server's, so the status and the `isAvailable` derived from it
      // are the API's rather than the client's guess.
      expect(updated.status, CatalogItemStatus.soldOut);
      expect(updated.isAvailable, isFalse);
    });

    test('requestSupply posts the piece as a sourcing ticket', () async {
      final repo = ApiCatalogProductRepository(
        dio,
        organizationId: () => 'org-123',
      );

      final piece = (await repo.fetchProduct('prod-1'))!;
      recordedRequests.clear();

      final id = await repo.requestSupply(piece: piece);

      expect(id, 'src-9');
      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/sourcing');
      expect(req['method'], 'POST');

      final data = req['data'] as Map;
      expect(data['category'], 'Sarees');
      expect(data['color'], 'Crimson');
      expect(data['targetPrice'], 45000.0);
      expect(data['quantityNeeded'], 1);
      expect(data['urgency'], 'medium');
    });

    test('a mutation with no organization is refused rather than toasted', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => null);

      // A read can answer with an empty page, but a mutation has nowhere to go: it must
      // fail loudly instead of letting the screen claim the floor was changed.
      await expectLater(
        repo.updateStatus('prod-1', CatalogItemStatus.onHold),
        throwsA(isA<StateError>()),
      );
      expect(recordedRequests, isEmpty);
    });

    test('uploadImage posts multipart image bytes and returns metadata', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final result = await repo.uploadImage(
        bytes: [1, 2, 3, 4, 5],
        fileName: 'silk_saree.jpg',
      );

      expect(result.id, 'img-101');
      expect(result.url, contains('/images/img-101/file'));
      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/images/upload');
      expect(req['method'], 'POST');
    });

    test('analyzeImage calls vision AI endpoint with imageRefId', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final analysis = await repo.analyzeImage(
        imageRefId: 'img-101',
        fileNameHint: 'emerald_lehenga.jpg',
      );

      expect(analysis.category, 'Lehengas');
      expect(analysis.detectedColor, 'Emerald Green');
      expect(analysis.colorHex, '#2F6B52');
      expect(analysis.confidenceScore, 0.95);
      expect(analysis.visualAttributes, contains('Emerald Green'));
      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/analyze-image');
      expect(req['method'], 'POST');
    });

    test('createProduct posts new inventory item and returns parsed product', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const payload = CreateProductPayload(
        name: 'Emerald Zari Lehenga',
        category: 'Lehengas',
        color: 'Emerald Green',
        sizes: ['38', '40'],
        price: 85000.0,
        cost: 35000.0,
        quantity: 3,
        fabric: 'Raw Silk',
        style: 'Bridal Regal',
      );

      final created = await repo.createProduct(payload);

      expect(created.id, 'prod-created-1');
      expect(created.name, 'Emerald Zari Lehenga');
      expect(created.price, 85000.0);
      expect(created.status, CatalogItemStatus.available);
      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items');
      expect(req['method'], 'POST');
    });

    test('updateProduct puts item payload and returns updated product', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const payload = UpdateProductPayload(
        name: 'Royal Banarasi Silk Saree',
        price: 48000.0,
      );

      final updated = await repo.updateProduct('prod-1', payload);

      expect(updated.id, 'prod-1');
      expect(updated.name, 'Royal Banarasi Silk Saree');
      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items/prod-1');
      expect(req['method'], 'PUT');
    });

    test('deleteProduct sends delete request', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      await repo.deleteProduct('prod-1');

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items/prod-1');
      expect(req['method'], 'DELETE');
    });

    test('getLookbooks fetches lookbooks list and filters by occasion/query', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final allLookbooks = await repo.getLookbooks();
      expect(allLookbooks, hasLength(2));
      expect(allLookbooks.first.name, 'Emerald Heritage Sangeet Look');
      expect(allLookbooks.first.items, hasLength(1));

      // Filter by occasion
      final filteredOccasion = await repo.getLookbooks(occasion: 'Bridal Heirloom');
      expect(filteredOccasion, hasLength(1));
      expect(filteredOccasion.single.id, 'lb-2');

      // Filter by query
      final filteredQuery = await repo.getLookbooks(query: 'Emerald');
      expect(filteredQuery, hasLength(1));
      expect(filteredQuery.single.id, 'lb-1');
    });

    test('composeOutfit sends payload to compose endpoint and returns composed outfit', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const payload = ComposeOutfitPayload(
        name: 'Curated Sangeet Look',
        primaryItemId: 'piece-1',
        occasion: 'Sangeet & Reception',
        notes: 'Styled with antique jewelry',
      );

      final composed = await repo.composeOutfit(payload);
      expect(composed.id, 'lb-composed-1');
      expect(composed.items, hasLength(2));
      expect(composed.totalPrice, 65000.0);

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/lookbooks/compose');
      expect(req['method'], 'POST');
    });

    test('updateLookbook sends put request and returns updated lookbook', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const payload = UpdateLookbookPayload(
        name: 'Royal Heritage Edition',
        occasion: 'Bridal Heirloom',
      );

      final updated = await repo.updateLookbook('lb-1', payload);
      expect(updated.id, 'lb-1');
      expect(updated.name, 'Royal Heritage Edition');

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/lookbooks/lb-1');
      expect(req['method'], 'PUT');
    });

    test('deleteLookbook sends delete request', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      await repo.deleteLookbook('lb-1');

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/lookbooks/lb-1');
      expect(req['method'], 'DELETE');
    });

    test('getSourcingRequests lists tickets with optional filters', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final all = await repo.getSourcingRequests();
      expect(all, hasLength(2));
      expect(all.first.id, 'src-1');
      expect(all.first.clientName, 'Mrs. Radhika Merchant');

      final pending = await repo.getSourcingRequests(status: 'pending');
      expect(pending, hasLength(1));
      expect(pending.single.id, 'src-1');

      expect(recordedRequests, hasLength(2));
      expect(recordedRequests.first['path'], '/api/v1/orgs/org-123/catalog/sourcing');
      expect(recordedRequests.last['queryParameters'], containsPair('status', 'pending'));
    });

    test('createSourcingRequest posts bespoke commission and returns created ticket', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const payload = CreateSourcingRequestPayload(
        clientName: 'Devraj Rajput',
        category: 'Sherwanis',
        color: 'Ivory Cream',
        description: 'Raw silk tailored sherwani',
        supplierId: 'sup-2',
        estimatedCost: 650.0,
        targetPrice: 1450.0,
        urgency: 'high',
      );

      final created = await repo.createSourcingRequest(payload);
      expect(created.id, 'src-created-1');
      expect(created.clientName, 'Devraj Rajput');
      expect(created.status, SourcingStatus.pending);

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/sourcing');
      expect(req['method'], 'POST');
    });

    test('updateSourcingStatus sends patch request with status', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final updated = await repo.updateSourcingStatus('src-1', SourcingStatus.approved, notes: 'Quote confirmed');
      expect(updated.id, 'src-1');
      expect(updated.status, SourcingStatus.approved);

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/sourcing/src-1/status');
      expect(req['method'], 'PATCH');
      expect(req['data'], containsPair('status', 'approved'));
    });

    test('getSuppliers returns list of partner craft ateliers', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final suppliers = await repo.getSuppliers();
      expect(suppliers, hasLength(2));
      expect(suppliers.first.id, 'sup-1');
      expect(suppliers.first.name, 'Varanasi Silk Works');

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/suppliers');
      expect(req['method'], 'GET');
    });

    test('recordSale posts sale data and parses CatalogSaleReceipt', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const payload = RecordSalePayload(
        quantity: 2,
        unitPrice: 42000.0,
        customerId: 'cust-1',
        note: 'Special bridal discount',
      );

      final receipt = await repo.recordSale(itemId: 'prod-1', payload: payload);
      expect(receipt.itemId, 'prod-1');
      expect(receipt.quantitySold, 2);
      expect(receipt.unitPrice, 42000.0);
      expect(receipt.totalAmount, 84000.0);
      expect(receipt.remainingStock, 4);
      expect(receipt.ledgerEntryId, 'tx-ledger-123');

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items/prod-1/sales');
      expect(req['method'], 'POST');
      expect(req['data'], containsPair('quantity', 2));
      expect(req['data'], containsPair('unitPrice', 42000.0));
      expect(req['data'], containsPair('customerId', 'cust-1'));
    });

    test('adjustStock puts updated quantity and status', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final updated = await repo.adjustStock(
        itemId: 'prod-1',
        quantity: 0,
        status: CatalogItemStatus.soldOut,
      );

      expect(updated.id, 'prod-1');
      expect(updated.quantity, 0);
      expect(updated.status, CatalogItemStatus.soldOut);

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/items/prod-1');
      expect(req['method'], 'PUT');
      expect(req['data'], containsPair('quantity', 0));
      expect(req['data'], containsPair('status', 'sold_out'));
    });

    test('fetchTags retrieves and deserializes boutique tags', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      final tags = await repo.fetchTags();
      expect(tags, hasLength(2));
      expect(tags[0].slug, 'bridal');
      expect(tags[0].label, 'Bridal');
      expect(tags[0].colorHex, '#D4AF37');
      expect(tags[1].slug, 'festive');

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['path'], '/api/v1/orgs/org-123/catalog/tags');
      expect(req['method'], 'GET');
    });

    test('fetchPage sends tagIds in query body', () async {
      final repo = ApiCatalogProductRepository(dio, organizationId: () => 'org-123');

      const query = CatalogProductQuery(tagIds: {'bridal', 'festive'});
      await repo.fetchPage(page: 0, pageSize: 8, query: query);

      expect(recordedRequests, hasLength(1));
      final req = recordedRequests.single;
      expect(req['data'], containsPair('tagIds', containsAll(['bridal', 'festive'])));
    });
  });
}
