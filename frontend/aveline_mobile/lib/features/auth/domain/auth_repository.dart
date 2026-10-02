import '../../../core/network/auth_token_provider.dart';
import 'auth_failure.dart';
import 'auth_user.dart';

// Re-exported so callers that already depend on the auth contract pick up
// [AuthFailure] and [AuthCapabilities] without a second import.
export 'auth_failure.dart';

/// Contract for the auth feature, consumed by the UI and the router.
///
/// Extends [AuthTokenProvider] so HTTP infrastructure can share the same
/// implementation without depending on the auth feature.
abstract interface class AuthRepository implements AuthTokenProvider {
  /// Whether a signed-in session currently exists.
  bool get isSignedIn;

  /// The signed-in user, or `null` when signed out.
  AuthUser? get currentUser;

  /// What this Clerk instance allows, so forms render what the instance
  /// actually supports instead of hard-coding the development instance's
  /// settings.
  AuthCapabilities get capabilities;

  /// Signs in with an email address or username and a password.
  ///
  /// Returns an [AuthFailure] describing what went wrong, or `null` on success.
  /// When the account has a second factor enabled, the sign-in is left in a
  /// `needs_second_factor` state (see [needsSecondFactor]) and must be completed
  /// with [verifySecondFactorCode].
  Future<AuthFailure?> signInWithPassword({
    required String identifier,
    required String password,
  });

  /// Whether the in-progress sign-in requires a second factor before the
  /// session is established.
  bool get needsSecondFactor;

  /// The second-factor strategy Clerk is requesting (e.g. `email_code`,
  /// `phone_code`, `totp`, `backup_code`), or `null` when none is required.
  String? get secondFactorStrategy;

  /// Sends the one-time code for a code-based second factor (email/phone).
  ///
  /// Returns an [AuthFailure], or `null` when the code was sent.
  Future<AuthFailure?> sendSecondFactorCode();

  /// Completes a sign-in that requires a second factor using the one-time [code]
  /// (email/phone code, authenticator TOTP, or a backup code).
  ///
  /// Returns an [AuthFailure], or `null` on success.
  Future<AuthFailure?> verifySecondFactorCode({required String code});

  /// Creates a password-based account (email is required; username and name
  /// fields are passed through when the instance supports them).
  ///
  /// Returns an [AuthFailure], or `null` when the account was created. If email
  /// verification is required, call [sendEmailVerificationCode] next.
  Future<AuthFailure?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  });

  /// Sends a one-time email verification code for the pending sign-up.
  Future<AuthFailure?> sendEmailVerificationCode();

  /// Verifies the pending sign-up with the emailed code.
  Future<AuthFailure?> verifyEmailCode({required String code});
}
