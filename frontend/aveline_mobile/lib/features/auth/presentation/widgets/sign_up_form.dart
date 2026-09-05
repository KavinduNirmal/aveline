import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../domain/auth_repository.dart';

/// Custom Clerk sign-up form (first/last name, username, email + password),
/// including the email verification-code step.
class SignUpForm extends StatefulWidget {
  const SignUpForm({super.key});

  @override
  State<SignUpForm> createState() => _SignUpFormState();
}

class _SignUpFormState extends State<SignUpForm> {
  final _formKey = GlobalKey<FormState>();
  final _firstNameController = TextEditingController();
  final _lastNameController = TextEditingController();
  final _usernameController = TextEditingController();
  final _emailController = TextEditingController();
  final _passwordController = TextEditingController();
  final _codeController = TextEditingController();

  bool _busy = false;
  bool _verifying = false;
  String? _error;

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

  Future<void> _create() async {
    if (!(_formKey.currentState?.validate() ?? false)) {
      return;
    }
    setState(() {
      _busy = true;
      _error = null;
    });

    final auth = context.read<AuthRepository>();
    final error = await auth.signUpWithPassword(
      emailAddress: _emailController.text.trim(),
      username: _usernameController.text.trim(),
      firstName: _firstNameController.text.trim(),
      lastName: _lastNameController.text.trim(),
      password: _passwordController.text,
    );

    if (!mounted) return;

    if (error == null && !auth.isSignedIn) {
      // Email verification required by the instance — start the code step.
      final sendError = await auth.sendEmailVerificationCode();
      if (!mounted) return;
      if (sendError == null) {
        setState(() {
          _busy = false;
          _verifying = true;
        });
        return;
      }
      setState(() {
        _busy = false;
        _error = sendError;
      });
      return;
    }

    setState(() {
      _busy = false;
      _error = error;
    });
  }

  Future<void> _resend() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    final auth = context.read<AuthRepository>();
    final error = await auth.sendEmailVerificationCode();
    if (!mounted) return;
    setState(() {
      _busy = false;
      _error = error;
    });
  }

  Future<void> _verify() async {
    setState(() {
      _busy = true;
      _error = null;
    });
    final auth = context.read<AuthRepository>();
    final error = await auth.verifyEmailCode(code: _codeController.text.trim());
    if (!mounted) return;
    setState(() {
      _busy = false;
      _error = error;
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (_verifying) {
      return Form(
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
            if (_error != null) ...[
              Container(
                padding: const EdgeInsets.all(12),
                decoration: BoxDecoration(
                  color: scheme.errorContainer,
                  borderRadius: BorderRadius.circular(12),
                ),
                child: Text(
                  _error!,
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: scheme.onErrorContainer,
                  ),
                ),
              ),
              const SizedBox(height: 16),
            ],
            TextField(
              controller: _codeController,
              autofocus: true,
              keyboardType: TextInputType.number,
              textAlign: TextAlign.center,
              style: theme.textTheme.titleLarge?.copyWith(letterSpacing: 8),
              maxLength: 6,
              decoration: const InputDecoration(
                labelText: 'Verification code',
                counterText: '',
              ),
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
          if (_error != null) ...[
            Container(
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: scheme.errorContainer,
                borderRadius: BorderRadius.circular(12),
              ),
              child: Text(
                _error!,
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onErrorContainer,
                ),
              ),
            ),
            const SizedBox(height: 16),
          ],
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
          const SizedBox(height: 16),
          TextFormField(
            controller: _usernameController,
            autocorrect: false,
            textInputAction: TextInputAction.next,
            decoration: const InputDecoration(
              labelText: 'Username',
              hintText: 'kasun_d',
              prefixIcon: Icon(Icons.alternate_email_outlined),
            ),
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _emailController,
            keyboardType: TextInputType.emailAddress,
            textInputAction: TextInputAction.next,
            autocorrect: false,
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
              return null;
            },
          ),
          const SizedBox(height: 16),
          TextFormField(
            controller: _passwordController,
            obscureText: true,
            textInputAction: TextInputAction.done,
            onFieldSubmitted: (_) => _create(),
            decoration: const InputDecoration(
              labelText: 'Password',
              prefixIcon: Icon(Icons.lock_outline),
            ),
            validator: (value) => (value == null || value.length < 8)
                ? 'Password must be at least 8 characters'
                : null,
          ),
          const SizedBox(height: 24),
          FilledButton(
            onPressed: _busy ? null : _create,
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
