import 'package:flutter/widgets.dart';

import 'auth_failure.dart';
import 'sign_up_field.dart';
import 'social_provider.dart';

/// The social sign-in half of the auth contract.
///
/// Kept apart from [AuthRepository] for two reasons. The consent flow is the one
/// part of auth that needs a [BuildContext], because the provider's own page is
/// shown in a dialog over the widget that asked for it; and a fake standing in
/// for password sign-in has no business implementing it. A UI that finds no
/// [SocialAuthRepository] simply shows no provider buttons.
abstract interface class SocialAuthRepository {
  /// Signs in with [provider], showing the provider's consent page.
  ///
  /// Returns an [AuthFailure] describing what went wrong, or `null` when the
  /// flow finished. A `null` does not promise a session: an account that does not
  /// exist yet, or one the instance wants more information for, leaves
  /// [pendingSignUpFields] non-empty instead, and the caller continues there.
  /// A user who dismisses the consent page is neither: nothing happened, and
  /// nothing is reported.
  Future<AuthFailure?> signInWithOAuth(
    SocialProvider provider, {
    required BuildContext context,
  });

  /// Creates an account with [provider], showing the provider's consent page.
  ///
  /// Behaves as [signInWithOAuth] does from there: the instance may still need
  /// more than the provider handed over.
  Future<AuthFailure?> signUpWithOAuth(
    SocialProvider provider, {
    required BuildContext context,
  });

  /// What the in-progress sign-up still needs, in the order Clerk asks for it.
  ///
  /// Empty when there is no sign-up in progress, or when it is complete, or when
  /// what is missing is something this app has no prompt for - compare against
  /// [signUpIncomplete] to tell those apart.
  List<SignUpField> get pendingSignUpFields;

  /// Whether a sign-up is waiting on information from the user.
  ///
  /// True even when [pendingSignUpFields] is empty, which is what an instance
  /// asking for something this screen cannot collect looks like. The caller can
  /// then say so rather than leaving the account half-made and silent.
  bool get signUpIncomplete;

  /// Whether the in-progress sign-up still has to confirm its email address.
  ///
  /// A provider that hands over an address it has not verified leaves Clerk
  /// asking for a code, which [AuthRepository.sendEmailVerificationCode] sends
  /// and [AuthRepository.verifyEmailCode] checks.
  bool get signUpNeedsEmailVerification;

  /// Supplies [values] for the in-progress sign-up and advances it.
  ///
  /// Returns an [AuthFailure] when the instance rejects a value, or `null` when
  /// they were accepted. Check [pendingSignUpFields] afterwards: accepting one
  /// value may reveal the next requirement.
  Future<AuthFailure?> submitSignUpFields(Map<SignUpField, String> values);
}
