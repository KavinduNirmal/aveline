import 'package:dio/dio.dart';

import 'auth_interceptor.dart';
import 'auth_token_provider.dart';
import 'retry_interceptor.dart';

/// Builds the app's single [Dio] instance.
abstract final class ApiClientFactory {
  /// Creates a [Dio] configured with the Aveline API [baseUrl] and the auth
  /// interceptor that attaches the Clerk JWT to every request.
  static Dio create({
    required String baseUrl,
    required AuthTokenProvider tokenProvider,
  }) {
    final dio = Dio(
      BaseOptions(
        baseUrl: baseUrl,
        connectTimeout: const Duration(seconds: 15),
        receiveTimeout: const Duration(seconds: 15),
        sendTimeout: const Duration(seconds: 15),
        headers: const {'Accept': 'application/json'},
      ),
    );

    final authInterceptor = AuthInterceptor(tokenProvider: tokenProvider)
      ..attachTo(dio);
    final retryInterceptor = RetryInterceptor()
      ..attachTo(dio);
    dio.interceptors.addAll([
      authInterceptor,
      retryInterceptor,
    ]);

    return dio;
  }
}
