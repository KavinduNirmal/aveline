import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/auth_repository.dart';

/// Custom Clerk sign-up form (first/last name, optional username, email and
/// password), including the email verification-code step.
///
/// The form renders what the *instance* supports rather than what the
/// development instance happened to allow: production accepts no username and
/// both instances require a longer password than this form used to ask for.
/// Failures are reported by [AuthFailure], so a code that names a field lands on
/// that input instead of in a toast.
class SignUpForm extends StatefulWidget {
  const SignUpForm({super.key});

  @override
  State<SignUpForm> createState() => _SignUpFormState();
}

class _SignUpFormState extends State<SignUpForm> {
  final _formKey = GlobalKey<FormState>();
  final _codeFormKey = GlobalKey<FormState>();
  final _firstNameController = TextEditingController();
  final _lastNameController = TextEditingController();
  final _usernameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _codeController = TextEditingController();

  bool _busy = false;
  bool _verifying = false;

  /// The last server-side failure, so a code that names a field can be shown on
  /// that input instead of in a toast that vanishes before it is acted on.
  AuthFailure? _failure;

  @override
  void dispose() {
    _firstNameController.dispose();
    _lastNameController.dispose();
    _usernameController.dispose();
    _emailController.dispose();
    _passwordController.dispose();
    _codeController.dispose();
    super.dispose();
  }

