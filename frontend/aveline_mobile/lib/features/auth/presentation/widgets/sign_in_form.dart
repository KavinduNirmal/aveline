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
    setState(() => _busy = true);

    try {
      final auth = context.read<AuthRepository>();
      final error = await auth.signInWithPassword(
        identifier: _identifierController.text.trim(),
        password: _passwordController.text,
      );
      if (!mounted) return;
      if (error != null) {
        AppToast.show(context, error, error: true);
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

  void _backToPassword() {
    setState(() => _needsSecondFactor = false);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (_needsSecondFactor) {
      return _SecondFactorStep(
        key: ValueKey('second-factor'),
        onBack: _backToPassword,
      );
    }
    return _buildPasswordStep(theme, scheme);
  }

  Widget _buildPasswordStep(ThemeData theme, ColorScheme scheme) {
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
            decoration: const InputDecoration(
              labelText: 'Email or username',
              hintText: 'you@example.com',
              prefixIcon: Icon(Icons.alternate_email_outlined),
            ),
            validator: (value) => (value == null || value.trim().isEmpty)
                ? 'Enter your email or username'
                : null,
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _passwordController,
            obscureText: true,
            textInputAction: TextInputAction.done,
            onFieldSubmitted: (_) => _submit(),
            decoration: const InputDecoration(
              labelText: 'Password',
              prefixIcon: Icon(Icons.lock_outline),
            ),
            validator: (value) =>
                (value == null || value.isEmpty) ? 'Enter your password' : null,
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
          const SizedBox(height: 16),
          Text(
            'Protected by Clerk · your session stays on this device',
            textAlign: TextAlign.center,
            style: theme.textTheme.bodySmall?.copyWith(
              color: scheme.onSurfaceVariant,
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
  final _codeController = TextEditingController();
  bool _busy = false;
  bool _codeSent = false;

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

  Future<void> _sendCode({bool auto = false}) async {
    if (_busy || _resendIn > 0) {
      return;
    }
    setState(() => _busy = true);
    try {
      final auth = context.read<AuthRepository>();
      final error = await auth.sendSecondFactorCode();
      if (!mounted) return;
      if (error != null) {
        AppToast.show(context, error, error: true);
      } else {
        setState(() {
          _codeSent = true;
          _resendIn = _resendCooldown;
        });
        _startTimer();
        if (!auto) {
          AppToast.show(context, 'Code sent to your email');
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
    if (_codeController.text.trim().isEmpty) {
      AppToast.show(context, 'Enter your verification code', error: true);
      return;
    }
    setState(() => _busy = true);
    try {
      final auth = context.read<AuthRepository>();
      final error = await auth.verifySecondFactorCode(
        code: _codeController.text.trim(),
      );
      if (!mounted) return;
      if (error != null) {
        AppToast.show(context, error, error: true);
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

    return Column(
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
        TextField(
          controller: _codeController,
          autofocus: true,
          keyboardType: TextInputType.number,
          textAlign: TextAlign.center,
          style: theme.textTheme.titleLarge?.copyWith(letterSpacing: 6),
          decoration: const InputDecoration(
            labelText: 'Verification code',
            prefixIcon: Icon(Icons.shield_outlined),
          ),
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
    );
  }
}
