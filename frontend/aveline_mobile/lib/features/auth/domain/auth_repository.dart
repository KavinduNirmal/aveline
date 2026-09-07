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
  /// Returns a human-readable error string, or `null` on success.
  Future<String?> signInWithPassword({
    required String identifier,
    required String password,
  });

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
