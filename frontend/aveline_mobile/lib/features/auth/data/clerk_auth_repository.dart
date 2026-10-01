import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:flutter/foundation.dart';

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
  bool get needsSecondFactor => _authState.signIn?.needsSecondFactor ?? false;

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
  }) {
    final cleanIdentifier = identifier.trim();
    if (cleanIdentifier.isEmpty || password.isEmpty) {
      return Future.value('Please provide both an identifier and a password.');
    }
    return _run(() => _authState.attemptSignIn(
          strategy: clerk.Strategy.password,
          identifier: cleanIdentifier,
          password: password,
        ));
  }

  @override
  String? get secondFactorStrategy {
    final factors = _authState.signIn?.supportedSecondFactors ?? const [];
    return factors.isNotEmpty ? factors.first.strategy.name : null;
  }

  /// The [clerk.Strategy] Clerk is requesting for the second factor, falling
  /// back to TOTP when the supported factors are not exposed.
  clerk.Strategy get _secondFactorStrategy {
    final factors = _authState.signIn?.supportedSecondFactors ?? const [];
    return factors.isNotEmpty ? factors.first.strategy : clerk.Strategy.totp;
  }

  @override
  Future<String?> sendSecondFactorCode() {
    // For code-based factors (email/phone) calling attemptSignIn without a code
    // triggers Clerk's prepare step, which dispatches the one-time code.
    return _run(() => _authState.attemptSignIn(strategy: _secondFactorStrategy));
  }

  @override
  Future<String?> verifySecondFactorCode({required String code}) {
    final cleanCode = code.trim();
    if (cleanCode.isEmpty) {
      return Future.value('Please enter the verification code.');
    }
    return _run(() => _authState.attemptSignIn(
          strategy: _secondFactorStrategy,
          code: cleanCode,
        ));
  }

  @override
  Future<String?> signUpWithPassword({
    required String emailAddress,
    String? username,
    String? firstName,
    String? lastName,
    required String password,
  }) {
    final cleanEmail = emailAddress.trim();
    if (cleanEmail.isEmpty || password.isEmpty) {
      return Future.value('Please provide both an email and a password.');
    }
    final cleanUsername =
        (username == null || username.trim().isEmpty) ? null : username.trim();
    final cleanFirstName =
        (firstName == null || firstName.trim().isEmpty) ? null : firstName.trim();
    final cleanLastName =
        (lastName == null || lastName.trim().isEmpty) ? null : lastName.trim();

    return _run(() => _authState.attemptSignUp(
          strategy: clerk.Strategy.password,
          emailAddress: cleanEmail,
          username: cleanUsername,
          firstName: cleanFirstName,
          lastName: cleanLastName,
          password: password,
          passwordConfirmation: password,
        ));
  }

  @override
  Future<String?> sendEmailVerificationCode() =>
      _run(() => _authState.attemptSignUp(strategy: clerk.Strategy.emailCode));

  @override
  Future<String?> verifyEmailCode({required String code}) {
    final cleanCode = code.trim();
    if (cleanCode.isEmpty) {
      return Future.value('Please enter the verification code.');
    }
    return _run(() => _authState.attemptSignUp(
          strategy: clerk.Strategy.emailCode,
          code: cleanCode,
        ));
  }

  /// Extracts the human-readable error message from a [clerk.ClerkError].
  ///
  /// Clerk's server-error template string contains unformatted '{arg}'.
  /// The actual user-facing reason (e.g. invalid credentials, identifier not found)
  /// is stored in [clerk.ClerkError.argument] or [clerk.ClerkError.errors].
  @visibleForTesting
  static String extractErrorMessage(clerk.ClerkError error) {
    if (error.argument case final arg? when arg.trim().isNotEmpty && arg != '{arg}') {
      return arg.trim();
    }
    final errors = error.errors?.errors;
    if (errors != null && errors.isNotEmpty) {
      final messages = errors
          .map((e) => e.fullMessage.trim())
          .where((m) => m.isNotEmpty && m != 'Unknown error')
          .toList();
      if (messages.isNotEmpty) {
        return messages.join('; ');
      }
    }
    final asString = error.toString().trim();
    if (asString.isNotEmpty &&
        !asString.contains('{arg}') &&
        asString != '(ERROR RECEIVED FROM SERVER)') {
      final stripped = asString.replaceFirst(' (ERROR RECEIVED FROM SERVER)', '').trim();
      if (stripped.isNotEmpty) {
        return stripped;
      }
    }
    final cleanMessage = error.message
        .replaceFirst('{arg}', '')
        .replaceFirst('(ERROR RECEIVED FROM SERVER)', '')
        .trim();
    if (cleanMessage.isNotEmpty && !cleanMessage.contains('{arg}')) {
      return cleanMessage;
    }
    return 'Authentication failed. Please verify your credentials and try again.';
  }

  /// Runs an auth action, translating failures into a human-readable message.
  Future<String?> _run(Future<void> Function() action) async {
    try {
      await action();
      return null;
    } on clerk.ClerkError catch (error) {
      final message = extractErrorMessage(error);
      debugPrint('[clerk auth error] ClerkError: $message - ${error.code}');
      return message;
    } catch (error, stack) {
      debugPrint('[clerk auth error] $error\n$stack');
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
