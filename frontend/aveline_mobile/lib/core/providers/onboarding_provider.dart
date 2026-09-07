import 'package:flutter/foundation.dart';

import '../../features/onboarding/data/onboarding_preferences.dart';
import '../../features/onboarding/domain/account_type.dart';

/// Holds the current user's onboarding account-type choice and keeps it in sync
/// with local storage. The router reads [accountType] to decide whether a pending
/// account should continue the owner wizard or the staff invite-code path.
class OnboardingProvider extends ChangeNotifier {
  OnboardingProvider(this._preferences);

  final OnboardingPreferences _preferences;

  String? _clerkId;
  AccountType? _accountType;

  /// An invitation code received via a deep link, awaiting redemption once the
  /// user is signed in and on the staff invite-code step.
  String? _pendingInviteCode;

  /// The Clerk id the current choice belongs to.
  String? get clerkId => _clerkId;

  /// The chosen onboarding path for the current user, or `null` if not chosen.
  AccountType? get accountType => _accountType;

  bool get isOwner => _accountType == AccountType.owner;

  /// A deep-linked invitation code to prefill/auto-accept, or `null`.
  String? get pendingInviteCode => _pendingInviteCode;

  /// Records a deep-linked invitation code (e.g. from `aveline://invite?code=…`).
  void setPendingInviteCode(String? code) {
    _pendingInviteCode = code;
    notifyListeners();
  }

  /// Loads the persisted choice for [clerkId]. Call when a user signs in.
  Future<void> load(String clerkId) async {
    _clerkId = clerkId;
    _accountType = _preferences.readAccountType(clerkId);
    notifyListeners();
  }

  /// Persists the chosen [type] for the current user.
  Future<void> select(AccountType type) async {
    final clerkId = _clerkId;
    if (clerkId == null) {
      return;
    }
    _accountType = type;
    await _preferences.writeAccountType(clerkId, type);
    notifyListeners();
  }

  /// Clears the choice (e.g. on sign-out) so a fresh account starts clean.
  Future<void> clear() async {
    final clerkId = _clerkId;
    _clerkId = null;
    _accountType = null;
    _pendingInviteCode = null;
    if (clerkId != null) {
      await _preferences.clear(clerkId);
    }
    notifyListeners();
  }
}
