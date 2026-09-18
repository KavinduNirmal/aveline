import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../domain/thread_message.dart';

/// One message in a thread with a client.
///
/// Two axes decide how it looks, and they are deliberately independent:
///
/// - **The side is who spoke.** The client is on the left, the boutique on the
///   right. That is the only thing the alignment means.
/// - **The treatment is where it went.** A message that reached the client is a
///   filled bubble wearing its delivery tick. A note that never left the shop is
///   tinted, labelled `NOT SENT` and carries no tick. A reply an agent staged is
///   labelled `AWAITING APPROVAL` and carries its own decision, right where the
///   message will sit once it goes out.
///
/// The distinction matters more here than in a chat between two people: this
/// thread is the shop's record of what it told a client, and a note read as a sent
/// message would be the record lying.
class ThreadMessageBubble extends StatelessWidget {
  const ThreadMessageBubble({
    super.key,
    required this.message,
    this.onRetry,
    this.onApproveDraft,
    this.onDismissDraft,
  });

  final ThreadMessage message;

  /// Tries a message this device failed to send.
  final VoidCallback? onRetry;

  /// Releases a staged draft to the client.
  final VoidCallback? onApproveDraft;

  /// Drops a staged draft without sending it.
  final VoidCallback? onDismissDraft;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final staged = message.needsSignOff;
    final dismissed = message.status == MessageStatus.cancelled;
    final note = message.isInternalNote;

    final radius = BorderRadius.only(
      topLeft: const Radius.circular(16),
      topRight: const Radius.circular(16),
      bottomLeft: Radius.circular(message.isFromClient ? 6 : 16),
      bottomRight: Radius.circular(message.isFromClient ? 16 : 6),
    );

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Column(
        crossAxisAlignment: message.isFromClient
            ? CrossAxisAlignment.start
            : CrossAxisAlignment.end,
        children: [
          if (note || staged || dismissed) ...[
            _Overline(
              key: note
                  ? ValueKey('thread_note_${message.id}')
                  : staged
                  ? ValueKey('thread_draft_${message.id}')
                  : ValueKey('thread_dismissed_${message.id}'),
              label: note
                  ? 'NOTE · NOT SENT'
                  : staged
                  ? 'AWAITING APPROVAL'
                  : 'DISMISSED',
              color: note ? _noteInk(scheme) : scheme.primary,
            ),
            const SizedBox(height: 3),
          ],
          Row(
            mainAxisAlignment: message.isFromClient
                ? MainAxisAlignment.start
                : MainAxisAlignment.end,
            children: [
              Flexible(
                child: Container(
                  key: ValueKey('thread_message_${message.id}'),
                  constraints: BoxConstraints(
                    maxWidth: MediaQuery.of(context).size.width * 0.78,
                  ),
                  padding: const EdgeInsets.symmetric(
                    horizontal: 14,
                    vertical: 10,
                  ),
                  decoration: BoxDecoration(
                    color: _fill(scheme, note: note, dismissed: dismissed),
                    borderRadius: radius,
                    border: message.isFromClient || note
                        ? Border.all(
                            color: note
                                ? _noteInk(scheme).withValues(alpha: 0.35)
                                : scheme.outlineVariant,
                          )
                        : null,
                  ),
                  child: Text(
                    message.text,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: message.isFromClient
                          ? scheme.onSurface
                          : note || dismissed
                          ? scheme.onSurfaceVariant
                          : scheme.onPrimary,
                    ),
                  ),
                ),
              ),
            ],
          ),
          if (staged && (onApproveDraft != null || onDismissDraft != null)) ...[
            const SizedBox(height: 6),
            _DraftActions(
              messageId: message.id,
              onApprove: onApproveDraft,
              onDismiss: onDismissDraft,
            ),
          ] else ...[
            const SizedBox(height: 2),
            _Footer(message: message, onRetry: onRetry),
          ],
        ],
      ),
    );
  }

  /// The bubble's fill: the client's is paper, the boutique's is the brand, and a
  /// message the client never saw is neither.
  Color _fill(ColorScheme scheme, {required bool note, required bool dismissed}) {
    if (message.isFromClient) {
      return scheme.surfaceContainerLowest;
    }
    if (note || dismissed) {
      return scheme.surfaceContainerHigh;
    }
    return scheme.primary;
  }

  /// The ink a note wears, so "not sent" reads before the label is read.
  static Color _noteInk(ColorScheme scheme) => const Color(0xFF9A6B2F);
}

/// The small uppercase label above a bubble that needs one.
class _Overline extends StatelessWidget {
  const _Overline({super.key, required this.label, required this.color});

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 6),
      child: Text(
        label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: color,
          letterSpacing: 1.1,
          fontSize: 10,
        ),
      ),
    );
  }
}

/// The decision a staged reply is waiting on, attached to the reply itself.
class _DraftActions extends StatelessWidget {
  const _DraftActions({
    required this.messageId,
    this.onApprove,
    this.onDismiss,
  });

  final String messageId;
  final VoidCallback? onApprove;
  final VoidCallback? onDismiss;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(top: 2, right: 2),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          if (onDismiss != null)
            TextButton(
              key: ValueKey('thread_draft_dismiss_$messageId'),
              onPressed: onDismiss,
              child: const Text('Dismiss'),
            ),
          if (onApprove != null) ...[
            const SizedBox(width: 4),
            FilledButton(
              key: ValueKey('thread_draft_approve_$messageId'),
              onPressed: onApprove,
              child: const Text('Approve'),
            ),
          ],
        ],
      ),
    );
  }
}

/// What sits under a bubble: its time, its delivery tick, or why it did not go.
class _Footer extends StatelessWidget {
  const _Footer({required this.message, this.onRetry});

  final ThreadMessage message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final base = theme.textTheme.labelSmall?.copyWith(
      color: scheme.onSurfaceVariant,
      fontSize: 10,
    );

    if (message.isFailed) {
      return Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'Failed to send',
            style: base?.copyWith(color: scheme.error),
          ),
          if (onRetry != null)
            TextButton(
              key: ValueKey('thread_retry_${message.id}'),
              onPressed: onRetry,
              style: TextButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                minimumSize: const Size(0, 28),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
              child: const Text('Try again'),
            ),
        ],
      );
    }

    if (message.isSending) {
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 6),
        child: Text('Sending…', style: base),
      );
    }

    // A note went nowhere, so it has no delivery to report.
    final showTick = message.isFromBoutique && !message.isInternalNote;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 6),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(clockTime(message.createdAt), style: base),
          if (showTick) ...[
            const SizedBox(width: 5),
            _DeliveryTick(message: message),
          ],
        ],
      ),
    );
  }
}

/// The tick a message that reached the client wears.
///
/// Two ticks rather than one for anything the server accepted, and the read state
/// is the brand's colour rather than a different glyph: a second glyph would read
/// as a different thing rather than as the same thing further along.
class _DeliveryTick extends StatelessWidget {
  const _DeliveryTick({required this.message});

  final ThreadMessage message;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    final (state, color, label) = switch (message.status) {
      MessageStatus.read => ('read', scheme.primary, 'Read by the client'),
      MessageStatus.delivered => (
        'delivered',
        scheme.onSurfaceVariant,
        'Delivered',
      ),
      _ => ('sent', scheme.onSurfaceVariant, 'Sent'),
    };

    return Semantics(
      label: label,
      child: Icon(
        message.status == MessageStatus.sent
            ? Icons.done_rounded
            : Icons.done_all_rounded,
        key: ValueKey('thread_tick_${state}_${message.id}'),
        size: 14,
        color: color,
      ),
    );
  }
}
