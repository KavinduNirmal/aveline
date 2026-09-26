import 'dart:math';
import 'package:dio/dio.dart';

/// Intercepts failed idempotent requests and retries with jittered exponential backoff.
class RetryInterceptor extends Interceptor {
  RetryInterceptor({
    this.maxRetries = 3,
    this.initialDelay = const Duration(milliseconds: 500),
  });

  final int maxRetries;
  final Duration initialDelay;

  static const String _retryCountKey = 'network_retry_count';
  Dio? _dio;

  /// Binds the owning [Dio] instance so retried requests preserve adapter and options.
  void attachTo(Dio dio) => _dio = dio;

  @override
  Future<void> onError(DioException err, ErrorInterceptorHandler handler) async {
    final request = err.requestOptions;
    final statusCode = err.response?.statusCode;
    final dio = _dio;

    // Only retry idempotent GET requests
    final isGet = request.method.toUpperCase() == 'GET';

    // Only retry transient network errors or 5xx server errors
    final isTransient = err.type == DioExceptionType.connectionTimeout ||
        err.type == DioExceptionType.receiveTimeout ||
        err.type == DioExceptionType.connectionError ||
        (statusCode != null && statusCode >= 500 && statusCode <= 599);

    final currentRetry = (request.extra[_retryCountKey] as int?) ?? 0;

    // Never retry non-idempotent methods, non-transient errors, or exhausted attempts
    if (!isGet || !isTransient || currentRetry >= maxRetries || dio == null) {
      return handler.next(err);
    }

    request.extra[_retryCountKey] = currentRetry + 1;

    // Exponential delay: initialDelay * 2^currentRetry + random jitter (0-200ms)
    final delayMs = (initialDelay.inMilliseconds * pow(2, currentRetry)).toInt() +
        Random().nextInt(200);
    await Future<void>.delayed(Duration(milliseconds: delayMs));

    try {
      final response = await dio.fetch<dynamic>(request);
      return handler.resolve(response);
    } on DioException catch (nextErr) {
      return handler.next(nextErr);
    } catch (_) {
      return handler.next(err);
    }
  }
}
