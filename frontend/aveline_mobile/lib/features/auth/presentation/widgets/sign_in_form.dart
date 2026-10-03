import 'dart:async';

import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/auth_repository.dart';

/// Custom Clerk sign-in form (email/username + password).
///
/// When the account has a second factor (e.g. email/phone OTP, TOTP or a backup
/// code) enabled, the form advances to a one-time-code step after the password
/// is accepted.
///
/// Failures are reported by [AuthFailure], not by a bare string: a failure that
/// names a field is shown on that input, and anything else becomes a toast with
/// a headline and a sentence rather than a single opaque line.
class SignInForm extends StatefulWidget {
  const SignInForm({super.key});

  @override
  State<SignInForm> createState() => _SignInFormState();
}

class _SignInFormState extends State<SignInForm> {
  final _formKey = GlobalKey<FormState>();
  final _identifierController = TextEditingController();
  final _passwordController = TextEditingController();

  bool _busy = false;
  bool _needsSecondFactor = false;

  /// The last server-side failure, so a code that names a field can be shown on
  /// that input instead of in a toast that vanishes before it is acted on.
  AuthFailure? _failure;

  @override
  void dispose() {
    _identifierController.dispose();
    _passwordController.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    if (_busy) {
      return;
    }
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }
    setState(() {
      _busy = true;
      _failure = null;
    });

    try {
      final auth = context.read<AuthRepository>();
      final failure = await auth.signInWithPassword(
        identifier: _identifierController.text.trim(),
        password: _passwordController.text,
      );
      if (!mounted) return;
      if (failure != null) {
        _report(failure);
      } else if (auth.needsSecondFactor) {
        // Password accepted; a second factor is required to finish sign-in.
        setState(() => _needsSecondFactor = true);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  /// Shows a failure where the user can act on it.
  ///
  /// A failure that names a form field belongs on that input; anything else
  /// (lockout, rate limit, network) has no field to attach to and is toasted.
  void _report(AuthFailure failure) {
    if (failure.field != null) {
      setState(() => _failure = failure);
      _formKey.currentState?.validate();
      return;
    }
    AppToast.show(
      context,
      failure.message,
      title: failure.title,
      error: true,
      actionLabel: failure.actionLabel,
      onAction: failure.action,
    );
  }

  /// Clears an inline server error once the user edits the field it was on.
  void _clearFailure(String field) {
    if (_failure?.field == field) {
      setState(() => _failure = null);
    }
  }

  void _backToPassword() {
    setState(() => _needsSecondFactor = false);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (_needsSecondFactor) {
      return _SecondFactorStep(
        key: const ValueKey('second-factor'),
        onBack: _backToPassword,
      );
    }
    return _buildPasswordStep(theme, scheme);
  }

  Widget _buildPasswordStep(ThemeData theme, ColorScheme scheme) {
    // The instance decides whether a username is a valid identifier; the
    // production and development instances disagree, so the label follows it.
    final caps = context.read<AuthRepository>().capabilities;

    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextFormField(
            controller: _identifierController,
            keyboardType: TextInputType.emailAddress,
            textInputAction: TextInputAction.next,
            autocorrect: false,
            onChanged: (_) => _clearFailure('identifier'),
            decoration: InputDecoration(
              labelText: caps.usernameEnabled ? 'Email or username' : 'Email',
              hintText: 'you@example.com',
              prefixIcon: const Icon(Icons.alternate_email_outlined),
            ),
            validator: (value) {
              final v = value?.trim() ?? '';
              if (v.isEmpty) {
                return caps.usernameEnabled
                    ? 'Enter your email or username'
                    : 'Enter your email';
              }
              return _failure?.field == 'identifier' ? _failure!.message : null;
            },
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _passwordController,
            obscureText: true,
            textInputAction: TextInputAction.done,
            onChanged: (_) => _clearFailure('password'),
            onFieldSubmitted: (_) => _submit(),
            decoration: const InputDecoration(
              labelText: 'Password',
              prefixIcon: Icon(Icons.lock_outline),
            ),
            validator: (value) {
              if (value == null || value.isEmpty) {
                return 'Enter your password';
              }
              return _failure?.field == 'password' ? _failure!.message : null;
            },
          ),
          const SizedBox(height: 24),
          FilledButton(
            onPressed: _busy ? null : _submit,
            style: FilledButton.styleFrom(
              padding: const EdgeInsets.symmetric(vertical: 14),
            ),
            child: _busy
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : Text(
                    'Sign in',
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: scheme.onPrimary,
                    ),
                  ),
          ),
        ],
      ),
    );
  }
}

/// The one-time-code step shown when a second factor is required.
///
/// For code-based factors (email/phone) it automatically requests the code on
/// mount and shows a countdown before the code can be resent.
class _SecondFactorStep extends StatefulWidget {
  const _SecondFactorStep({super.key, required this.onBack});

