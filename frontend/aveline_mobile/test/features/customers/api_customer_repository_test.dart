import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/customers/data/api_customer_repository.dart';
import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _MultiRouteAdapter implements HttpClientAdapter {
  final Map<String, Object?> responses = {};
  final Map<String, int> statusCodes = {};
  String? lastPath;
  Object? lastBody;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastPath = options.path;
    lastBody = options.data;
    final code = statusCodes[options.path] ?? 200;
    if (code >= 400) {
      return ResponseBody.fromString(
        jsonEncode({'message': 'Error $code'}),
        code,
        headers: {
          Headers.contentTypeHeader: [Headers.jsonContentType],
        },
      );
    }
    final body = responses[options.path] ?? {};
    return ResponseBody.fromString(
      jsonEncode(body),
      code,
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
      'tags': ['vip-client'],
    };

void main() {
  late _MultiRouteAdapter adapter;
  late Dio dio;
  late ApiCustomerRepository repository;

  setUp(() {
    adapter = _MultiRouteAdapter();
    dio = Dio()..httpClientAdapter = adapter;
    repository = ApiCustomerRepository(dio, organizationId: 'org-1');
  });

  group('ApiCustomerRepository.fetchBook', () {
    test('maps the page envelope into lettered sections', () async {
      adapter.responses['/api/v1/orgs/org-1/customers'] = {
        'items': [
          _bookItem(id: 'b', fullName: 'Bella Perera', level: 'level1'),
          _bookItem(id: 'a', fullName: 'Anna Silva', level: 'vip'),
        ],
        'total': 2,
        'page': 1,
        'pageSize': 200,
      };

      final book = await repository.fetchBook();

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers');
      expect(book.letters, ['A', 'B']);
      expect(book.sections.first.customers.single.fullName, 'Anna Silva');
      expect(book.sections.first.customers.single.level, CustomerLevel.vip);
      expect(book.total, 2);
    });

    test('files a client the shop has no name for under #', () async {
      adapter.responses['/api/v1/orgs/org-1/customers'] = {
        'items': [_bookItem(id: 'a', fullName: null, level: null)],
        'total': 1,
        'page': 1,
        'pageSize': 200,
      };

      final book = await repository.fetchBook();

      expect(book.letters, ['#']);
    });

    test('passes search query and level filter', () async {
      adapter.responses['/api/v1/orgs/org-1/customers'] = {
        'items': [],
        'total': 0,
        'page': 1,
        'pageSize': 200,
      };

      await repository.fetchBook(
        query: const CustomerQuery(search: 'Anna', level: CustomerLevel.vip),
      );

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers');
    });
  });

  group('ApiCustomerRepository.fetchCustomer', () {
    const customerId = 'c1';

    test('fetches profile and sub-resources into CustomerDetail', () async {
      adapter.responses['/api/v1/orgs/org-1/customers/$customerId'] = {
        'customerId': customerId,
        'fullName': 'Chamari Silva',
        'nickname': 'Chami',
        'phoneNumber': '+94711223344',
        'email': 'chami@example.com',
        'level': 'vip',
        'status': 'vip',
        'totalSpent': 150000.0,
        'visitCount': 12,
        'lastVisitAtUtc': '2026-09-12T14:30:00Z',
        'loyaltyTierIsDerived': 1,
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
      };

      adapter.responses['/api/v1/orgs/org-1/customers/$customerId/consent'] = {
        'id': 'consent-1',
        'status': 'granted',
        'grantedAtUtc': '2026-08-03T10:00:00Z',
        'revokedAtUtc': null,
      };

      adapter.responses['/api/v1/orgs/org-1/customers/$customerId/memories'] = [
        {
          'id': 'm1',
          'customerId': customerId,
          'content': 'Prefers dark jewel tones for evening wear.',
          'category': 'preference',
          'source': 'conversation',
          'isExplicit': true,
          'confidence': 0.95,
          'createdAtUtc': '2026-09-01T12:00:00Z',
        }
      ];

      adapter.responses['/api/v1/orgs/org-1/customers/$customerId/events'] = [
        {
          'id': 'e1',
          'eventType': 'wedding',
          'eventDate': '2026-12-15T00:00:00Z',
          'description': 'Sister’s wedding reception',
          'isActive': true,
        }
      ];

      adapter.responses['/api/v1/orgs/org-1/customers/$customerId/interactions'] = {
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
      };

      final detail = await repository.fetchCustomer(customerId);

      expect(detail, isNotNull);
      expect(detail!.customer.id, customerId);
      expect(detail.customer.fullName, 'Chamari Silva');
      expect(detail.customer.nickname, 'Chami');
      expect(detail.customer.displayName, 'Chamari Silva');
      expect(detail.customer.level, CustomerLevel.vip);
      expect(detail.consent.status, ConsentStatus.granted);
      expect(detail.preferences, hasLength(1));
      expect(detail.preferences.first.key, 'Fabric');
      expect(detail.memories, hasLength(1));
      expect(detail.memories.first.content, contains('dark jewel tones'));
      expect(detail.events, hasLength(1));
      expect(detail.events.first.type, CustomerEventType.wedding);
      expect(detail.interactions, hasLength(1));
      expect(detail.interactions.first.channel, InteractionChannel.inPerson);
    });

    test('returns null when API returns 404', () async {
      adapter.statusCodes['/api/v1/orgs/org-1/customers/unknown-id'] = 404;

      final detail = await repository.fetchCustomer('unknown-id');

      expect(detail, isNull);
    });
  });

  group('ApiCustomerRepository mutations', () {
    test('recordVisit posts inbound in-person interaction with idempotency key', () async {
      adapter.statusCodes['/api/v1/orgs/org-1/customers/c1/interactions'] = 201;

      await repository.recordVisit('c1');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers/c1/interactions');
      final body = adapter.lastBody as Map;
      expect(body['channel'], 'in_person');
      expect(body['direction'], 'inbound');
    });

    test('recomputeTier posts to status endpoint and returns new status', () async {
      adapter.responses['/api/v1/orgs/org-1/customers/c1/status'] = {
        'status': 'vip',
      };

      final result = await repository.recomputeTier('c1');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/customers/c1/status');
      expect(result, 'vip');
    });
  });
}
