import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:clerk_flutter/clerk_flutter.dart';

import '../domain/auth_claims.dart';
import '../domain/auth_repository.dart';
import '../domain/auth_user.dart';
import 'auth_user_mapper.dart';

/// [AuthRepository] backed by the Clerk Flutter SDK.
///
/// Sessions are persisted by the SDK, so the signed-in state is restored
/// automatically across app restarts. Tokens are minted from the Aveline JWT
/// template so they carry the `user_role`/`org_role` claims the backend needs.
class ClerkAuthRepository implements AuthRepository {
  ClerkAuthRepository(this._authState, {required this.jwtTemplateName});

  final ClerkAuthState _authState;
  final String jwtTemplateName;

  clerk.SessionToken? _cachedToken;

  @override
  bool get isSignedIn => _authState.isSignedIn;

  @override
  AuthUser? get currentUser {
    final user = _authState.user;
    if (user == null) {
      return null;
    }
    final claims = _cachedToken == null
        ? const AuthClaims()
        : AuthClaims.fromBody(_cachedToken!.body);
    return AuthUserMapper.fromClerk(user, claims: claims);
  }

  @override
  Future<String?> getToken() async {
    final cached = _cachedToken;
    if (cached != null && cached.isNotExpired) {
      return cached.jwt;
    }
    final token = await _fetchToken();
    return token?.jwt;
  }

  @override
  Future<String?> refreshToken() async {
    _cachedToken = null;
    final token = await _fetchToken();
    return token?.jwt;
  }

  @override
  Future<void> signOut() => _authState.signOut();

  @override
  Future<String?> signInWithPassword({
    required String identifier,
    required String password,
  }) =>
      _run(() => _authState.attemptSignIn(
            strategy: clerk.Strategy.password,
            identifier: identifier,
            password: password,
          ));

  @override
  Future<String?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  }) =>
      _run(() => _authState.attemptSignUp(
            strategy: clerk.Strategy.password,
            emailAddress: emailAddress,
            username: username,
            firstName: firstName,
            lastName: lastName,
            password: password,
            passwordConfirmation: password,
          ));

  @override
  Future<String?> sendEmailVerificationCode() =>
      _run(() => _authState.attemptSignUp(strategy: clerk.Strategy.emailCode));

  @override
  Future<String?> verifyEmailCode({required String code}) =>
      _run(() => _authState.attemptSignUp(
            strategy: clerk.Strategy.emailCode,
            code: code,
          ));

  /// Runs an auth action, translating failures into a human-readable message.
  Future<String?> _run(Future<void> Function() action) async {
    try {
      await action();
      return null;
    } on clerk.ClerkError catch (error) {
      return error.message;
    } on Exception catch (error) {
      return error.toString().replaceFirst('Exception: ', '');
    }
  }

  Future<clerk.SessionToken?> _fetchToken() async {
    if (!_authState.isSignedIn) {
      return null;
    }
    final token = await _authState.sessionToken(templateName: jwtTemplateName);
    _cachedToken = token;
    return token;
  }
}
