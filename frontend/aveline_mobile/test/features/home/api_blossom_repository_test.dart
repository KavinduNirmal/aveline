import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/home/data/api_blossom_repository.dart';
import 'package:aveline_mobile/features/home/data/blossom_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Serves `GET /orgs/{id}/blossoms/balance` from a canned body, or a status code.
class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  Object? body;
  int statusCode = 200;
  String? lastPath;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    lastPath = options.path;
    if (statusCode != 200) {
      return ResponseBody.fromString('{}', statusCode);
    }
    return ResponseBody.fromString(
      jsonEncode(body),
      200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

Map<String, Object?> _balanceBody({
  Object? monthlyBlossomLimit = 150.0,
  Object? blossomGranted = 50.0,
  Object? blossomAdjusted = 0.0,
  Object? blossomUsed = 168.5,
  Object? blossomRemaining = 31.5,
  Object? lowBalanceThresholdPercent = 20.0,
  String periodEnd = '2026-10-01T00:00:00Z',
}) =>
    {
      'organizationId': '11111111-1111-1111-1111-111111111111',
      'periodStart': '2026-09-01T00:00:00Z',
      'periodEnd': periodEnd,
      'periodIsClosed': false,
      'planTier': 'atelier',
      'monthlyBlossomLimit': monthlyBlossomLimit,
      'blossomGranted': blossomGranted,
      'blossomAdjusted': blossomAdjusted,
      'blossomUsed': blossomUsed,
      'blossomRemaining': blossomRemaining,
      'percentUsed': 112.33,
      'lowBalanceThresholdPercent': lowBalanceThresholdPercent,
      'asOf': '2026-09-18T12:00:00Z',
    };

void main() {
  late _StubAdapter adapter;
  late Dio dio;
  late ApiBlossomRepository repository;

  setUp(() {
    adapter = _StubAdapter(_balanceBody());
    dio = Dio()..httpClientAdapter = adapter;
    repository = ApiBlossomRepository(dio);
  });

  group('ApiBlossomRepository', () {
    test('maps the balance projection onto BlossomUsage', () async {
      final usage = await repository.fetchBalance(organizationId: 'org-1');

      expect(adapter.lastPath, '/api/v1/orgs/org-1/blossoms/balance');
      expect(usage.used, 168.5);
      // The allowance is limit + granted - adjusted, which is the rule the
      // backend reconciles against.
      expect(usage.allowance, 200.0);
      // The server's own projection wins over deriving it.
      expect(usage.remaining, 31.5);
      expect(usage.lowBalanceThresholdPercent, 20.0);
      expect(usage.renewsOn, '1 October');
    });

    test('keeps a fractional balance instead of truncating it', () async {
      adapter.body = _balanceBody(
        blossomUsed: 0.1,
        blossomRemaining: 199.9,
        blossomGranted: 0.0,
        monthlyBlossomLimit: 200.0,
      );

      final usage = await repository.fetchBalance(organizationId: 'org-1');

      expect(usage.used, 0.1);
      expect(usage.remaining, 199.9);
      expect(usage.allowance, 200.0);
    });

    test('surfaces a forbidden balance as a typed failure', () async {
      adapter.statusCode = 403;

      // Not a zero balance: the card must distinguish "you may not read this"
      // from "the shop has none left".
      await expectLater(
        repository.fetchBalance(organizationId: 'org-1'),
        throwsA(isA<BlossomBalanceUnavailable>()),
      );
    });

    test('surfaces any other refusal as a typed failure', () async {
      adapter.statusCode = 500;

      await expectLater(
        repository.fetchBalance(organizationId: 'org-1'),
        throwsA(isA<BlossomBalanceUnavailable>()),
      );
    });
  });
}
