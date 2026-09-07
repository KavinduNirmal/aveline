import 'package:shared_preferences/shared_preferences.dart';

import '../domain/account_type.dart';

/// Persists the onboarding account-type choice locally, scoped per Clerk user so
/// switching accounts never leaks one user's choice into another's onboarding.
class OnboardingPreferences {
  OnboardingPreferences(this._prefs);

  final SharedPreferences _prefs;

  static const _keyPrefix = 'onboarding.accountType.';

  String _key(String clerkId) => '$_keyPrefix$clerkId';

  AccountType? readAccountType(String clerkId) {
    final value = _prefs.getString(_key(clerkId));
    return AccountType.parse(value);
  }

  Future<void> writeAccountType(String clerkId, AccountType type) async {
    await _prefs.setString(_key(clerkId), type.wireValue);
  }

  Future<void> clear(String clerkId) async {
    await _prefs.remove(_key(clerkId));
  }
}
