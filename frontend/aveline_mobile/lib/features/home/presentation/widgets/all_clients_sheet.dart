import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/client_highlight.dart';
import 'client_highlight_tile.dart';

/// The full direct-client list, behind "See all" on Home.
///
/// The row on Home is a glance at the five most active clients; this is where
/// the rest of them, and the reason each one is here, can be read.
Future<void> showAllClientsSheet(
  BuildContext context, {
  required List<ClientHighlight> clients,
}) {
  return showModalBottomSheet<void>(
    context: context,
    backgroundColor: Colors.transparent,
    isScrollControlled: true,
    builder: (context) => _AllClientsSheet(clients: clients),
  );
}

class _AllClientsSheet extends StatelessWidget {
  const _AllClientsSheet({required this.clients});

  final List<ClientHighlight> clients;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return SafeArea(
      top: false,
      child: Container(
        margin: const EdgeInsets.all(12),
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 16),
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: BorderRadius.circular(24),
          boxShadow: [
            BoxShadow(
              color: const Color(0xFF8B2E42).withValues(alpha: 0.12),
              blurRadius: 28,
              offset: const Offset(0, 10),
            ),
          ],
        ),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Center(
              child: Container(
                width: 40,
                height: 4,
                decoration: BoxDecoration(
                  color: scheme.outlineVariant,
                  borderRadius: BorderRadius.circular(2),
                ),
              ),
            ),
            const SizedBox(height: 18),
            Row(
              crossAxisAlignment: CrossAxisAlignment.baseline,
              textBaseline: TextBaseline.alphabetic,
              children: [
                Expanded(
                  child: Text('Clients', style: theme.textTheme.headlineSmall),
                ),
                Text(
                  clients.length == 1
                      ? '1 with activity'
                      : '${clients.length} with activity',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Flexible(
              child: ListView.separated(
                shrinkWrap: true,
                padding: EdgeInsets.zero,
                itemCount: clients.length,
                separatorBuilder: (context, index) => const SizedBox(height: 2),
                itemBuilder: (context, index) =>
                    _ClientRow(client: clients[index]),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _ClientRow extends StatelessWidget {
  const _ClientRow({required this.client});

  final ClientHighlight client;

  void _open(BuildContext context) {
    AppToast.show(context, "${client.shortName}'s profile is not on mobile yet.");
    Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      type: MaterialType.transparency,
      child: InkWell(
        onTap: () => _open(context),
        borderRadius: BorderRadius.circular(16),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 8),
          child: Row(
            children: [
              ClientAvatar(client: client, size: 44),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            client.name,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleMedium,
                          ),
                        ),
                        const SizedBox(width: 8),
                        ClientTierChip(tier: client.tier),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(
                      client.activity,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
