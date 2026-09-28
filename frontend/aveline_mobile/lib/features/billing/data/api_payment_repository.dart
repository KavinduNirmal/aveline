import 'package:dio/dio.dart';

import '../domain/top_up.dart';
import 'payment_repository.dart';

/// Speaks the four payment routes over the app's shared [Dio] (plan §9.2).
///
/// The wire is the server's: the client sends a `skuCode` and nothing else, because the price and
/// the Blossom quantity come from the price book. Every refusal becomes a [PaymentUnavailable]
/// carrying a sentence that says whether money moved, since "try again" is the wrong advice for a
/// request that may already have applied.
class ApiPaymentRepository implements PaymentRepository {
  ApiPaymentRepository(this._dio);

  final Dio _dio;

  @override
  Future<List<TopUpPack>> fetchTopUpPacks({
    required String organizationId,
  }) async {
    try {
      final response = await _dio.get<List<dynamic>>(
        '/api/v1/orgs/$organizationId/blossoms/top-up-packs',
      );
      final data = response.data;
      if (data == null) {
        throw const PaymentUnavailable(
          'The top-up packs came back in a shape the app does not understand.',
        );
      }
      return data
          .whereType<Map<dynamic, dynamic>>()
          .map(_toPack)
          .toList(growable: false);
    } on DioException catch (error) {
      throw _describe(error);
    }
  }

  @override
  Future<TopUpCheckout> createTopUpCheckout({
    required String organizationId,
    required String skuCode,
    required String idempotencyKey,
  }) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>(
        '/api/v1/orgs/$organizationId/blossoms/top-ups/checkout',
        data: {'skuCode': skuCode},
        options: Options(headers: {'Idempotency-Key': idempotencyKey}),
      );
      return _toCheckout(_map(response.data));
    } on DioException catch (error) {
      throw _describe(error);
    }
  }

  @override
  Future<PaymentIntent> fetchPaymentIntent({
    required String organizationId,
    required String paymentIntentId,
  }) async {
    try {
      final response = await _dio.get<Map<String, dynamic>>(
        '/api/v1/orgs/$organizationId/payment-intents/$paymentIntentId',
      );
      return _toIntent(_map(response.data));
    } on DioException catch (error) {
      throw _describe(error);
    }
  }

  @override
  Future<PaymentIntent> cancelPaymentIntent({
    required String organizationId,
    required String paymentIntentId,
    required String idempotencyKey,
    String? reason,
  }) async {
    try {
      final response = await _dio.post<Map<String, dynamic>>(
        '/api/v1/orgs/$organizationId/payment-intents/$paymentIntentId/cancel',
        queryParameters: {'reason': reason},
        options: Options(headers: {'Idempotency-Key': idempotencyKey}),
      );
      return _toIntent(_map(response.data));
    } on DioException catch (error) {
      throw _describe(error);
    }
  }

  // ------------------------------------------------------------ wire -> domain

  static Map<dynamic, dynamic> _map(Object? data) {
    if (data is Map<dynamic, dynamic>) {
      return data;
    }
    throw const PaymentUnavailable(
      'The payment service answered in a shape the app does not understand.',
    );
  }

  static TopUpPack _toPack(Map<dynamic, dynamic> data) => TopUpPack(
    skuCode: _text(data['skuCode']) ?? '',
    blossomQuantity: _decimal(data['blossomQuantity']),
    priceLkr: _decimal(data['priceLkr']),
    currency: _text(data['currency']) ?? 'LKR',
  );

  static TopUpCheckout _toCheckout(Map<dynamic, dynamic> data) => TopUpCheckout(
    paymentIntentId: _text(data['paymentIntentId']) ?? '',
    provider: _text(data['provider']) ?? '',
    status: _text(data['status']) ?? '',
    skuCode: _text(data['skuCode']) ?? '',
    blossomQuantity: _decimal(data['blossomQuantity']),
    amountLkr: _decimal(data['amountLkr']),
    currency: _text(data['currency']) ?? 'LKR',
    checkoutUrl: _text(data['checkoutUrl']),
    expiresAt: _date(data['expiresAt']),
  );

  static PaymentIntent _toIntent(Map<dynamic, dynamic> data) => PaymentIntent(
    paymentIntentId: _text(data['paymentIntentId']) ?? '',
    provider: _text(data['provider']) ?? '',
    providerIntentId: _text(data['providerIntentId']) ?? '',
    purpose: _text(data['purpose']) ?? '',
    status: _text(data['status']) ?? '',
    amountLkr: _decimal(data['amountLkr']),
    currency: _text(data['currency']) ?? 'LKR',
    checkoutUrl: _text(data['checkoutUrl']),
    failureCode: _text(data['failureCode']),
    failureMessage: _text(data['failureMessage']),
    createdAt: _date(data['createdAt']) ?? DateTime.now().toUtc(),
    settledAt: _date(data['settledAt']),
    expiresAt: _date(data['expiresAt']),
  );

  static String? _text(Object? value) {
    if (value == null) return null;
    final text = value.toString();
    return text.isEmpty ? null : text;
  }

  static double _decimal(Object? value) {
    if (value is num) return value.toDouble();
    if (value is String) return double.tryParse(value) ?? 0;
    return 0;
  }

  static DateTime? _date(Object? value) {
    if (value is String) return DateTime.tryParse(value)?.toUtc();
    return null;
  }

  // ------------------------------------------------------------ failures

  static PaymentUnavailable _describe(DioException error) {
    final status = error.response?.statusCode;
    final data = error.response?.data;
    final code = data is Map<dynamic, dynamic> ? _text(data['code']) : null;

    switch (code) {
      case 'unknown-sku':
        return PaymentUnavailable(
          'That pack is no longer available. Reload the list and choose another.',
          statusCode: status,
        );
      case 'unpurchasable-sku':
        return PaymentUnavailable(
          'That pack has no price on file and cannot be purchased.',
          statusCode: status,
        );
      case 'idempotency-key-reuse':
        return PaymentUnavailable(
          'This attempt was already used for a different request. Start a new purchase.',
          statusCode: status,
        );
      case 'payment-intent-state':
        return PaymentUnavailable(
          'This checkout can no longer be changed. Nothing further was charged.',
          statusCode: status,
        );
    }

    if (status == 503) {
      return const PaymentUnavailable(
        'The payment provider is temporarily unavailable. Nothing was charged.',
        statusCode: 503,
      );
    }
    if (status == 502) {
      return const PaymentUnavailable(
        'The payment provider could not be reached. Nothing was charged.',
        statusCode: 502,
      );
    }
    if (status == 403) {
      return const PaymentUnavailable(
        'You do not have permission to buy Blossoms for this boutique.',
        statusCode: 403,
      );
    }
    if (status == 401) {
      return const PaymentUnavailable(
        'Your session has expired.',
        statusCode: 401,
      );
    }

    // A transport failure carries no status: the outcome is unknown, so the wording must not
    // promise that nothing happened.
    return PaymentUnavailable(
      status == null
          ? 'The request did not complete. Check your connection; retrying is safe.'
          : 'The payment request failed ($status). Nothing was charged.',
      statusCode: status,
    );
  }
}
