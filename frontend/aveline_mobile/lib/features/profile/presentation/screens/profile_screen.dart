import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../auth/domain/auth_repository.dart';
import '../../../../shared/widgets/blossom.dart';

/// Profile dock tab: shows the signed-in user's identity, role claims, and a
/// sign-out action. Mirrors the web shell's user footer card.
class ProfileScreen extends StatelessWidget {
  const ProfileScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final auth = context.read<AuthRepository>();
    final user = auth.currentUser;

    return ListView(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
      children: [
        Text(
          'PROFILE',
          style: theme.textTheme.labelMedium?.copyWith(
            color: scheme.onSurfaceVariant,
            letterSpacing: 1.5,
          ),
        ),
        const SizedBox(height: 8),
        Text('Your account', style: theme.textTheme.headlineMedium),
        const SizedBox(height: 24),
        Container(
          padding: const EdgeInsets.all(24),
          decoration: BoxDecoration(
            color: scheme.surfaceContainerLowest,
            borderRadius: BorderRadius.circular(16),
            boxShadow: [
              BoxShadow(
                color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
                blurRadius: 20,
                offset: const Offset(0, 4),
              ),
            ],
          ),
          child: Column(
            children: [
              if (user?.imageUrl case final url?)
                CircleAvatar(
                  radius: 40,
                  backgroundImage: NetworkImage(url),
                )
              else
                CircleAvatar(
                  radius: 40,
                  backgroundColor: scheme.primaryContainer.withValues(alpha: 0.15),
                  child: Blossom(size: 40, color: scheme.primaryContainer),
                ),
              const SizedBox(height: 16),
              Text(
                user?.displayName ?? 'Welcome',
                style: theme.textTheme.titleLarge,
                textAlign: TextAlign.center,
              ),
              const SizedBox(height: 4),
              Text(
                user?.email ?? '',
                style: theme.textTheme.bodyMedium?.copyWith(
                  color: scheme.onSurfaceVariant,
                ),
                textAlign: TextAlign.center,
              ),
              if (user?.userRole case final userRole?) ...[
                const SizedBox(height: 12),
                _RoleChip(label: 'Team role: $userRole'),
              ],
              if (user?.orgRole case final orgRole?) ...[
                const SizedBox(height: 8),
                _RoleChip(label: 'Store role: $orgRole'),
              ],
              const SizedBox(height: 24),
              SizedBox(
                width: double.infinity,
                child: FilledButton.icon(
                  onPressed: auth.signOut,
                  icon: const Icon(Icons.logout),
                  label: const Text('Sign out'),
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }
}

class _RoleChip extends StatelessWidget {
  const _RoleChip({required this.label});

  final String label;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.08),
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelMedium?.copyWith(
              color: scheme.primary,
            ),
      ),
    );
  }
}
