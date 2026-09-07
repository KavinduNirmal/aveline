import 'package:flutter/material.dart';

import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/blossom.dart';
import '../widgets/sign_in_form.dart';
import '../widgets/sign_up_form.dart';

enum _AuthMode { signIn, signUp }

/// Sign-in / sign-up entry screen built with Clerk's custom-flow APIs.
///
/// The sign-in and sign-up methods shown reflect the Clerk Dashboard instance
/// settings (email/username + password). Email verification, when required, is
/// handled inline by the forms.
class AuthScreen extends StatefulWidget {
  const AuthScreen({super.key});

  @override
  State<AuthScreen> createState() => _AuthScreenState();
}

class _AuthScreenState extends State<AuthScreen> {
  _AuthMode _mode = _AuthMode.signIn;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      body: Stack(
        children: [
          // Animated aurora + drifting blossoms (mirrors the web AuroraField).
          const Positioned.fill(child: AuroraField()),
          SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.symmetric(
                  horizontal: 24,
                  vertical: 24,
                ),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 460),
                  child: Column(
                    children: [
                      // Brand mark
                      Container(
                        width: 76,
                        height: 76,
                        alignment: Alignment.center,
                        decoration: BoxDecoration(
                          shape: BoxShape.circle,
                          color: scheme.primaryContainer.withValues(alpha: 0.12),
                          border: Border.all(
                            color: scheme.primaryContainer.withValues(alpha: 0.35),
                          ),
                        ),
                        child: Blossom(
                          size: 38,
                          color: scheme.primaryContainer,
                        ),
                      ),
                      const SizedBox(height: 12),
                      Text(
                        'Aveline',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.headlineMedium?.copyWith(
                          color: scheme.onSurface,
                        ),
                      ),
                      Text(
                        'AVELINE ASSISTANT',
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: scheme.onSurfaceVariant,
                          letterSpacing: 3,
                        ),
                      ),
                      const SizedBox(height: 28),

                      Card(
                        child: Padding(
                          padding: const EdgeInsets.all(20),
                          child: Column(
                            children: [
                              SegmentedButton<_AuthMode>(
                                segments: const [
                                  ButtonSegment(
                                    value: _AuthMode.signIn,
                                    label: Text('Sign in'),
                                    icon: Icon(Icons.login_outlined),
                                  ),
                                  ButtonSegment(
                                    value: _AuthMode.signUp,
                                    label: Text('Sign up'),
                                    icon: Icon(Icons.person_add_alt_1_outlined),
                                  ),
                                ],
                                selected: {_mode},
                                onSelectionChanged: (selection) {
                                  setState(() => _mode = selection.first);
                                },
                                showSelectedIcon: false,
                                style: ButtonStyle(
                                  visualDensity: VisualDensity.comfortable,
                                ),
                              ),
                              const SizedBox(height: 24),
                              AnimatedSwitcher(
                                duration: const Duration(milliseconds: 220),
                                switchInCurve: Curves.easeOut,
                                switchOutCurve: Curves.easeIn,
                                child: _mode == _AuthMode.signIn
                                    ? const SignInForm(key: ValueKey('signin'))
                                    : const SignUpForm(key: ValueKey('signup')),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}
