import 'dart:convert';
import 'dart:typed_data';

import 'package:aveline_mobile/core/auth/clerk_bootstrap.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/auth/domain/contact_preference.dart';
import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';

/// Records what `PATCH /api/v1/users/me` was handed, and answers with the record
/// the API would hold afterwards.
///
/// The real endpoint answers a change with the whole updated [UserDto], which is
/// what lets the provider replace its held profile with the server's answer
/// rather than guessing at it.
class _UpdateAdapter implements HttpClientAdapter {
  int statusCode = 200;
  String failureMessage = 'That change was refused.';
  bool fail = false;
  int calls = 0;
  String? method;
  String? path;
  Map<String, dynamic>? body;

  Map<String, dynamic> profile = {
    'id': 'user_1',
    'clerkId': 'clerk_1',
    'email': 'charlotte@aveline.com',
    'firstName': 'Charlotte',
    'lastName': 'Tilbury',
    'displayName': 'Charlotte Tilbury',
    'username': 'charlotte',
    'phoneNumber': '+94771234567',
    'userRole': 'staff',
    'organizationRole': 'org:boutique_staff',
    'organizationId': 'org_1',
    'hasCompletedOnboarding': true,
    'accountState': 'Active',
    'contactPreference': 'WhatsApp',
    'pushNotificationsEnabled': true,
    'isActive': true,
  };

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    calls++;
    method = options.method;
    path = options.uri.path;
    final sent = options.data;
    body = sent is Map
        ? sent.map((key, value) => MapEntry(key.toString(), value))
        : null;

    if (fail) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'Connection refused',
      );
    }

    final headers = {Headers.contentTypeHeader: [Headers.jsonContentType]};
    if (statusCode != 200) {
      return ResponseBody.fromString(
        jsonEncode(<String, dynamic>{'message': failureMessage}),
        statusCode,
        headers: headers,
      );
    }

    return ResponseBody.fromString(
      jsonEncode(<String, dynamic>{...profile, ...?body}),
      200,
      headers: headers,
    );
  }

  @override
  void close({bool force = false}) {}
}

AvelineUser _heldUser() => AvelineUser.fromJson(_UpdateAdapter().profile);

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

  group('UserProvider.updateProfile', () {
    late _UpdateAdapter adapter;
    late Dio dio;
    late UserProvider provider;

    setUp(() {
      adapter = _UpdateAdapter();
      dio = Dio()..httpClientAdapter = adapter;
      provider = UserProvider()..setUser(_heldUser());
    });

    test('sends only the fields it was handed', () async {
      final saved = await provider.updateProfile(
        dio,
        pushNotificationsEnabled: false,
      );

      expect(saved, isTrue);
      expect(adapter.method, 'PATCH');
      expect(adapter.path, '/api/v1/users/me');
      // `UpdateUserProfileRequest` changes only the fields present, so an absent
      // field is what leaves the rest of the record alone.
      expect(adapter.body, {'pushNotificationsEnabled': false});
    });

    test('sends a contact preference in the spelling the API reads', () async {
      await provider.updateProfile(
        dio,
        contactPreference: ContactPreference.whatsApp,
      );

      expect(adapter.body, {'contactPreference': 'WhatsApp'});
    });

    test('sends nothing at all when it is handed nothing to change', () async {
      final saved = await provider.updateProfile(dio);

      expect(saved, isTrue);
      expect(adapter.calls, 0);
    });

    test('replaces the held profile with the answer the server gives', () async {
      final saved = await provider.updateProfile(
        dio,
        pushNotificationsEnabled: false,
        contactPreference: ContactPreference.email,
      );

      expect(saved, isTrue);
      expect(provider.user!.pushNotificationsEnabled, isFalse);
      expect(provider.user!.contactPreference, 'Email');
      expect(provider.updateErrorMessage, isNull);
    });

    test('reports a refused change without disturbing the held profile', () async {
      adapter.fail = true;

      final saved = await provider.updateProfile(
        dio,
        pushNotificationsEnabled: false,
      );

      expect(saved, isFalse);
      // The controls read the held record rather than local state, so a save that
      // failed reverts on its own without the screen having to put it back.
      expect(provider.user!.pushNotificationsEnabled, isTrue);
      expect(provider.updateErrorMessage, contains('Connection refused'));
      // A write that failed is not a profile that could not be read: the router
      // sends the retry screen on load failures, and must not be dragged there.
      expect(provider.hasLoadFailed, isFalse);
    });

    test('repeats the reason the server gave for refusing a change', () async {
      adapter.statusCode = 400;
      adapter.failureMessage = 'Phone number is too long.';

      final saved = await provider.updateProfile(
        dio,
        phoneNumber: '+947712345678901234567890',
      );

      expect(saved, isFalse);
      // Dio's own message for a bad response explains validateStatus, which is no
      // use to an associate. The API's reason is the one worth repeating.
      expect(provider.updateErrorMessage, 'Phone number is too long.');
    });

    test('clears the failure once a later save succeeds', () async {
      adapter.fail = true;
      await provider.updateProfile(dio, pushNotificationsEnabled: false);
      expect(provider.updateErrorMessage, isNotNull);

      adapter.fail = false;
      final saved = await provider.updateProfile(
        dio,
        pushNotificationsEnabled: false,
      );

      expect(saved, isTrue);
      expect(provider.updateErrorMessage, isNull);
    });

    test('clearUpdateError leaves the held profile alone', () async {
      adapter.fail = true;
      await provider.updateProfile(dio, pushNotificationsEnabled: false);

      provider.clearUpdateError();

      expect(provider.updateErrorMessage, isNull);
      expect(provider.user, isNotNull);
    });
  });
}
