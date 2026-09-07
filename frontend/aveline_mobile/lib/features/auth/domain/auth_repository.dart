import '../../../core/network/auth_token_provider.dart';
import 'auth_user.dart';

/// Contract for the auth feature, consumed by the UI and the router.
///
/// Extends [AuthTokenProvider] so HTTP infrastructure can share the same
/// implementation without depending on the auth feature.
abstract interface class AuthRepository implements AuthTokenProvider {
  /// Whether a signed-in session currently exists.
  bool get isSignedIn;

  /// The signed-in user, or `null` when signed out.
  AuthUser? get currentUser;

  /// Signs in with an email address or username and a password.
  ///
  /// Returns a human-readable error string, or `null` on success. When the
  /// account has a second factor enabled, the sign-in is left in a
  /// `needs_second_factor` state (see [needsSecondFactor]) and must be completed
  /// with [signInWithSecondFactor].
  Future<String?> signInWithPassword({
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
  /// Returns a human-readable error string, or `null` when the code was sent.
  Future<String?> sendSecondFactorCode();

  /// Completes a sign-in that requires a second factor using the one-time [code]
  /// (email/phone code, authenticator TOTP, or a backup code).
  ///
  /// Returns a human-readable error string, or `null` on success.
  Future<String?> verifySecondFactorCode({required String code});

  /// Creates a password-based account (email is required; username and name
  /// fields are passed through when the instance supports them).
  ///
  /// Returns a human-readable error string, or `null` when the account was
  /// created. If email verification is required, call
  /// [sendEmailVerificationCode] next.
  Future<String?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  });

  /// Sends a one-time email verification code for the pending sign-up.
  Future<String?> sendEmailVerificationCode();

  /// Verifies the pending sign-up with the emailed code.
  Future<String?> verifyEmailCode({required String code});
}
