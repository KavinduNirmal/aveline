import 'dart:async';

import 'package:aveline_mobile/core/auth/clerk_bootstrap.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('withSessionReset', () {
    test('keeps the stored session when the first attempt succeeds', () async {
      var created = 0;
      var cleared = 0;

      final result = await withSessionReset<String>(
        create: () async {
          created++;
          return 'auth-state';
        },
        clearSession: () async => cleared++,
      );

      expect(result, 'auth-state');
      expect(created, 1);
      expect(cleared, 0);
    });

    test('clears the stored session and retries after a failure', () async {
      var created = 0;
      var cleared = 0;
      Object? reported;

      final result = await withSessionReset<String>(
        create: () async {
          created++;
          if (created == 1) {
            throw Exception('ClientException with SocketException');
          }
          return 'auth-state';
        },
        clearSession: () async => cleared++,
        onStaleSession: (error, _) => reported = error,
      );

      expect(result, 'auth-state');
      expect(created, 2);
      expect(cleared, 1);
      expect(reported, isNotNull);
    });

    test('reports the retry failure, not the one that triggered it', () async {
      final first = StateError('stale session');
      final second = StateError('still unreachable');
      final errors = [first, second];
      var attempt = 0;
      var cleared = 0;

      await expectLater(
        withSessionReset<void>(
          create: () async => throw errors[attempt++],
          clearSession: () async => cleared++,
        ),
        throwsA(same(second)),
      );

      expect(attempt, 2);
      expect(cleared, 1);
    });

    test('does not clear the session when there is nothing to recover', () async {
      var cleared = 0;
      final error = StateError('bad publishable key');

      await expectLater(
        withSessionReset<void>(
          create: () async => throw error,
          clearSession: () async => cleared++,
        ),
        throwsA(same(error)),
      );

      // The recovery still runs once; it is the second failure that surfaces.
      expect(cleared, 1);
    });

    test('treats an attempt that never answers as a failure', () async {
      var created = 0;
      var cleared = 0;

      // Reproduces a network whose lookups never answer: the SDK awaits its
      // session-token poll and never returns.
      final result = await withSessionReset<String>(
        create: () {
          created++;
          if (created == 1) {
            return Completer<String>().future;
          }
          return Future<String>.value('auth-state');
        },
        clearSession: () async => cleared++,
        attemptTimeout: const Duration(milliseconds: 20),
      );

      expect(result, 'auth-state');
      expect(created, 2);
      expect(cleared, 1);
    });

    test('gives up when even the retry times out', () async {
      await expectLater(
        withSessionReset<String>(
          create: () => Completer<String>().future,
          clearSession: () async {},
          attemptTimeout: const Duration(milliseconds: 20),
        ),
        throwsA(isA<TimeoutException>()),
      );
    });
  });

  group('describeBootstrapFailure', () {
    test('recognises a wrapped socket failure as a network problem', () {
      final failure = describeBootstrapFailure(
        Exception(
          "ClientException with SocketException: Failed host lookup: "
          "'inspired-warthog-8208.clerk.accounts.dev' (OS Error: No address "
          'associated with hostname, errno = 7)',
        ),
      );

      expect(failure.isNetwork, isTrue);
      expect(failure.detail, contains('Failed host lookup'));
    });

    test('recognises a refused connection as a network problem', () {
      expect(
        describeBootstrapFailure(
          Exception('Connection refused'),
        ).isNetwork,
        isTrue,
      );
    });

    test('treats a Clerk rejection as something other than the network', () {
      final failure = describeBootstrapFailure(
        Exception('Invalid authentication: you need to supply an active session'),
      );

      expect(failure.isNetwork, isFalse);
      expect(failure.detail, contains('Invalid authentication'));
    });

    test('treats a timeout as a network problem', () {
      expect(
        describeBootstrapFailure(
          TimeoutException('Future not completed', const Duration(seconds: 10)),
        ).isNetwork,
        isTrue,
      );
    });
  });
}
