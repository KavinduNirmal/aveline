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
}
