import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/auth_repository.dart';
import '../../domain/sign_up_field.dart';
import '../../domain/social_auth_repository.dart';
import '../../domain/social_provider.dart';
import 'social_provider_mark.dart';

/// The ways into the app that do not use a password.
///
/// Renders one button per provider the *instance* has enabled - production has
/// Google, the development instance also has Facebook - so the screen never
/// offers a provider that cannot complete a sign-in.
///
/// A social sign-in is not always a whole sign-in. Both Aveline instances require
/// a username, which no provider hands over, so after the consent page a new
/// account is left unfinished. This widget finishes it: it asks for whatever Clerk
/// reports as still missing, and for an email code when the provider's address
/// arrived unverified. When the instance wants something the app cannot collect,
/// it says so instead of leaving the account half-made.
///
/// The consent page itself belongs to the Clerk SDK: it opens the provider in a
/// web view, intercepts the redirect and applies the user agent Google's embedded
/// browser rules need. Reimplementing that here would be a fork of SDK internals
/// that rots on the next upgrade, so this widget hands the SDK the context to
/// show it over and nothing else.
class SocialAuthButtons extends StatefulWidget {
  /// Construct a [SocialAuthButtons].
  const SocialAuthButtons({super.key, required this.isSignUp});

  /// Whether the buttons create an account (the sign-up tab) or sign in to an
  /// existing one.
  final bool isSignUp;

  @override
  State<SocialAuthButtons> createState() => _SocialAuthButtonsState();
}

/// Which part of the flow is on screen.
enum _Step {
  /// The provider buttons.
  providers,

  /// A prompt for the details the provider could not supply.
  details,

  /// The email code that confirms a newly created account.
  emailCode,
}

class _SocialAuthButtonsState extends State<SocialAuthButtons> {
  final _detailsFormKey = GlobalKey<FormState>();
  final _codeFormKey = GlobalKey<FormState>();
  final _codeController = TextEditingController();

  /// One controller per detail asked for, kept across rebuilds so a value the
  /// user typed survives a re-render.
  final _detailControllers = <SignUpField, TextEditingController>{};

  _Step _step = _Step.providers;
  bool _busy = false;

  /// The provider whose consent page is open, so only its button shows a spinner.
  SocialProvider? _running;

  /// The last server-side failure, so a rejection that names a field lands on
  /// that input rather than in a toast that vanishes before it is read.
  AuthFailure? _failure;

  /// The provider that started the unfinished sign-up, for the copy that explains
  /// why more is being asked for.
  SocialProvider? _startedWith;

  @override
  void dispose() {
    _codeController.dispose();
    for (final controller in _detailControllers.values) {
      controller.dispose();
    }
    super.dispose();
  }

  TextEditingController _controllerFor(SignUpField field) =>
      _detailControllers.putIfAbsent(field, TextEditingController.new);

  /// The repository as the social half of the contract, or `null` when it is one
  /// that only knows about passwords - a test fake, or a build with no social
  /// providers to offer.
  ///
  /// The cast is spelled out because Dart cannot promote between two unrelated
  /// interfaces: neither half of the contract is a subtype of the other, so an
  /// `is` test on its own leaves the static type unchanged.
  static SocialAuthRepository? _ssoOf(AuthRepository auth) =>
      auth is SocialAuthRepository ? auth as SocialAuthRepository : null;

