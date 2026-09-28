import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/features/billing/data/api_payment_repository.dart';
import 'package:aveline_mobile/features/billing/data/payment_repository.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Serves scripted JSON bodies for the payment routes and records what was sent.
class _StubAdapter implements HttpClientAdapter {
  _StubAdapter(this.body);

  Object? body;
  int statusCode = 200;
  final List<_Recorded> recorded = [];

  _Recorded get last => recorded.last;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    recorded.add(
      _Recorded(
        method: options.method,
        path: options.path,
        headers: Map<String, dynamic>.from(options.headers),
        query: Map<String, dynamic>.from(options.queryParameters),
        data: options.data,
      ),
    );

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

class _Recorded {
  _Recorded({
    required this.method,
    required this.path,
    required this.headers,
    required this.query,
    required this.data,
  });

  final String method;
  final String path;
  final Map<String, dynamic> headers;
  final Map<String, dynamic> query;
  final Object? data;
}

Map<String, Object?> _packBody() => {
  'skuCode': 'blossom_pack_500',
  'blossomQuantity': 500,
  'priceLkr': 2000,
  'currency': 'LKR',
};

Map<String, Object?> _checkoutBody() => {
  'paymentIntentId': '22222222-2222-2222-2222-222222222222',
  'provider': 'mock',
  'status': 'RequiresAction',
  'skuCode': 'blossom_pack_500',
  'blossomQuantity': 500,
  'amountLkr': 2000,
  'currency': 'LKR',
  'checkoutUrl': '/api/v1/dev/mock-checkout/22222222-2222-2222-2222-222222222222',
  'expiresAt': null,
};

Map<String, Object?> _intentBody({String status = 'Succeeded'}) => {
  'paymentIntentId': '22222222-2222-2222-2222-222222222222',
  'provider': 'mock',
  'providerIntentId': 'mock_22222222-2222-2222-2222-222222222222',
  'purpose': 'BlossomTopUp',
  'status': status,
  'amountLkr': 2000,
  'currency': 'LKR',
  'checkoutUrl': '/api/v1/dev/mock-checkout/22222222-2222-2222-2222-222222222222',
  'failureCode': null,
  'failureMessage': null,
  'createdAt': '2026-09-24T00:00:00Z',
  'settledAt': '2026-09-24T00:05:00Z',
  'expiresAt': null,
};

void main() {
  late _StubAdapter adapter;
  late Dio dio;
  late ApiPaymentRepository repository;

  setUp(() {
    adapter = _StubAdapter(_packBody());
    dio = Dio()..httpClientAdapter = adapter;
    repository = ApiPaymentRepository(dio);
  });

  group('ApiPaymentRepository', () {
    test('reads the purchasable packs from the blossoms route', () async {
      adapter.body = [_packBody()];

      final packs = await repository.fetchTopUpPacks(organizationId: 'org-1');

      expect(adapter.last.method, 'GET');
      expect(adapter.last.path, '/api/v1/orgs/org-1/blossoms/top-up-packs');
      expect(packs, hasLength(1));
      expect(packs.first.skuCode, 'blossom_pack_500');
      expect(packs.first.blossomQuantity, 500);
      expect(packs.first.priceLkr, 2000);
      expect(packs.first.currency, 'LKR');
    });

    test('posts only the SKU, with the required Idempotency-Key header', () async {
      adapter.body = _checkoutBody();

      final checkout = await repository.createTopUpCheckout(
        organizationId: 'org-1',
        skuCode: 'blossom_pack_500',
        idempotencyKey: 'key-123',
      );

      expect(adapter.last.method, 'POST');
      expect(adapter.last.path, '/api/v1/orgs/org-1/blossoms/top-ups/checkout');
      // The price is the server's: the client names the SKU and nothing else.
      expect(adapter.last.data, {'skuCode': 'blossom_pack_500'});
      expect(adapter.last.headers['Idempotency-Key'], 'key-123');
      expect(checkout.paymentIntentId, '22222222-2222-2222-2222-222222222222');
      expect(checkout.status, 'RequiresAction');
      expect(checkout.checkoutUrl, contains('/dev/mock-checkout/'));
    });

    test('polls one intent by its own route', () async {
      adapter.body = _intentBody();

      final intent = await repository.fetchPaymentIntent(
        organizationId: 'org-1',
        paymentIntentId: 'intent-1',
      );

      expect(adapter.last.method, 'GET');
      expect(adapter.last.path, '/api/v1/orgs/org-1/payment-intents/intent-1');
      expect(intent.status, 'Succeeded');
      expect(intent.isTerminal, isTrue);
    });

    test('cancels with the reason as a query value and its own key', () async {
      adapter.body = _intentBody(status: 'Cancelled');

      final intent = await repository.cancelPaymentIntent(
        organizationId: 'org-1',
        paymentIntentId: 'intent-1',
        idempotencyKey: 'key-cancel',
        reason: 'Changed my mind.',
      );

      expect(adapter.last.method, 'POST');
      expect(adapter.last.path, '/api/v1/orgs/org-1/payment-intents/intent-1/cancel');
      expect(adapter.last.query['reason'], 'Changed my mind.');
      expect(adapter.last.headers['Idempotency-Key'], 'key-cancel');
      expect(intent.status, 'Cancelled');
    });

    test('surfaces a refusal as a typed failure instead of an empty catalogue', () async {
      adapter.statusCode = 403;

      await expectLater(
        repository.fetchTopUpPacks(organizationId: 'org-1'),
        throwsA(isA<PaymentUnavailable>()),
      );
    });
  });
}
