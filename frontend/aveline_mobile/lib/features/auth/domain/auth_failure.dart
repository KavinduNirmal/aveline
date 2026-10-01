import 'dart:async';

import 'package:clerk_auth/clerk_auth.dart' as clerk;
import 'package:flutter/foundation.dart';

/// What a failure means, for callers that branch on it.
enum AuthFailureKind {
  /// The identifier and password pair did not match.
  invalidCredentials,

  /// No account exists for the identifier that was submitted.
  identifierNotFound,

  /// The account exists but has no password (it signs in with an OAuth provider).
  oauthOnlyAccount,

  /// Too many failed attempts; the account is temporarily locked.
  lockedOut,

  /// The instance is rate limiting this client.
  rateLimited,

  /// The chosen password is known to have appeared in a data breach.
  passwordCompromised,

  /// The password does not satisfy the instance's password policy.
  weakPassword,

  /// The email or username is already registered.
  identifierTaken,

  /// The one-time verification code was wrong or expired.
  invalidCode,

  /// The instance requires a CAPTCHA this client cannot complete.
  captchaRequired,

  /// The device could not reach the instance.
  network,

  /// The instance rejected the request for a reason we do not map.
  server,

  /// Anything else.
  unknown,
}

/// A user-presentable auth failure.
///
/// Clerk's `ClerkError.message` is a *template* - `'{arg} (ERROR RECEIVED FROM
/// SERVER)'` - whose real text is parked in `argument`/`errors`. Rendering
/// `message` directly shows the user the placeholder, so [title], [message] and
/// [field] are derived from the machine code instead, and [code] is kept
/// verbatim for logs and telemetry.
@immutable
class AuthFailure {
  /// Construct an [AuthFailure].
  const AuthFailure({
    required this.kind,
    required this.title,
    required this.message,
    this.code,
    this.field,
    this.actionLabel,
    this.action,
  });

  /// The class of failure.
  final AuthFailureKind kind;

  /// Short headline, e.g. `Wrong password`.
  final String title;

  /// One sentence the user can act on.
  final String message;

  /// The raw Clerk/machine code, kept for logs.
  final String? code;

  /// The form field this failure belongs to (`identifier`, `password`, `code`,
  /// `username`), or `null` when it belongs to the form as a whole. A failure
  /// with a field is shown inline rather than in a toast that disappears.
  final String? field;

  /// Optional action label, paired with [action].
  final String? actionLabel;

  /// What the optional action does.
  final VoidCallback? action;

  /// Maps a Clerk SDK error onto actionable copy.
  factory AuthFailure.fromClerk(clerk.ClerkError error) {
    final list = error.errors?.errors;
    final first = (list != null && list.isNotEmpty) ? list.first : null;
    final code = first?.code;
    final copy = _copy[code];
    final paramName = first?.meta?['param_name'];
    return AuthFailure(
      kind: copy?.kind ?? AuthFailureKind.server,
      title: copy?.title ?? 'Sign-in failed',
      // An unmapped code falls back to Clerk's own long/message text, which is
      // written for people - never back to the `{arg}` template.
      message:
          copy?.message ?? error.errors?.errorMessage ?? 'Please try again.',
      code: code ?? error.code.name,
      field: copy?.field ?? (paramName is String ? paramName : null),
    );
  }

  /// Maps a non-Clerk throwable (socket, timeout, parse) onto copy.
  ///
  /// `dart:io` is deliberately avoided: this app also builds for web, where
  /// importing it fails to compile.
  factory AuthFailure.fromException(Object error) {
    final name = error.runtimeType.toString();
    final offline = error is TimeoutException ||
        name.contains('SocketException') ||
        name.contains('ClientException') ||
        name.contains('HandshakeException');
    return AuthFailure(
      kind: offline ? AuthFailureKind.network : AuthFailureKind.unknown,
      title: offline ? 'No connection' : 'Something went wrong',
      message: offline
          ? 'We could not reach Aveline. Check your connection and try again.'
          : 'Please try again in a moment.',
      code: name,
    );
  }

  @override
  String toString() => '${code ?? kind.name}: $title - $message';
}

