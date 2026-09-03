import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/network/auth_interceptor.dart';
import 'package:aveline_mobile/core/network/auth_token_provider.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

class _FakeTokenProvider implements AuthTokenProvider {
  _FakeTokenProvider({this.current = 'token-1', this.refreshed});

  String? current;
  final String? refreshed;
  int refreshCount = 0;
  int signOutCount = 0;

  @override
  Future<String?> getToken() async => current;

  @override
  Future<String?> refreshToken() async {
    refreshCount++;
    if (refreshed != null) {
      current = refreshed;
    }
    return current;
  }

  @override
  Future<void> signOut() async {
    signOutCount++;
    current = null;
  }
}

class _QueuedAdapter implements HttpClientAdapter {
  _QueuedAdapter(this.handler);

  final ResponseBody Function(RequestOptions options) handler;
  final List<RequestOptions> requests = [];

  /// The `Authorization` header value captured when each request was sent
  /// (the [RequestOptions] object itself may be mutated later by a retry).
  final List<String?> sentAuthorization = [];

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);
    sentAuthorization.add(options.headers['Authorization'] as String?);
    return handler(options);
  }

  @override
  void close({bool force = false}) {}
}

ResponseBody _json(int status, Map<String, dynamic> body) => ResponseBody(
      Stream.fromIterable([utf8.encode(jsonEncode(body))]),
      status,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );

Dio _buildDio({
  required HttpClientAdapter adapter,
  required AuthTokenProvider tokenProvider,
}) {
  final dio = Dio(BaseOptions(baseUrl: 'http://api.test'));
  dio.httpClientAdapter = adapter;
  final interceptor = AuthInterceptor(tokenProvider: tokenProvider)
    ..attachTo(dio);
  dio.interceptors.add(interceptor);
  return dio;
}

void main() {
  group('AuthInterceptor', () {
    test('attaches the Bearer token to every request', () async {
      final tokenProvider = _FakeTokenProvider(current: 'abc');
      final adapter = _QueuedAdapter((options) => _json(200, {'ok': true}));
      final dio = _buildDio(adapter: adapter, tokenProvider: tokenProvider);

      await dio.get<dynamic>('/ping');

      final headers = adapter.requests.single.headers;
      expect(headers['Authorization'], 'Bearer abc');
    });

    test('adds no Authorization header when signed out', () async {
      final tokenProvider = _FakeTokenProvider(current: null);
      final adapter = _QueuedAdapter((options) => _json(200, {'ok': true}));
      final dio = _buildDio(adapter: adapter, tokenProvider: tokenProvider);

      await dio.get<dynamic>('/ping');

      expect(
        adapter.requests.single.headers.containsKey('Authorization'),
        isFalse,
      );
    });

    test('refreshes the token and retries once after a 401', () async {
      final tokenProvider = _FakeTokenProvider(
        current: 'token-1',
        refreshed: 'token-2',
      );
      final adapter = _QueuedAdapter((options) {
        final retried = options.extra['auth_retried'] == true;
        return retried ? _json(200, {'ok': true}) : _json(401, {'error': 'x'});
      });
      final dio = _buildDio(adapter: adapter, tokenProvider: tokenProvider);

      final response = await dio.get<dynamic>('/ping');

      expect(response.statusCode, 200);
      expect(tokenProvider.refreshCount, 1);
      expect(tokenProvider.signOutCount, 0);
      expect(
        adapter.sentAuthorization,
        ['Bearer token-1', 'Bearer token-2'],
      );
    });

    test('signs out when a retried request is still rejected with 401',
        () async {
      final tokenProvider = _FakeTokenProvider(
        current: 'token-1',
        refreshed: 'token-2',
      );
      final adapter = _QueuedAdapter(
        (options) => _json(401, {'error': 'unauthorized'}),
      );
      final dio = _buildDio(adapter: adapter, tokenProvider: tokenProvider);

      await expectLater(
        dio.get<dynamic>('/ping'),
        throwsA(
          isA<DioException>().having(
            (e) => e.response?.statusCode,
            'statusCode',
            401,
          ),
        ),
      );

      expect(tokenProvider.refreshCount, 1);
      expect(tokenProvider.signOutCount, 1);
    });

    test('does not retry non-401 errors', () async {
      final tokenProvider = _FakeTokenProvider(current: 'abc');
      final adapter = _QueuedAdapter((options) => _json(500, {'error': 'boom'}));
      final dio = _buildDio(adapter: adapter, tokenProvider: tokenProvider);

      await expectLater(
        dio.get<dynamic>('/ping'),
        throwsA(
          isA<DioException>().having(
            (e) => e.response?.statusCode,
            'statusCode',
            500,
          ),
        ),
      );

      expect(tokenProvider.refreshCount, 0);
      expect(tokenProvider.signOutCount, 0);
    });
  });
}
