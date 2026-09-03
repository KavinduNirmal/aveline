import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../auth/domain/auth_repository.dart';

/// Post-auth landing screen for the signed-in user.
class HomeScreen extends StatelessWidget {
  const HomeScreen({super.key});

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final auth = context.read<AuthRepository>();
    final user = auth.currentUser;

    return Scaffold(
      appBar: AppBar(title: const Text('Aveline')),
      body: Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (user?.imageUrl case final url?)
              CircleAvatar(radius: 40, backgroundImage: NetworkImage(url)),
            const SizedBox(height: 12),
            Text(user?.displayName ?? 'Welcome',
                style: theme.textTheme.headlineSmall),
            const SizedBox(height: 4),
            Text(user?.email ?? '', style: theme.textTheme.bodyMedium),
            if (user?.userRole case final userRole?) ...[
              const SizedBox(height: 4),
              Text('Team role: $userRole'),
            ],
            if (user?.orgRole case final orgRole?) ...[
              const SizedBox(height: 4),
              Text('Store role: $orgRole'),
            ],
            const SizedBox(height: 24),
            FilledButton.icon(
              onPressed: auth.signOut,
              icon: const Icon(Icons.logout),
              label: const Text('Sign out'),
            ),
          ],
        ),
      ),
    );
  }
}
