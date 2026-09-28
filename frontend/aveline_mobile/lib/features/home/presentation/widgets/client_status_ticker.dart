import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../domain/client_highlight.dart';
import 'client_highlight_tile.dart';

/// The status behind the client row, one client at a time, rotating on its own.
///
/// The row above is a glance at who is on the line; this is the sentence behind
/// those activity dots. One at a time keeps the text readable at full width,
/// where a line under a 76dp avatar would be cut to two words.
class ClientStatusTicker extends StatefulWidget {
  const ClientStatusTicker({super.key, required this.clients});

  final List<ClientHighlight> clients;

  /// How long a status holds before the next one rises into place.
  static const Duration hold = Duration(seconds: 5);

  /// How long the changeover takes.
  static const Duration transition = Duration(milliseconds: 420);

  @override
  State<ClientStatusTicker> createState() => _ClientStatusTickerState();
}

class _ClientStatusTickerState extends State<ClientStatusTicker> {
  Timer? _timer;

  int _index = 0;

  /// The statuses worth rotating: everyone in the row.
  ///
  /// There is no read marker in the schema, so there is no "new" subset to
  /// prefer; a dot that can never clear would be a worse lie than no dot.
  late List<ClientHighlight> _statuses = List.of(widget.clients);

  static List<ClientHighlight> _pick(List<ClientHighlight> clients) =>
      List.of(clients);

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _syncTimer();
  }

  @override
  void didUpdateWidget(covariant ClientStatusTicker oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!listEquals(oldWidget.clients, widget.clients)) {
      _statuses = _pick(widget.clients);
      _index = _statuses.isEmpty
          ? 0
          : _index.clamp(0, _statuses.length - 1);
      _syncTimer();
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  /// Rotation is ambient, not information: under reduced motion the card holds
  /// the newest status and the reader taps through the rest.
  void _syncTimer() {
    final rotating =
        _statuses.length > 1 && !MediaQuery.disableAnimationsOf(context);

    if (!rotating) {
      _timer?.cancel();
      _timer = null;
      return;
    }

    _timer ??= Timer.periodic(ClientStatusTicker.hold, (_) => _advance());
  }

  void _advance() {
    if (!mounted || _statuses.length < 2) {
      return;
    }
    setState(() => _index = (_index + 1) % _statuses.length);
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    if (_statuses.isEmpty) {
      return const SizedBox.shrink();
    }

    final client = _statuses[_index];

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          // Tapping moves on, so the card is usable without waiting for it.
          onTap: _statuses.length > 1 ? _advance : null,
          borderRadius: BorderRadius.circular(16),
          child: Container(
            padding: const EdgeInsets.all(16),
            decoration: BoxDecoration(
              color: scheme.surfaceContainerLowest,
              borderRadius: BorderRadius.circular(16),
              border: Border.all(
                color: scheme.outlineVariant.withValues(alpha: 0.5),
              ),
            ),
            child: AnimatedSwitcher(
              duration: ClientStatusTicker.transition,
              switchInCurve: Curves.easeOutCubic,
              switchOutCurve: Curves.easeInCubic,
              transitionBuilder: (child, animation) => FadeTransition(
                opacity: animation,
                child: SlideTransition(
                  position: Tween<Offset>(
                    begin: const Offset(0, 0.28),
                    end: Offset.zero,
                  ).animate(animation),
                  child: child,
                ),
              ),
              child: KeyedSubtree(
                key: ValueKey(client.id),
                child: _Snapshot(client: client),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// One status: who, and what is waiting on them.
class _Snapshot extends StatelessWidget {
  const _Snapshot({required this.client});

  final ClientHighlight client;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            ClientAvatar(client: client, size: 32),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                client.shortName,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.titleMedium,
              ),
            ),
            if (client.tier != null)
              Text(
                clientTierLabel(client.tier!),
                style: theme.textTheme.labelSmall?.copyWith(
                  color: clientTierColor(client.tier!, scheme),
                  fontWeight: FontWeight.w700,
                  letterSpacing: 0.6,
                ),
              ),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          client.activity,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: theme.textTheme.bodySmall?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
      ],
    );
  }
}