  /// Shows a failure where the user can act on it.
  ///
  /// A failure that names a form field belongs on that input; anything else
  /// (rate limit, CAPTCHA, network) has no field to attach to and is toasted.
  void _report(AuthFailure failure) {
    if (failure.field != null) {
      setState(() => _failure = failure);
      (_verifying ? _codeFormKey : _formKey).currentState?.validate();
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

  /// Clears an inline server error once the user edits the fields it was on.
  void _clearFailure(List<String> fields) {
    if (fields.contains(_failure?.field)) {
      setState(() => _failure = null);
    }
  }

  /// The instance demands a CAPTCHA on sign-up and the Clerk Flutter SDK has no
  /// way to send one, so creating an account cannot succeed. Saying so plainly
  /// beats sending a request that is guaranteed to fail.
  void _reportCaptchaBlocked() {
    AppToast.show(
      context,
      'Sign-up is protected by a CAPTCHA this build cannot complete. '
          'Please contact support.',
      title: 'Sign-up unavailable',
      error: true,
    );
  }

  Future<void> _create() async {
    if (_busy) {
      return;
    }
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }
    final caps = context.read<AuthRepository>().capabilities;
    if (caps.signUpCaptchaRequired) {
      _reportCaptchaBlocked();
      return;
    }

    setState(() {
      _busy = true;
      _failure = null;
    });

    try {
      final auth = context.read<AuthRepository>();
      final failure = await auth.signUpWithPassword(
        emailAddress: _emailController.text.trim(),
        // Only send a username when the instance accepts one: production has it
        // off, the development instance requires it.
        username: caps.usernameEnabled ? _usernameController.text.trim() : null,
        firstName: _firstNameController.text.trim(),
        lastName: _lastNameController.text.trim(),
        password: _passwordController.text,
      );

      if (!mounted) return;

      if (failure != null) {
        _report(failure);
        return;
      }
      if (auth.isSignedIn) {
        // The instance created a session; the router takes it from here.
        return;
      }

      // Email verification required by the instance - start the code step.
      final sendFailure = await auth.sendEmailVerificationCode();
      if (!mounted) return;
      if (sendFailure != null) {
        _report(sendFailure);
        return;
      }
      setState(() {
        _busy = false;
        _failure = null;
        _verifying = true;
      });
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _resend() async {
    if (_busy) {
      return;
    }
    setState(() => _busy = true);
    try {
      final auth = context.read<AuthRepository>();
      final failure = await auth.sendEmailVerificationCode();
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

  Future<void> _verify() async {
    if (_busy) {
      return;
    }
    if (!(_codeFormKey.currentState?.validate() ?? false)) {
      return;
    }
    setState(() {
      _busy = true;
      _failure = null;
    });
    try {
      final auth = context.read<AuthRepository>();
      final failure = await auth.verifyEmailCode(
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
    final caps = context.read<AuthRepository>().capabilities;

    if (_verifying) {
      return Form(
        key: _codeFormKey,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Verify your email',
              textAlign: TextAlign.center,
              style: theme.textTheme.headlineSmall,
            ),
            const SizedBox(height: 8),
            Text(
              "We sent a one-time code to ${_emailController.text.trim()}. "
              'Enter it below to activate your account.',
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
              style: theme.textTheme.titleLarge?.copyWith(letterSpacing: 8),
              maxLength: 6,
              onChanged: (_) => _clearFailure(const ['code']),
              decoration: const InputDecoration(
                labelText: 'Verification code',
                counterText: '',
              ),
              validator: (value) {
                if (value == null || value.trim().isEmpty) {
                  return 'Enter the code we emailed you';
                }
                return _failure?.field == 'code' ? _failure!.message : null;
              },
            ),
            const SizedBox(height: 16),
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
                  : const Text('Verify & continue'),
            ),
            TextButton(
              onPressed: _busy ? null : _resend,
              child: const Text('Resend code'),
            ),
          ],
        ),
      );
    }

    return Form(
      key: _formKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                child: TextFormField(
                  controller: _firstNameController,
                  textCapitalization: TextCapitalization.words,
                  textInputAction: TextInputAction.next,
                  decoration: const InputDecoration(
                    labelText: 'First name',
                    prefixIcon: Icon(Icons.person_outline),
                  ),
                ),
              ),
              const SizedBox(width: 12),
              Expanded(
                child: TextFormField(
                  controller: _lastNameController,
                  textCapitalization: TextCapitalization.words,
                  textInputAction: TextInputAction.next,
                  decoration: const InputDecoration(labelText: 'Last name'),
                ),
              ),
            ],
          ),
          // Only rendered when the instance treats a username as an identifier.
          // Production has it off; the development instance requires it.
          if (caps.usernameEnabled) ...[
            const SizedBox(height: 16),
            TextFormField(
              controller: _usernameController,
              autocorrect: false,
              textInputAction: TextInputAction.next,
              onChanged: (_) => _clearFailure(const ['username', 'identifier']),
              decoration: const InputDecoration(
                labelText: 'Username',
                hintText: 'kasun_d',
                prefixIcon: Icon(Icons.alternate_email_outlined),
              ),
              validator: (value) {
                final v = value?.trim() ?? '';
                if (v.isEmpty) {
                  return caps.usernameRequired ? 'Choose a username' : null;
                }
                return _failure?.field == 'username' ? _failure!.message : null;
              },
            ),
          ],
          const SizedBox(height: 16),
          TextFormField(
            controller: _emailController,
            keyboardType: TextInputType.emailAddress,
            textInputAction: TextInputAction.next,
            autocorrect: false,
            onChanged: (_) =>
                _clearFailure(const ['identifier', 'email_address']),
            decoration: const InputDecoration(
              labelText: 'Email',
              hintText: 'you@example.com',
              prefixIcon: Icon(Icons.mail_outline),
            ),
            validator: (value) {
              final v = value?.trim() ?? '';
              if (v.isEmpty || !v.contains('@')) {
                return 'Enter a valid email address';
              }
              final field = _failure?.field;
              if (field == 'identifier' || field == 'email_address') {
                return _failure!.message;
              }
              return null;
            },
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _passwordController,
            obscureText: true,
            textInputAction: TextInputAction.done,
            onChanged: (_) => _clearFailure(const ['password']),
            onFieldSubmitted: (_) => _create(),
            decoration: InputDecoration(
              labelText: 'Password',
              helperText:
                  'At least ${caps.passwordMinLength} characters',
              prefixIcon: const Icon(Icons.lock_outline),
            ),
            validator: (value) {
              final v = value ?? '';
              if (v.length < caps.passwordMinLength) {
                return 'Password must be at least '
                    '${caps.passwordMinLength} characters';
              }
              return _failure?.field == 'password' ? _failure!.message : null;
            },
          ),
          if (caps.signUpCaptchaRequired) ...[
            const SizedBox(height: 16),
            _BlockedNotice(
              message: 'Sign-up is unavailable in this build: this account '
                  'instance requires a CAPTCHA it cannot complete.',
              scheme: scheme,
              theme: theme,
            ),
          ],
          const SizedBox(height: 24),
          FilledButton(
            onPressed: (_busy || caps.signUpCaptchaRequired) ? null : _create,
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
                    'Create account',
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

/// An inline, non-dismissable explanation for a form that cannot be submitted.
class _BlockedNotice extends StatelessWidget {
  const _BlockedNotice({
    required this.message,
    required this.scheme,
    required this.theme,
  });

  final String message;
  final ColorScheme scheme;
  final ThemeData theme;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.errorContainer,
        borderRadius: BorderRadius.circular(12),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.info_outline, size: 18, color: scheme.onErrorContainer),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              message,
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onErrorContainer,
              ),
            ),
          ),
        ],
      ),
    );
  }
}
