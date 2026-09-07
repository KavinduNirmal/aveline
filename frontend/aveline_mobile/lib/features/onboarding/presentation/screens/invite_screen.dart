import 'package:aveline_mobile/core/providers/onboarding_provider.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/features/auth/domain/auth_repository.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/shared/widgets/aurora_field.dart';
import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

/// Handles an invitation deep link (`aveline://invite?code=…`).
///
/// When signed in it auto-accepts the code (skipping manual entry) and routes to
/// the home screen. When signed out it stores the code and lets the route guard
/// send the user to sign-in; after signing in, the staff invite-code step
/// prefills the stored code.
class InviteScreen extends StatefulWidget {
  const InviteScreen({super.key});

  @override
  State<InviteScreen> createState() => _InviteScreenState();
}

class _InviteScreenState extends State<InviteScreen> {
  bool _working = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _handle();
  }

  Future<void> _handle() async {
    final onboarding = context.read<OnboardingProvider>();
    final code = onboarding.pendingInviteCode;
    final authRepo = context.read<AuthRepository?>();

    if (!(authRepo?.isSignedIn ?? false)) {
      // Not signed in: the route guard redirects to /auth. Keep the code so the
      // invite-code step can prefill it after sign-in.
      return;
    }

    if (code == null || code.trim().isEmpty) {
      _goHomeOrSetup();
      return;
    }

    setState(() {
      _working = true;
      _error = null;
    });

    try {
      final dio = context.read<Dio>();
      final userProvider = context.read<UserProvider>();
      await userProvider.acceptInvitationCode(dio, code: code);
      onboarding.setPendingInviteCode(null);
      if (!mounted) return;
      final active =
          userProvider.user?.accountState == AvelineAccountState.active;
      context.go(active ? AppRoutes.home : AppRoutes.orgSetup);
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _working = false;
        _error = e.toString().replaceFirst('Exception: ', '');
      });
    }
  }

  void _goHomeOrSetup() {
    if (!mounted) return;
    final userProvider = context.read<UserProvider>();
    final active =
        userProvider.user?.accountState == AvelineAccountState.active;
    context.go(active ? AppRoutes.home : AppRoutes.orgSetup);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      appBar: AppBar(
        title: Text(
          'AVELINE',
          style: theme.textTheme.labelMedium?.copyWith(
            letterSpacing: 2.0,
            color: scheme.primary,
          ),
        ),
        centerTitle: true,
      ),
      body: Stack(
        children: [
          const Positioned.fill(child: AuroraField()),
          SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.symmetric(
                  horizontal: 24.0,
                  vertical: 16.0,
                ),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 480),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Icon(
                        _error == null
                            ? Icons.how_to_reg_outlined
                            : Icons.error_outline,
                        size: 56,
                        color: _error == null ? scheme.primary : scheme.error,
                      ),
                      const SizedBox(height: 16),
                      Text(
                        _error == null
                            ? 'Joining your boutique…'
                            : 'Unable to join',
                        style: theme.textTheme.headlineSmall?.copyWith(
                          color: scheme.onSurface,
                        ),
                        textAlign: TextAlign.center,
                      ),
                      const SizedBox(height: 8),
                      if (_error != null) ...[
                        Text(
                          _error!,
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: scheme.onSurfaceVariant,
                          ),
                          textAlign: TextAlign.center,
                        ),
                        const SizedBox(height: 20),
                        FilledButton(
                          onPressed: () => context.go(AppRoutes.orgSetup),
                          child: const Text('Enter the code manually'),
                        ),
                      ] else if (_working)
                        const Padding(
                          padding: EdgeInsets.symmetric(vertical: 24),
                          child: Center(child: CircularProgressIndicator()),
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