  /// Shows a failure where the user can act on it.
  ///
  /// A failure that names a field belongs on that input, which is why the form to
  /// validate is passed in: the screen has more than one.
  void _report(AuthFailure failure, {GlobalKey<FormState>? form}) {
    if (form != null && failure.field != null) {
      setState(() => _failure = failure);
      form.currentState?.validate();
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

  /// Opens [provider]'s consent page and carries on from wherever it lands.
  Future<void> _start(SocialAuthRepository sso, SocialProvider provider) async {
    setState(() {
      _busy = true;
      _running = provider;
      _failure = null;
    });
    try {
      final failure = widget.isSignUp
          ? await sso.signUpWithOAuth(provider, context: context)
          : await sso.signInWithOAuth(provider, context: context);
      if (!mounted) return;
      if (failure != null) {
        _report(failure);
        return;
      }
      _startedWith = provider;
      await _advance(sso);
    } finally {
      if (mounted) {
        setState(() {
          _busy = false;
          _running = null;
        });
      }
    }
  }

  /// Moves to whatever the instance still wants.
  ///
  /// A finished flow needs none of this: the session exists, the auth state
  /// notifies its listeners, and the router takes the user on.
  Future<void> _advance(SocialAuthRepository sso) async {
    if (!mounted) return;

    if (sso.pendingSignUpFields.isNotEmpty) {
      setState(() {
        _step = _Step.details;
        _failure = null;
      });
      return;
    }

    if (sso.signUpIncomplete) {
      // Something is missing that this screen has no prompt for - a legal
      // acceptance, say. Saying so beats an account that never finishes.
      AppToast.show(
        context,
        'Your account still needs information this app cannot collect. '
            'Finish setting it up in the Aveline web app.',
        title: 'Almost there',
        error: true,
      );
      return;
    }

    if (!sso.signUpNeedsEmailVerification) {
      // Nothing left to ask for. Either the flow finished and a session exists -
      // in which case the router is already taking the user on - or it was a
      // plain sign-in that needed none of this. The buttons are the right thing
      // to leave on screen for the frame before that navigation happens.
      setState(() => _step = _Step.providers);
      return;
    }

    final failure = await context.read<AuthRepository>().sendEmailVerificationCode();
    if (!mounted) return;
    if (failure != null) {
      _report(failure);
      return;
    }
    setState(() {
      _step = _Step.emailCode;
      _failure = null;
    });
  }

  /// Sends the details the provider could not supply.
  Future<void> _submitDetails(SocialAuthRepository sso) async {
    if (_busy) {
      return;
    }
    if (!(_detailsFormKey.currentState?.validate() ?? false)) {
      return;
    }
    setState(() {
      _busy = true;
      _failure = null;
    });
    try {
      final values = {
        for (final field in sso.pendingSignUpFields)
          field: _controllerFor(field).text.trim(),
      };
      final failure = await sso.submitSignUpFields(values);
      if (!mounted) return;
      if (failure != null) {
        _report(failure, form: _detailsFormKey);
        return;
      }
      await _advance(sso);
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _verifyCode() async {
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
        _report(failure, form: _codeFormKey);
        return;
      }
      final sso = _ssoOf(auth);
      if (sso != null) {
        await _advance(sso);
      }
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  Future<void> _resendCode() async {
    if (_busy) {
      return;
    }
    setState(() => _busy = true);
    try {
      final failure = await context.read<AuthRepository>().sendEmailVerificationCode();
      if (!mounted) return;
      if (failure != null) {
        _report(failure, form: _codeFormKey);
        return;
      }
      AppToast.show(context, 'We sent another code to your email.');
    } finally {
      if (mounted) {
        setState(() => _busy = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final auth = context.read<AuthRepository>();
    final sso = _ssoOf(auth);
    final providers = auth.capabilities.socialProviders;

    // An instance with no social connections has no buttons to show, and a
    // repository that cannot run the flow has nothing to run: in both cases the
    // divider would separate the form from nothing.
    if (sso == null || providers.isEmpty) {
      return const SizedBox.shrink();
    }

    final theme = Theme.of(context);
    return switch (_step) {
      _Step.providers => _buildProviders(theme, providers),
      _Step.details => _buildDetails(theme, sso),
      _Step.emailCode => _buildEmailCode(theme),
    };
  }

  Widget _buildProviders(
    ThemeData theme,
    List<SocialProvider> providers,
  ) {
    final scheme = theme.colorScheme;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        // Room above the rule, so it reads as separating the form from the
        // providers rather than as a caption on the form's submit button.
        const SizedBox(height: 28),
        _OrDivider(theme: theme, scheme: scheme),
        const SizedBox(height: 20),
        for (final (index, provider) in providers.indexed) ...[
          if (index > 0) const SizedBox(height: 10),
          OutlinedButton(
            key: ValueKey('social-${provider.brand}'),
            onPressed: _busy ? null : () => _startWith(provider),
            style: OutlinedButton.styleFrom(
              padding: const EdgeInsets.symmetric(vertical: 12),
              foregroundColor: scheme.onSurface,
              side: BorderSide(color: scheme.outlineVariant),
            ),
            child: _running == provider
                ? const SizedBox(
                    height: 20,
                    width: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Row(
                    mainAxisAlignment: MainAxisAlignment.center,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      SocialProviderMark(provider: provider),
                      const SizedBox(width: 10),
                      // Flexible, because the label has to survive a long
                      // provider name, a narrow phone and a large font scale
                      // without overflowing the button. Google's brand
                      // guidelines require this wording rather than the
                      // provider's name on its own, so it is not shortened to
                      // make it fit.
                      Flexible(
                        child: Text(
                          'Continue with ${provider.name}',
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                    ],
                  ),
          ),
        ],
      ],
    );
  }

  /// Starts the flow, reading the repository at the moment of the tap.
  void _startWith(SocialProvider provider) {
    final sso = _ssoOf(context.read<AuthRepository>());
    if (sso != null) {
      _start(sso, provider);
    }
  }

  Widget _buildDetails(ThemeData theme, SocialAuthRepository sso) {
    final scheme = theme.colorScheme;
    final provider = _startedWith?.name ?? 'Your provider';
    return Form(
      key: _detailsFormKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'A little more information',
            textAlign: TextAlign.center,
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            '$provider signed you in, but this account needs '
            '${sso.pendingSignUpFields.length == 1 ? 'one more detail' : 'a few more details'} '
            'before it can be created.',
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 24),
          for (final field in sso.pendingSignUpFields) ...[
            _buildDetailField(theme, field),
            const SizedBox(height: 16),
          ],
          FilledButton(
            onPressed: _busy ? null : () => _submitDetails(sso),
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
                    'Continue',
                    style: theme.textTheme.titleMedium?.copyWith(
                      color: scheme.onPrimary,
                    ),
                  ),
          ),
        ],
      ),
    );
  }

  Widget _buildDetailField(ThemeData theme, SignUpField field) {
    return TextFormField(
      controller: _controllerFor(field),
      autocorrect: false,
      keyboardType: switch (field) {
        SignUpField.emailAddress => TextInputType.emailAddress,
        SignUpField.phoneNumber => TextInputType.phone,
        _ => TextInputType.text,
      },
      textInputAction: TextInputAction.next,
      onChanged: (_) {
        if (_failure?.field == field.fieldName) {
          setState(() => _failure = null);
        }
      },
      decoration: InputDecoration(
        labelText: field.label,
        prefixIcon: Icon(
          switch (field) {
            SignUpField.emailAddress => Icons.mail_outline,
            SignUpField.phoneNumber => Icons.phone_outlined,
            SignUpField.username => Icons.alternate_email_outlined,
            SignUpField.firstName || SignUpField.lastName => Icons.person_outline,
          },
        ),
      ),
      validator: (value) {
        if ((value?.trim() ?? '').isEmpty) {
          return 'Enter your ${field.label.toLowerCase()}';
        }
        return _failure?.field == field.fieldName ? _failure!.message : null;
      },
    );
  }

  Widget _buildEmailCode(ThemeData theme) {
    final scheme = theme.colorScheme;
    return Form(
      key: _codeFormKey,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Confirm your email',
            textAlign: TextAlign.center,
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            'We sent a one-time code to your email. Enter it below to '
            'activate your account.',
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
            onChanged: (_) {
              if (_failure?.field == 'code') {
                setState(() => _failure = null);
              }
            },
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
            onPressed: _busy ? null : _verifyCode,
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
            onPressed: _busy ? null : _resendCode,
            child: const Text('Resend code'),
          ),
        ],
      ),
    );
  }
}

/// The hairline-and-caption rule that separates the password form from the
/// provider buttons, matching the web's `OrDivider`.
class _OrDivider extends StatelessWidget {
  const _OrDivider({required this.theme, required this.scheme});

  final ThemeData theme;
  final ColorScheme scheme;

  @override
  Widget build(BuildContext context) {
    return Row(
      children: [
        Expanded(child: Divider(color: scheme.outlineVariant)),
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12),
          child: Text(
            'or continue with',
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              letterSpacing: 1.5,
            ),
          ),
        ),
        Expanded(child: Divider(color: scheme.outlineVariant)),
      ],
    );
  }
}
