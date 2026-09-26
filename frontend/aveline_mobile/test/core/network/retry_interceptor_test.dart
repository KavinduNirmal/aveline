import 'dart:typed_data';

import 'package:aveline_mobile/core/network/retry_interceptor.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _FailingAdapter implements HttpClientAdapter {
  int attempts = 0;
  int failUntilAttempt = 2;
  int failStatusCode = 503;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    attempts++;
    if (attempts <= failUntilAttempt) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'Temporary network drop',
      );
    }
    return ResponseBody.fromString(
      '{"status":"ok"}',
      200,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  group('RetryInterceptor', () {
    test('retries idempotent GET on transient failure and succeeds', () async {
      final adapter = _FailingAdapter();
      final dio = Dio(BaseOptions(baseUrl: 'https://api.example.com'))
        ..httpClientAdapter = adapter;

      final interceptor = RetryInterceptor(
        maxRetries: 3,
        initialDelay: const Duration(milliseconds: 10),
      )..attachTo(dio);

      dio.interceptors.add(interceptor);

      final response = await dio.get<Map<String, dynamic>>('/test');

      expect(response.statusCode, 200);
      expect(adapter.attempts, 3); // 2 failures + 1 success
    });

    test('does not retry non-idempotent POST requests', () async {
      final adapter = _FailingAdapter();
      final dio = Dio(BaseOptions(baseUrl: 'https://api.example.com'))
        ..httpClientAdapter = adapter;

      final interceptor = RetryInterceptor(
        maxRetries: 3,
        initialDelay: const Duration(milliseconds: 10),
      )..attachTo(dio);

      dio.interceptors.add(interceptor);

      await expectLater(
        dio.post<Map<String, dynamic>>('/test', data: {'a': 1}),
        throwsA(isA<DioException>()),
      );

      expect(adapter.attempts, 1); // No retries for POST
    });
  });
}
