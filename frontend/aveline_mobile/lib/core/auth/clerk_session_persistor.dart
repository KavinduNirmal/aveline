import 'dart:convert';

import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:shared_preferences/shared_preferences.dart';

/// [clerk.Persistor] that keeps Clerk's client and environment cache in
/// [SharedPreferences].
///
/// Aveline owns session persistence instead of using the SDK's built-in store
/// for one reason: the SDK's store is private to `clerk_flutter`, so application
/// code cannot clear it. [clear] has to be able to drop a stored session that
/// makes `ClerkAuthState.create` fail on a cold start, and it is the only way to
/// recover without asking the user to reinstall.
///
/// Values are JSON encoded per key, which is what the SDK itself does: it writes
/// the result of `toJson()` and reads the same shape back on the next launch.
class ClerkSessionPersistor implements clerk.Persistor {
  ClerkSessionPersistor(this._preferences);

  final SharedPreferences _preferences;

  /// Keys are namespaced because the SDK's own keys (`$client`, `$env`) are not
  /// valid preference names on every platform.
  static const String _prefix = 'clerk.';

  String _key(String key) => '$_prefix$key';

  /// Nothing to load: [SharedPreferences] is already in memory.
  @override
  Future<void> initialize() async {}

  /// Nothing to release: writes go straight through [SharedPreferences].
  @override
  void terminate() {}

  @override
  T? read<T>(String key) {
    final encoded = _preferences.getString(_key(key));
    if (encoded == null) {
      return null;
    }
    try {
      final decoded = jsonDecode(encoded);
      // A stored value of an unexpected shape is treated as absent, so the SDK
      // re-fetches rather than failing to start.
      return decoded is T ? decoded : null;
    } on FormatException {
      return null;
    }
  }

  @override
  Future<void> write<T>(String key, T value) async {
    await _preferences.setString(_key(key), jsonEncode(value));
  }

  @override
  Future<void> delete(String key) async {
    await _preferences.remove(_key(key));
  }

  /// Drops every key this persistor owns, signing the device out of Clerk.
  ///
  /// Used during bootstrap when a stored session cannot be used, so the retry
  /// starts from a signed-out client.
  Future<void> clear() async {
    final owned = _preferences
        .getKeys()
        .where((key) => key.startsWith(_prefix))
        .toList(growable: false);
    for (final key in owned) {
      await _preferences.remove(key);
    }
  }
}
