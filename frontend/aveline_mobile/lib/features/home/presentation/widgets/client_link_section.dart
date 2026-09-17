import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../../../shared/widgets/trailing_fade.dart';
import '../../domain/client_highlight.dart';
import 'all_clients_sheet.dart';
import 'client_highlight_tile.dart';
import 'quick_add_client_sheet.dart';

/// `Direct client link`: the clients with something happening right now,
/// leading with the walk-in slot so a new client can be added from the counter.
class ClientLinkSection extends StatefulWidget {
  const ClientLinkSection({super.key, required this.clients});

  final List<ClientHighlight> clients;

  /// The row is a glance, not a directory; the rest live behind "See all".
  static const int rowLimit = 5;

  static const double _tileGap = 14;

  @override
  State<ClientLinkSection> createState() => _ClientLinkSectionState();
}

class _ClientLinkSectionState extends State<ClientLinkSection> {
  final ScrollController _controller = ScrollController();

  late List<ClientHighlight> _clients = List.of(widget.clients);

  @override
  void didUpdateWidget(covariant ClientLinkSection oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!listEquals(oldWidget.clients, widget.clients)) {
      _clients = List.of(widget.clients);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  Future<void> _addWalkIn() async {
    final client = await showQuickAddClientSheet(context);
    if (client == null || !mounted) {
      return;
    }
    setState(() => _clients = [client, ..._clients]);
    AppToast.show(context, 'Added. ${client.shortName} is on the client list.');
  }

  void _openClient(ClientHighlight client) {
    AppToast.show(context, "${client.shortName}'s profile is not on mobile yet.");
  }

  @override
  Widget build(BuildContext context) {
    final visible = _clients.take(ClientLinkSection.rowLimit).toList();

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          child: Row(
            children: [
              const Expanded(child: SectionOverline('Direct client link')),
              _SeeAllButton(
                onTap: () => showAllClientsSheet(context, clients: _clients),
              ),
            ],
          ),
        ),
        const SizedBox(height: 8),
        SizedBox(
          height: ClientTileMetrics.height,
          child: TrailingFade(
            controller: _controller,
            child: ListView.separated(
              controller: _controller,
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 20),
              itemCount: visible.length + 1,
              separatorBuilder: (context, index) =>
                  const SizedBox(width: ClientLinkSection._tileGap),
              itemBuilder: (context, index) {
                if (index == 0) {
                  return AddClientTile(onTap: _addWalkIn);
                }
                final client = visible[index - 1];
                return ClientHighlightTile(
                  client: client,
                  onTap: () => _openClient(client),
                );
              },
            ),
          ),
        ),
      ],
    );
  }
}

/// The row's way out: everything behind the five clients on screen.
class _SeeAllButton extends StatelessWidget {
  const _SeeAllButton({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return TextButton(
      onPressed: onTap,
      style: TextButton.styleFrom(
        foregroundColor: theme.colorScheme.primary,
        // No trailing padding, so the chevron lines up with the content edge.
        padding: const EdgeInsets.fromLTRB(8, 6, 0, 6),
        minimumSize: const Size(48, 36),
        visualDensity: VisualDensity.compact,
        textStyle: theme.textTheme.labelMedium?.copyWith(
          fontWeight: FontWeight.w600,
        ),
      ),
      child: const Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text('See all'),
          SizedBox(width: 2),
          Icon(Icons.chevron_right_rounded, size: 18),
        ],
      ),
    );
  }
}