/// What the Clerk instance allows, so the UI stops hard-coding it.
///
/// Read from the instance environment ([clerk.Environment]) rather than assumed:
/// the production and development instances disagree about whether `username`
/// is an identifier, and both require longer passwords than the form used to
/// accept.
@immutable
class AuthCapabilities {
  /// Construct [AuthCapabilities].
  const AuthCapabilities({
    this.usernameEnabled = false,
    this.usernameRequired = false,
    this.passwordMinLength = 8,
    this.signUpCaptchaRequired = false,
  });

  /// Whether the instance accepts a username at all.
  final bool usernameEnabled;

  /// Whether the instance requires a username.
  final bool usernameRequired;

  /// The instance's minimum password length.
  final int passwordMinLength;

  /// Whether sign-up is gated behind a CAPTCHA. The Clerk Flutter SDK has no
  /// way to send a captcha token, so a sign-up attempted in this state is
  /// guaranteed to fail; the form refuses rather than trying.
  final bool signUpCaptchaRequired;

  /// Conservative defaults for tests and for the window before the instance
  /// environment has loaded.
  static const AuthCapabilities unknown = AuthCapabilities();
}

/// Copy for the Clerk codes we can be specific about.
///
/// Codes absent from this map fall through to Clerk's own user-facing text, so
/// a code that is not listed here still reads correctly.
class _AuthCopy {
  const _AuthCopy(this.kind, this.title, this.message, [this.field]);

  final AuthFailureKind kind;
  final String title;
  final String message;
  final String? field;
}

const Map<String, _AuthCopy> _copy = {
  'form_identifier_not_found': _AuthCopy(
    AuthFailureKind.identifierNotFound,
    'Account not found',
    'No account matches that email. Check it, or create one.',
    'identifier',
  ),
  'form_password_incorrect': _AuthCopy(
    AuthFailureKind.invalidCredentials,
    'Wrong password',
    'That password is incorrect. Try again, or reset it.',
    'password',
  ),
  'form_param_format_invalid': _AuthCopy(
    AuthFailureKind.identifierNotFound,
    'Check that entry',
    'That does not look like a valid email address.',
    'identifier',
  ),
  'form_identifier_exists': _AuthCopy(
    AuthFailureKind.identifierTaken,
    'Already registered',
    'An account already exists for that email. Sign in instead.',
    'identifier',
  ),
  'form_param_taken': _AuthCopy(
    AuthFailureKind.identifierTaken,
    'Already taken',
    'That username is taken. Try another.',
    'username',
  ),
  'form_password_pwned': _AuthCopy(
    AuthFailureKind.passwordCompromised,
    'Password not allowed',
    'That password has appeared in a data breach. Choose a different one.',
    'password',
  ),
  'form_password_length_too_short': _AuthCopy(
    AuthFailureKind.weakPassword,
    'Password too short',
    'That password is shorter than this instance allows.',
    'password',
  ),
  'strategy_for_user_invalid': _AuthCopy(
    AuthFailureKind.oauthOnlyAccount,
    'Password sign-in unavailable',
    'This account was created with Google and has no password. Ask an administrator to set one.',
    'identifier',
  ),
  'user_locked': _AuthCopy(
    AuthFailureKind.lockedOut,
    'Account temporarily locked',
    'Too many failed attempts. Try again in about an hour.',
  ),
  'too_many_requests': _AuthCopy(
    AuthFailureKind.rateLimited,
    'Too many attempts',
    'Wait a moment, then try again.',
  ),
  'captcha_missing_token': _AuthCopy(
    AuthFailureKind.captchaRequired,
    'Bot check required',
    'This build cannot complete the CAPTCHA this instance requires. Contact support.',
  ),
  'form_code_incorrect': _AuthCopy(
    AuthFailureKind.invalidCode,
    'Incorrect code',
    'That code is not right. Check it and try again.',
    'code',
  ),
  'verification_expired': _AuthCopy(
    AuthFailureKind.invalidCode,
    'Code expired',
    'Request a new code and try again.',
    'code',
  ),
  'session_exists': _AuthCopy(
    AuthFailureKind.unknown,
    'Already signed in',
    'A session already exists on this device.',
  ),
};
