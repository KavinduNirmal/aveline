import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:flutter/widgets.dart';

import '../domain/auth_claims.dart';
import '../domain/auth_repository.dart';
import '../domain/auth_user.dart';
import '../domain/sign_up_field.dart';
import '../domain/social_auth_repository.dart';
import '../domain/social_provider.dart';
import 'auth_user_mapper.dart';

/// [AuthRepository] backed by the Clerk Flutter SDK.
///
/// Sessions are persisted by the SDK, so the signed-in state is restored
/// automatically across app restarts. Tokens are minted from the Aveline JWT
/// template so they carry the `user_role`/`org_role` claims the backend needs.
///
/// Also the [SocialAuthRepository]: the SDK owns the provider consent page, so
/// the app hands it the widget to show that page over rather than reimplementing
/// the web view, its redirect interception and the Google-specific user agent
/// that page needs.
class ClerkAuthRepository implements AuthRepository, SocialAuthRepository {
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

  /// Reads what this instance allows from the Clerk environment.
  ///
  /// The production and development instances disagree about whether `username`
  /// is an identifier, and both require longer passwords than the forms used to
  /// accept, so the UI reads these values instead of hard-coding one instance's
  /// settings.
  @override
  AuthCapabilities get capabilities {
    final env = _authState.env;
    if (env.isEmpty) {
      return AuthCapabilities.unknown;
    }
    final username = env.user.attributes[clerk.UserAttribute.username];
    return AuthCapabilities(
      usernameEnabled: username?.isEnabled ?? false,
      usernameRequired: username?.isRequired ?? false,
      passwordMinLength: env.user.passwordSettings.minLength,
      signUpCaptchaRequired: env.user.signUp.captchaEnabled,
      socialProviders: SocialProvider.listFrom(env.socialConnections),
    );
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
  Future<AuthFailure?> signInWithPassword({
    required String identifier,
    required String password,
  }) =>
      _run(() => _authState.attemptSignIn(
            strategy: clerk.Strategy.password,
            identifier: identifier,
            password: password,
          ));

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
  Future<AuthFailure?> sendSecondFactorCode() {
    // For code-based factors (email/phone) calling attemptSignIn without a code
    // triggers Clerk's prepare step, which dispatches the one-time code.
    return _run(() => _authState.attemptSignIn(strategy: _secondFactorStrategy));
  }

  @override
  Future<AuthFailure?> verifySecondFactorCode({required String code}) {
    return _run(() => _authState.attemptSignIn(
          strategy: _secondFactorStrategy,
          code: code,
        ));
  }

  @override
  Future<AuthFailure?> signUpWithPassword({
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
  Future<AuthFailure?> sendEmailVerificationCode() =>
      _run(() => _authState.attemptSignUp(strategy: clerk.Strategy.emailCode));

  @override
  Future<AuthFailure?> verifyEmailCode({required String code}) =>
      _run(() => _authState.attemptSignUp(
            strategy: clerk.Strategy.emailCode,
            code: code,
          ));

  @override
  Future<AuthFailure?> signInWithOAuth(
    SocialProvider provider, {
    required BuildContext context,
  }) =>
      _runConsent(
        (onError) =>
            _authState.ssoSignIn(context, provider.strategy, onError: onError),
      );

  @override
  Future<AuthFailure?> signUpWithOAuth(
    SocialProvider provider, {
    required BuildContext context,
  }) =>
      _runConsent(
        (onError) =>
            _authState.ssoSignUp(context, provider.strategy, onError: onError),
      );

  @override
  List<SignUpField> get pendingSignUpFields => SignUpField.listFromNames(
        _authState.signUp?.missingFields.map((field) => field.name) ?? const [],
      );

  @override
  bool get signUpIncomplete =>
      _authState.signUp?.missingFields.isNotEmpty ?? false;

  @override
  bool get signUpNeedsEmailVerification =>
      _authState.signUp?.unverified(clerk.Field.emailAddress) ?? false;

  @override
  Future<AuthFailure?> submitSignUpFields(Map<SignUpField, String> values) =>
      _run(() => _authState.attemptSignUp(
            // The SDK's own guidance for finishing a sign-up the provider could
            // not complete: supply the missing values and let `attemptSignUp`
            // work out what is left. No strategy is named, because the fields
            // being filled in are not what the sign-up is attempted with.
            username: values[SignUpField.username],
            emailAddress: values[SignUpField.emailAddress],
            phoneNumber: values[SignUpField.phoneNumber],
            firstName: values[SignUpField.firstName],
            lastName: values[SignUpField.lastName],
          ));

  /// Runs a consent flow, translating failures into a presentable [AuthFailure].
  ///
  /// The SDK reports these through an `onError` callback instead of by throwing -
  /// `ssoSignIn`/`ssoSignUp` catch internally so the error can reach whatever
  /// overlay is showing - so the failure is collected here and mapped exactly as
  /// [_run] maps the password flows' errors.
  Future<AuthFailure?> _runConsent(
    Future<void> Function(ClerkErrorCallback onError) action,
  ) async {
    clerk.ClerkError? reported;
    try {
      await action((error) => reported = error);
    } on clerk.ClerkError catch (error) {
      reported = error;
    } catch (error, stack) {
      debugPrint('[clerk auth] unexpected $error\n$stack');
      return AuthFailure.fromException(error);
    }

    final error = reported;
    if (error == null) {
      return null;
    }
    final failure = AuthFailure.fromClerk(error);
    debugPrint(
      '[clerk auth] sso kind=${failure.kind.name} code=${failure.code} '
      'detail=${error.errors?.errorMessage} raw=${_rawCodes(error)}',
    );
    return failure;
  }

  /// Runs an auth action, translating failures into a presentable [AuthFailure].
  ///
  /// The raw Clerk error is logged in full on purpose: `ClerkError.message` is a
  /// template (`'{arg} (ERROR RECEIVED FROM SERVER)'`) whose real text lives in
  /// `argument`/`errors`, so logging only `message` - as this used to - hid the
  /// cause from both the log and the user.
  Future<AuthFailure?> _run(Future<void> Function() action) async {
    try {
      await action();
      return null;
    } on clerk.ClerkError catch (error) {
      final failure = AuthFailure.fromClerk(error);
      debugPrint(
        '[clerk auth] kind=${failure.kind.name} code=${failure.code} '
        'detail=${error.errors?.errorMessage} '
        'raw=${_rawCodes(error)}',
      );
      return failure;
    } catch (error, stack) {
      debugPrint('[clerk auth] unexpected $error\n$stack');
      return AuthFailure.fromException(error);
    }
  }

  /// Every code the server returned, for the log line.
  String _rawCodes(clerk.ClerkError error) =>
      error.errors?.errors
          ?.map((e) => '${e.code}: ${e.fullMessage}')
          .join(' | ') ??
      'none';

  Future<clerk.SessionToken?> _fetchToken() async {
    if (!_authState.isSignedIn) {
      return null;
    }
    final token = await _authState.sessionToken(templateName: jwtTemplateName);
    _cachedToken = token;
    return token;
  }
}
