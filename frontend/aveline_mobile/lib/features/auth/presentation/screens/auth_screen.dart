import 'package:clerk_flutter/clerk_flutter.dart';
import 'package:flutter/material.dart';

import '../../../../core/theme/app_theme.dart';

/// Sign-in / sign-up entry screen, powered by the Clerk SDK's prebuilt UI.
///
/// The sign-in and sign-up methods shown are controlled by the Clerk
/// Dashboard instance settings.
class AuthScreen extends StatelessWidget {
  const AuthScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Scaffold(
      body: SafeArea(
        child: Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 420),
            child: ListView(
              shrinkWrap: true,
              padding: const EdgeInsets.all(24),
              children: [
                Icon(
                  Icons.storefront,
                  size: 64,
                  color: AppTheme.seedColor,
                ),
                const SizedBox(height: 8),
                Text(
                  'Aveline',
                  textAlign: TextAlign.center,
                  style: theme.textTheme.headlineMedium,
                ),
                const SizedBox(height: 24),
                const ClerkErrorListener(child: ClerkAuthentication()),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
