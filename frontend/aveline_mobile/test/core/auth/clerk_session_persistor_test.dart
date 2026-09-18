import 'package:aveline_mobile/core/auth/clerk_session_persistor.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';

/// The SDK's own keys, which contain a `$` and are therefore namespaced by the
/// persistor.
const String _clientKey = r'$client';
const String _envKey = r'$env';

void main() {
  group('ClerkSessionPersistor', () {
    setUp(() => SharedPreferences.setMockInitialValues(<String, Object>{}));

    Future<ClerkSessionPersistor> newPersistor() async =>
        ClerkSessionPersistor(await SharedPreferences.getInstance());

    test('round-trips a nested map the way the SDK stores a client', () async {
      final persistor = await newPersistor();

      await persistor.write(_clientKey, <String, dynamic>{
        'id': 'client_1',
        'sessions': [
          {'id': 'sess_1'},
        ],
      });

      expect(
        persistor.read<Map<String, dynamic>>(_clientKey),
        <String, dynamic>{
          'id': 'client_1',
          'sessions': [
            {'id': 'sess_1'},
          ],
        },
      );
    });

    test('round-trips a plain string the way the SDK stores its nonce', () async {
      final persistor = await newPersistor();

      await persistor.write('rotating_token_nonce', 'abc123');

      expect(persistor.read<String>('rotating_token_nonce'), 'abc123');
    });

    test('reads a key that was never written as absent', () async {
      final persistor = await newPersistor();

      expect(persistor.read<Map<String, dynamic>>(_clientKey), isNull);
    });

    test('reads an undecodable value as absent so the SDK refetches', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{
        'clerk.$_clientKey': 'not json at all',
      });
      final persistor = await newPersistor();

      expect(persistor.read<Map<String, dynamic>>(_clientKey), isNull);
    });

    test('reads a value of the wrong shape as absent', () async {
      final persistor = await newPersistor();
      await persistor.write(_clientKey, 'a string, not a map');

      expect(persistor.read<Map<String, dynamic>>(_clientKey), isNull);
    });

    test('delete removes a single key', () async {
      final persistor = await newPersistor();
      await persistor.write(_clientKey, <String, dynamic>{'id': 'client_1'});

      await persistor.delete(_clientKey);

      expect(persistor.read<Map<String, dynamic>>(_clientKey), isNull);
    });

    test('clear drops the session and environment but nothing else', () async {
      SharedPreferences.setMockInitialValues(<String, Object>{
        'onboarding.accountType.user_1': 'owner',
      });
      final prefs = await SharedPreferences.getInstance();
      final persistor = ClerkSessionPersistor(prefs);
      await persistor.write(_clientKey, <String, dynamic>{'id': 'client_1'});
      await persistor.write(_envKey, <String, dynamic>{'id': 'env_1'});

      await persistor.clear();

      expect(persistor.read<Map<String, dynamic>>(_clientKey), isNull);
      expect(persistor.read<Map<String, dynamic>>(_envKey), isNull);
      expect(prefs.getString('onboarding.accountType.user_1'), 'owner');
    });

    test('clear is safe when nothing was ever stored', () async {
      final persistor = await newPersistor();

      await expectLater(persistor.clear(), completes);
    });
  });
}
