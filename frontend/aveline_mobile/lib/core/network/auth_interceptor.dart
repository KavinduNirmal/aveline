import 'package:dio/dio.dart';

import 'auth_token_provider.dart';

/// Attaches the Clerk JWT to every request and reacts to 401 responses.
///
/// When a request fails with 401 the interceptor asks the
/// [AuthTokenProvider] for a fresh token, retries the request once, and —
/// if it is rejected again — signs the user out so the router can redirect
/// to the sign-in screen.
class AuthInterceptor extends Interceptor {
  AuthInterceptor({required this.tokenProvider});

  final AuthTokenProvider tokenProvider;

  /// Marker stored on the request's `extra` map so a retried request is not
  /// retried again (the marker is never sent to the server).
  static const String _retriedKey = 'auth_retried';

  Dio? _dio;

  /// Binds the owning [Dio] instance so the interceptor can retry requests
  /// after a token refresh.
  void attachTo(Dio dio) => _dio = dio;

  @override
  Future<void> onRequest(
    RequestOptions options,
    RequestInterceptorHandler handler,
  ) async {
    final token = await tokenProvider.getToken();
    if (token != null && token.isNotEmpty) {
      options.headers['Authorization'] = 'Bearer $token';
    }
    handler.next(options);
  }

  @override
  Future<void> onError(DioException err, ErrorInterceptorHandler handler) async {
    final isUnauthorized = err.response?.statusCode == 401;
    final alreadyRetried = err.requestOptions.extra[_retriedKey] == true;
    final dio = _dio;

    if (!isUnauthorized || alreadyRetried || dio == null) {
      handler.next(err);
      return;
    }

    final String? token;
    try {
      token = await tokenProvider.refreshToken();
    } catch (_) {
      handler.next(err);
      return;
    }

    if (token == null || token.isEmpty) {
      handler.next(err);
      return;
    }

    final options = err.requestOptions;
    options.headers['Authorization'] = 'Bearer $token';
    options.extra[_retriedKey] = true;

    try {
      final response = await dio.fetch<dynamic>(options);
      handler.resolve(response);
    } on DioException {
      // A fresh token still produced an error (usually 401): the session is
      // invalid, so log the user out and surface the original error.
      await tokenProvider.signOut();
      handler.next(err);
    }
  }
}
