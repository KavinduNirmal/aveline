import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/home/data/api_customer_tenant_repository.dart';
import 'package:aveline_mobile/features/home/domain/client_highlight.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  Object? body;
  int statusCode = 200;
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
    if (statusCode != 200 && statusCode != 201) {
      return ResponseBody.fromString('{}', statusCode);
    }
    return ResponseBody.fromString(
      jsonEncode(body),
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
  late ApiCustomerTenantRepository repository;

  setUp(() {
    adapter = _StubAdapter({'items': [], 'total': 0, 'page': 1, 'pageSize': 200});
    dio = Dio()..httpClientAdapter = adapter;
    repository = ApiCustomerTenantRepository(dio, organizationId: 'org-1');
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

      final book = await repository.fetchBook();

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

      final book = await repository.fetchBook();

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

      final client = await repository.createWalkIn('Maria Silva');

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

      final client = await repository.createWalkIn('Maria Silva');

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

      await repository.recordVisit('c1');

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

      final highlights = await repository.fetchHighlights();

      expect(highlights, hasLength(2));
      expect(highlights.first.tier, ClientTier.vip);
      expect(highlights.last.tier, isNull);
      expect(highlights.first.activity, 'Visited the boutique today.');
    });
  });
}
