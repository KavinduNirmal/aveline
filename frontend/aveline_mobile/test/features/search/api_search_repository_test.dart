import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/network/org_context.dart';
import 'package:aveline_mobile/features/search/data/api_search_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  Object? body;
  int statusCode = 200;
  String? lastPath;
  Map<String, dynamic>? lastQueryParams;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastPath = options.path;
    lastQueryParams = options.queryParameters;
    if (statusCode != 200) {
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

void main() {
  late _StubAdapter adapter;
  late Dio dio;

  setUp(() {
    adapter = _StubAdapter({
      'items': [
        {
          'type': 'catalogItem',
          'id': 'p1',
          'title': 'Silk Scarf',
          'subtitle': 'SKU: SS-01 · 8,500 LKR',
          'score': 1.0,
          'href': '/catalog/p1',
        },
        {
          'type': 'customer',
          'id': 'c1',
          'title': 'Nimali Silva',
          'subtitle': '+94771234567',
          'score': 0.9,
          'href': '/customers/c1',
        },
      ],
      'total': 2,
      'page': 1,
      'pageSize': 20,
    });
    dio = Dio()..httpClientAdapter = adapter;
  });

  group('ApiSearchRepository', () {
    test('search returns parsed search items for query >= 2 chars', () async {
      final repo = ApiSearchRepository(dio, organizationId: () => 'org-1');
      final results = await repo.search('silk');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/search');
      expect(adapter.lastQueryParams?['q'], 'silk');
      expect(results, hasLength(2));
      expect(results.first.title, 'Silk Scarf');
      expect(results.first.type, 'catalogItem');
      expect(results.first.href, '/catalog/p1');
      expect(results.last.title, 'Nimali Silva');
      expect(results.last.type, 'customer');
    });

    test('search returns empty list without calling API when query < 2 chars', () async {
      final repo = ApiSearchRepository(dio, organizationId: () => 'org-1');
      final results = await repo.search('a');

      expect(results, isEmpty);
      expect(adapter.lastPath, isNull);
    });

    test('throws OrgContextUnavailable when organizationId is null or empty', () async {
      final repo = ApiSearchRepository(dio, organizationId: () => null);
      expect(() => repo.search('silk'), throwsA(isA<OrgContextUnavailable>()));
    });
  });
}