  final VoidCallback onBack;

  @override
  State<_SecondFactorStep> createState() => _SecondFactorStepState();
}

class _SecondFactorStepState extends State<_SecondFactorStep> {
  final _formKey = GlobalKey<FormState>();
  final _codeController = TextEditingController();
  bool _busy = false;
  bool _codeSent = false;

  /// The last server-side failure, shown on the code input.
  AuthFailure? _failure;

  /// Seconds remaining before the code can be resent.
  int _resendIn = 0;
  Timer? _timer;

  static const _resendCooldown = 30;

  @override
  void initState() {
    super.initState();
    // Auto-request the code for code-based factors when the step appears.
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (mounted) _sendCode(auto: true);
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    _codeController.dispose();
    super.dispose();
  }

  /// Shows a failure where the user can act on it.
  void _report(AuthFailure failure) {
    if (failure.field != null) {
      setState(() => _failure = failure);
      _formKey.currentState?.validate();
      return;
    }
    AppToast.show(
      context,
      failure.message,
      title: failure.title,
      error: true,
      actionLabel: failure.actionLabel,
      onAction: failure.action,
    );
  }

  Future<void> _sendCode({bool auto = false}) async {
    if (_busy || _resendIn > 0) {
      return;
    }
    setState(() => _busy = true);
    try {
      final auth = context.read<AuthRepository>();
      final failure = await auth.sendSecondFactorCode();
      if (!mounted) return;
      if (failure != null) {
        _report(failure);
      } else {
        setState(() {
          _codeSent = true;
          _resendIn = _resendCooldown;
        });
        _startTimer();
        if (!auto) {
          final toPhone = auth.secondFactorStrategy == 'phone_code';
          AppToast.show(
            context,
            toPhone ? 'Code sent to your phone' : 'Code sent to your email',
          );
        }
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  void _startTimer() {
    _timer?.cancel();
    _timer = Timer.periodic(const Duration(seconds: 1), (timer) {
      if (!mounted) {
        timer.cancel();
        return;
      }
      setState(() {
        if (_resendIn > 0) {
          _resendIn--;
        } else {
          timer.cancel();
        }
      });
    });
  }

  Future<void> _verify() async {
    if (_busy) {
      return;
    }
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }
    setState(() {
      _busy = true;
      _failure = null;
    });
    try {
      final auth = context.read<AuthRepository>();
      final failure = await auth.verifySecondFactorCode(
        code: _codeController.text.trim(),
      );
      if (!mounted) return;
      if (failure != null) {
        _report(failure);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final strategy = context.read<AuthRepository>().secondFactorStrategy;
    final isTotp = strategy == 'totp';

    final String helper;
    if (isTotp) {
      helper =
          'Enter the code from your authenticator app to finish signing in.';
    } else if (strategy == 'phone_code') {
      helper = _codeSent
          ? 'We sent a one-time code to your phone. Enter it below to finish signing in.'
          : 'Requesting a code to your phone…';
    } else {
      helper = _codeSent
          ? 'We sent a one-time code to your email. Enter it below to finish signing in.'
          : 'Requesting a code to your email…';
    }

    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Two-step verification',
            textAlign: TextAlign.center,
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            helper,
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 24),
          TextFormField(
            controller: _codeController,
            autofocus: true,
            keyboardType: TextInputType.number,
            textAlign: TextAlign.center,
            style: theme.textTheme.titleLarge?.copyWith(letterSpacing: 6),
            onChanged: (_) {
              if (_failure?.field == 'code') {
                setState(() => _failure = null);
              }
            },
            decoration: const InputDecoration(
              labelText: 'Verification code',
              prefixIcon: Icon(Icons.shield_outlined),
            ),
            validator: (value) {
              if (value == null || value.trim().isEmpty) {
                return 'Enter your verification code';
              }
              return _failure?.field == 'code' ? _failure!.message : null;
            },
          ),
          const SizedBox(height: 24),
          FilledButton(
            onPressed: _busy ? null : _verify,
            style: FilledButton.styleFrom(
              padding: const EdgeInsets.symmetric(vertical: 14),
            ),
            child: _busy
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(
                      strokeWidth: 2,
                      color: Colors.white,
                    ),
                  )
                : Text(
                    'Verify & sign in',
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: scheme.onPrimary,
                    ),
                  ),
          ),
          if (!isTotp) ...[
            const SizedBox(height: 8),
            TextButton(
              onPressed: (_busy || _resendIn > 0) ? null : () => _sendCode(),
              child: Text(
                _resendIn > 0 ? 'Resend code in ${_resendIn}s' : 'Resend code',
              ),
            ),
          ],
          TextButton(
            onPressed: _busy ? null : widget.onBack,
            child: const Text('Back'),
          ),
        ],
      ),
    );
  }
}
