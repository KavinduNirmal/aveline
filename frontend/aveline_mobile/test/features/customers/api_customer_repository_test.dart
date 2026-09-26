import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/customers/data/api_customer_repository.dart';
import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/home/data/api_customer_tenant_repository.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  Object? body;
  Map<String, Object?> routeResponses = {};
  int statusCode = 200;
  String? lastPath;
  Object? lastBody;
  Map<String, dynamic>? lastQueryParameters;
  Map<String, dynamic>? lastHeaders;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastPath = options.path;
    lastBody = options.data;
    lastQueryParameters = options.queryParameters;
    lastHeaders = options.headers;
    if (statusCode != 200 && statusCode != 201) {
      return ResponseBody.fromString('{}', statusCode);
    }
    final responseObj = routeResponses[options.path] ?? body;
    return ResponseBody.fromString(
      jsonEncode(responseObj),
      statusCode,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

Map<String, Object?> _bookItem({
  String id = '11111111-1111-1111-1111-111111111111',
  String? fullName = 'Eleanor Vane',
  String? level = 'level3',
  String status = 'returning',
}) =>
    {
      'customerId': id,
      'fullName': fullName,
      'nickname': null,
      'level': level,
      'status': status,
      'phoneNumber': '+94771234567',
      'lastVisitAtUtc': '2026-09-10T09:00:00Z',
      'visitCount': 3,
      'totalSpent': 25000.0,
    };

void main() {
  late _StubAdapter adapter;
  late Dio dio;
  late ApiCustomerTenantRepository tenantRepository;
  late ApiCustomerRepository repository;

  setUp(() {
    adapter = _StubAdapter({'items': [], 'total': 0, 'page': 1, 'pageSize': 200});
    dio = Dio()..httpClientAdapter = adapter;
    tenantRepository = ApiCustomerTenantRepository(dio, organizationId: 'org-1');
    repository = ApiCustomerRepository(dio, organizationId: () => 'org-1');
  });

  group('ApiCustomerRepository.fetchBook', () {
    test('maps book items into alphabet-sectioned CustomerBook', () async {
      adapter.body = {
        'items': [
          _bookItem(id: 'b', fullName: 'Bianca Costa', level: 'level2'),
          _bookItem(id: 'a', fullName: 'Anjali Perera', level: 'vip'),
          _bookItem(id: 'c', fullName: null, level: null),
        ],
        'total': 3,
        'page': 1,
        'pageSize': 200,
      };

      final book = await repository.fetchBook();

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers');
      expect(book.letters, ['A', 'B', '#']);
      expect(book.sections.first.customers.first.displayName, 'Anjali Perera');
      expect(book.sections.first.customers.first.level, CustomerLevel.vip);
      expect(book.total, 3);
    });

    test('returns empty book when organizationId is empty', () async {
      final unattachedRepo = ApiCustomerRepository(dio, organizationId: () => null);
      final book = await unattachedRepo.fetchBook();
      expect(book.isEmpty, isTrue);
    });

    test('passes the search text and level filter through', () async {
      await repository.fetchBook(
        query: const CustomerQuery(search: 'Anjali', level: CustomerLevel.vip),
      );

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers');
      expect(adapter.lastQueryParameters?['search'], 'Anjali');
      expect(adapter.lastQueryParameters?['level'], 'vip');
    });
  });

  group('ApiCustomerRepository.fetchCustomer', () {
    test('fetches detail, consent, and interactions and combines into CustomerDetail', () async {
      adapter.routeResponses = {
        '/api/v1/orgs/org-1/customers/cus-1': {
          'customerId': 'cus-1',
          'fullName': 'Chamari Silva',
          'nickname': 'Chami',
          'phoneNumber': '+94776412098',
          'email': 'chamari@example.com',
          'level': 'vip',
          'status': 'vip',
          'totalSpent': 512000.0,
          'visitCount': 14,
          'lastVisitAtUtc': '2026-09-23T10:00:00Z',
          'createdAtUtc': '2026-01-15T08:00:00Z',
          'tags': ['bridal', 'atelier-pick'],
        },
        '/api/v1/orgs/org-1/customers/cus-1/consent': {
          'id': 'consent-1',
          'consentStatus': 'granted',
          'consentGrantedAt': '2026-08-01T12:00:00Z',
          'consentRevokedAt': null,
        },
        '/api/v1/orgs/org-1/customers/cus-1/interactions': {
          'items': [
            {
              'interactionId': 'int-1',
              'occurredAtUtc': '2026-09-23T10:00:00Z',
              'channel': 'whatsapp',
              'direction': 'inbound',
              'note': 'Wants to see bridal sarees.',
              'countedAsVisit': false,
            }
          ],
          'total': 1,
          'page': 1,
          'pageSize': 50,
        },
      };

      final detail = await repository.fetchCustomer('cus-1');

      expect(detail, isNotNull);
      expect(detail!.customer.fullName, 'Chamari Silva');
      expect(detail.customer.nickname, 'Chami');
      expect(detail.customer.level, CustomerLevel.vip);
      expect(detail.customer.tags, contains('bridal'));
      expect(detail.consent.status, ConsentStatus.granted);
      expect(detail.interactions, hasLength(1));
      expect(detail.interactions.first.channel, InteractionChannel.whatsapp);
      expect(detail.interactions.first.messageContent, 'Wants to see bridal sarees.');
    });

    test('returns null when server responds 404', () async {
      adapter.statusCode = 404;
      final detail = await repository.fetchCustomer('nonexistent');
      expect(detail, isNull);
    });

    test('reads memories, events and preferences into the profile', () async {
      adapter.routeResponses = {
        '/api/v1/orgs/org-1/customers/sub-1': {
          'customerId': 'sub-1',
          'fullName': 'Chamari Silva',
          'nickname': 'Chami',
          'phoneNumber': '+94711223344',
          'email': 'chami@example.com',
          'level': 'vip',
          'status': 'vip',
          'totalSpent': 150000.0,
          'visitCount': 12,
          'lastVisitAtUtc': '2026-09-12T14:30:00Z',
          'createdAtUtc': '2025-05-01T08:00:00Z',
          'tags': ['silk-lover', 'formal'],
          'preferences': [
            {
              'id': 'p1',
              'preferenceKey': 'Fabric',
              'preferenceValue': 'Raw silk',
              'isExplicit': true,
              'confidence': 1.0,
            }
          ],
        },
        '/api/v1/orgs/org-1/customers/sub-1/consent': {
          'id': 'consent-1',
          'status': 'granted',
          'grantedAtUtc': '2026-08-03T10:00:00Z',
          'revokedAtUtc': null,
        },
        '/api/v1/orgs/org-1/customers/sub-1/memories': [
          {
            'id': 'm1',
            'customerId': 'sub-1',
            'content': 'Prefers dark jewel tones for evening wear.',
            'category': 'preference',
            'source': 'conversation',
            'isExplicit': true,
            'confidence': 0.95,
            'createdAtUtc': '2026-09-01T12:00:00Z',
          }
        ],
        '/api/v1/orgs/org-1/customers/sub-1/events': [
          {
            'id': 'e1',
            'eventType': 'wedding',
            'eventDate': '2026-12-15T00:00:00Z',
            'description': 'Wedding reception',
            'isActive': true,
          }
        ],
        '/api/v1/orgs/org-1/customers/sub-1/interactions': {
          'items': [
            {
              'interactionId': 'i1',
              'channel': 'in_person',
              'direction': 'inbound',
              'occurredAtUtc': '2026-09-12T14:30:00Z',
              'note': 'Walked in to discuss wedding attire options.',
              'countedAsVisit': true,
            }
          ],
          'total': 1,
          'page': 1,
          'pageSize': 50,
        },
      };

      final detail = await repository.fetchCustomer('sub-1');

      expect(detail, isNotNull);
      expect(detail!.consent.status, ConsentStatus.granted);
      expect(detail.preferences, hasLength(1));
      expect(detail.preferences.first.key, 'Fabric');
      expect(detail.memories, hasLength(1));
      expect(detail.memories.first.content, contains('dark jewel tones'));
      expect(detail.events, hasLength(1));
      expect(detail.events.first.type, CustomerEventType.wedding);
      expect(detail.interactions, hasLength(1));
      expect(detail.interactions.first.channel, InteractionChannel.inPerson);
    });
  });

  group('ApiCustomerRepository mutations', () {
    test('recordVisit posts an inbound in-person interaction with an idempotency key', () async {
      await repository.recordVisit('cus-1');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers/cus-1/interactions');
      final body = adapter.lastBody as Map;
      expect(body['channel'], 'in_person');
      expect(body['direction'], 'inbound');
      expect(adapter.lastHeaders?['Idempotency-Key'], isNotEmpty);
    });

    test('recomputeTier posts to the status endpoint and returns the new status', () async {
      adapter.routeResponses = {
        '/api/v1/orgs/org-1/customers/cus-1/status': {
          'customerId': 'cus-1',
          'status': 'vip',
        },
      };

      final status = await repository.recomputeTier('cus-1');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers/cus-1/status');
      expect(status, 'vip');
    });
  });

  group('ApiCustomerTenantRepository.fetchBook', () {
    test('maps the page envelope into lettered sections', () async {
      adapter.body = {
        'items': [
          _bookItem(id: 'b', fullName: 'Bella Perera', level: 'level1'),
          _bookItem(id: 'a', fullName: 'Anna Silva', level: 'vip'),
        ],
        'total': 2,
        'page': 1,
        'pageSize': 200,
      };

      final book = await tenantRepository.fetchBook();

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers');
      expect(book.letters, ['A', 'B']);
      expect(book.sections.first.customers.single.fullName, 'Anna Silva');
      expect(book.sections.first.customers.single.level, CustomerLevel.vip);
      expect(book.total, 2);
    });

    test('files a client the shop has no name for under #', () async {
      adapter.body = {
        'items': [_bookItem(id: 'a', fullName: null, level: null)],
        'total': 1,
        'page': 1,
        'pageSize': 200,
      };

      final book = await tenantRepository.fetchBook();

      expect(book.letters, ['#']);
    });
  });

  group('ApiCustomerTenantRepository.createWalkIn', () {
    test('returns the server id and hides an ungraded badge', () async {
      adapter.statusCode = 201;
      adapter.body = {
        'customerId': 'abc-123',
        'fullName': 'Maria Silva',
        'level': null,
        'status': 'new',
        'consentStatus': 'pending',
        'duplicateOfCustomerId': null,
      };

      final client = await tenantRepository.createWalkIn('Maria Silva');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers');
      expect(client.id, 'abc-123');
      // No grade on the wire means no badge, not a default of Level 1.
      expect(client.tier, isNull);
      expect(client.name, 'Maria Silva');
    });

    test('reports a duplicate rather than claiming a second client', () async {
      adapter.statusCode = 200;
      adapter.body = {
        'customerId': 'existing-1',
        'fullName': 'Maria Silva',
        'level': 'level2',
        'status': 'returning',
        'consentStatus': 'pending',
        'duplicateOfCustomerId': 'existing-1',
      };

      final client = await tenantRepository.createWalkIn('Maria Silva');

      expect(client.id, 'existing-1');
      expect(client.activity, 'Already on file.');
    });
  });

  group('ApiCustomerTenantRepository.recordVisit', () {
    test('posts an inbound in-person interaction with an idempotency key', () async {
      adapter.statusCode = 201;
      adapter.body = {
        'visitId': 'v1',
        'customerId': 'c1',
        'visitCountAfter': 1,
        'blossomsCharged': 0,
      };

      await tenantRepository.recordVisit('c1');

      expect(
        adapter.lastPath,
        '/api/v1/orgs/org-1/customers/c1/interactions',
      );
      final body = adapter.lastBody as Map;
      expect(body['channel'], 'in_person');
      expect(body['direction'], 'inbound');
    });
  });

  group('ApiCustomerTenantRepository.fetchHighlights', () {
    test('maps the wire level onto the tier and keeps no activity flag', () async {
      adapter.body = {
        'items': [
          {
            'customerId': 'c1',
            'name': 'Eleanor Vane',
            'level': 'vip',
            'activity': 'Visited the boutique today.',
            'lastActivityAtUtc': '2026-09-18T09:00:00Z',
          },
          {
            'customerId': 'c2',
            'name': 'Maria Silva',
            'level': null,
            'activity': 'Added at the counter.',
            'lastActivityAtUtc': '2026-09-18T10:00:00Z',
          },
        ],
      };

      final highlights = await tenantRepository.fetchHighlights();

      expect(highlights, hasLength(2));
      expect(highlights.first.tier, ClientTier.vip);
      expect(highlights.last.tier, isNull);
      expect(highlights.first.activity, 'Visited the boutique today.');
    });
  });
}
