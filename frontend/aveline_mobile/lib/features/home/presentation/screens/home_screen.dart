import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../auth/domain/auth_repository.dart';
import '../../../../shared/widgets/blossom.dart';

/// Home dock tab: a quiet-luxury greeting and boutique overview, mirroring the
/// web Overview section. Uses the brand serif for the greeting and soft cards
/// for the boutique's key numbers.
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final auth = context.read<AuthRepository>();
    final user = auth.currentUser;
    final firstName = user?.firstName;

    return ListView(
      padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 24),
      children: [
        Text(
          'AVELINE',
          style: theme.textTheme.labelMedium?.copyWith(
            color: scheme.onSurfaceVariant,
            letterSpacing: 1.5,
          ),
        ),
        const SizedBox(height: 8),
        Text(
          'Good day, ${firstName ?? 'there'}',
          style: theme.textTheme.headlineMedium,
        ),
        const SizedBox(height: 24),
        _OverviewCard(
          title: 'Blossoms',
          subtitle: 'Remaining this billing period.',
          trailing: Blossom(size: 20, color: scheme.primary),
          child: Text(
            'Demo mode',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ),
        const SizedBox(height: 12),
        _OverviewCard(
          title: 'Approvals',
          subtitle:
              'Pending order approvals land here once the commerce slice is built out.',
          child: Text(
            'No pending approvals.',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ),
      ],
    );
  }
}

/// A soft, elevated card used on the home overview.
class _OverviewCard extends StatelessWidget {
  const _OverviewCard({
    required this.title,
    required this.subtitle,
    required this.child,
    this.trailing,
  });

  final String title;
  final String subtitle;
  final Widget child;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      padding: const EdgeInsets.all(20),
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
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  title,
                  style: theme.textTheme.titleLarge?.copyWith(
                    fontFamily: 'Playfair Display',
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
              ?trailing,
            ],
          ),
          const SizedBox(height: 4),
          Text(
            subtitle,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 16),
          child,
        ],
      ),
    );
  }
}
