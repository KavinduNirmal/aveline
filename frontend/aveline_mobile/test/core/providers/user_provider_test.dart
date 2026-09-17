import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/auth/clerk_bootstrap.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Serves a profile, or refuses the connection, on demand.
class _StubAdapter implements HttpClientAdapter {
  bool fail = false;
  int statusCode = 200;
  int calls = 0;

  /// Emulates an interceptor failure, where Dio has no message of its own and
  /// only the wrapped error explains what happened.
  bool failWithoutMessage = false;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    calls++;
    if (failWithoutMessage) {
      throw DioException(
        requestOptions: options,
        error: Exception(
          "ClientException with SocketException: Failed host lookup: "
          "'clerk.example' (OS Error: No address associated with hostname)",
        ),
      );
    }
    if (fail) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'Connection refused',
      );
    }
    if (statusCode != 200) {
      return ResponseBody.fromString('{}', statusCode);
    }
    return ResponseBody.fromString(
      jsonEncode(<String, dynamic>{
        'id': 'user_1',
        'hasCompletedOnboarding': true,
        'accountState': 'Active',
        'organizationRole': 'org:boutique_staff',
        'organizationId': 'org_1',
      }),
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
  late _StubAdapter adapter;
  late Dio dio;
  late UserProvider provider;

  setUp(() {
    adapter = _StubAdapter();
    dio = Dio()..httpClientAdapter = adapter;
    provider = UserProvider();
  });

  group('UserProvider.fetchUser', () {
    test('exposes the profile on success', () async {
      await provider.fetchUser(dio);

      expect(provider.hasLoadFailed, isFalse);
      expect(provider.accountState, AvelineAccountState.active);
      expect(provider.hasCompletedOnboarding, isTrue);
    });

    test('reports a refused connection as a load failure', () async {
      adapter.fail = true;

      await provider.fetchUser(dio);

      expect(provider.hasLoadFailed, isTrue);
      expect(provider.user, isNull);
      expect(provider.errorMessage, contains('Connection refused'));
    });

    test('reports a non-200 response as a load failure', () async {
      adapter.statusCode = 401;

      await provider.fetchUser(dio);

      // Silently returning null here is what used to leave the user parked on
      // an onboarding screen with nothing on screen to explain why.
      expect(provider.hasLoadFailed, isTrue);
      expect(provider.errorMessage, isNotNull);
    });

    test('keeps the underlying cause when Dio has no message of its own', () async {
      adapter.failWithoutMessage = true;

      await provider.fetchUser(dio);

      // Without the wrapped error the retry screen cannot tell an unreachable
      // server from a rejected request, and shows the wrong explanation.
      expect(provider.errorMessage, contains('Failed host lookup'));
      expect(
        describeBootstrapFailure(provider.errorMessage!).isNetwork,
        isTrue,
      );
    });

    test('stays failed while a retry is in flight', () async {
      adapter.fail = true;
      await provider.fetchUser(dio);
      expect(provider.hasLoadFailed, isTrue);

      final retry = provider.fetchUser(dio);

      // Clearing the failure up front would let the router leave the retry
      // screen mid-retry and land back on the onboarding flow.
      expect(provider.isLoading, isTrue);
      expect(provider.hasLoadFailed, isTrue);

      await retry;
      expect(provider.hasLoadFailed, isTrue);
    });

    test('clears the failure once a retry succeeds', () async {
      adapter.fail = true;
      await provider.fetchUser(dio);
      expect(provider.hasLoadFailed, isTrue);

      adapter.fail = false;
      await provider.fetchUser(dio);

      expect(provider.hasLoadFailed, isFalse);
      expect(provider.accountState, AvelineAccountState.active);
    });

    test('clear resets the failure and the profile', () async {
      adapter.fail = true;
      await provider.fetchUser(dio);

      provider.clear();

      expect(provider.hasLoadFailed, isFalse);
      expect(provider.user, isNull);
    });
  });
}
