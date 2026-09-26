import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../../../../shared/persona.dart';
import '../../../../shared/utils/date_formatter.dart';
import '../../domain/thread_message.dart';
import 'block_action_rail.dart';
import 'client_channel_surface.dart';
import 'conversation_avatar.dart';
import 'thread_blocks.dart';

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
///   message will sit once it goes out. A `SignOff` is classified by **kind
///   first**, so an approved one reads `APPROVED` rather than the note label it
///   would otherwise wear for being `Published`.
///
/// The distinction matters more here than in a chat between two people: this
/// thread is the shop's record of what it told a client, and a note read as a sent
/// message would be the record lying.
class ThreadMessageBubble extends StatelessWidget {
  const ThreadMessageBubble({
    super.key,
    required this.message,
    this.quotedParent,
    this.onRetry,
    this.onApproveDraft,
    this.onDismissDraft,
    this.onRevoke,
    this.onSelectCustomer,
    this.loadAttachment,
    this.openAttachment,
    this.bridge,
  });

  final ThreadMessage message;

  /// The message this one replies to, when it is in the window.
  ///
  /// A parent outside the window is omitted rather than fetched: a quoted line is
  /// tappable context, not a second read.
  final ThreadMessage? quotedParent;

  /// Tries a message this device failed to send.
  final VoidCallback? onRetry;

  /// Releases a staged draft to the client.
  final VoidCallback? onApproveDraft;

  /// Drops a staged draft without sending it.
  final VoidCallback? onDismissDraft;

  /// Returns an approved SignOff to the associate's queue. Shown only when the
  /// caller has the approval permission.
  final VoidCallback? onRevoke;

  /// Resolves a `choice` option's client.
  final void Function(ThreadBlock block, Map<String, dynamic> option)?
  onSelectCustomer;

  /// Fetches an attachment's bytes through the authenticated client.
  final Future<Uint8List> Function(String attachmentId)? loadAttachment;

  /// Opens a document through the platform viewer.
  final Future<void> Function(Uint8List bytes, String fileName)? openAttachment;

  /// The action rail's wiring for the thread the bubble is drawn in.
  ///
  /// Threaded down to the block renderer rather than read from a context, so the bubble
  /// stays drawable on its own and simply gets no rail when there is no thread behind it.
  final BlockActionBridge? bridge;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final staged = message.needsSignOff;
    final approved = message.isApprovedSignOff;
    // A dismissed message is one the associate dropped, whatever its kind: the seeded draft a
    // decision cancels is a `Note`, and it must still read DISMISSED rather than vanish.
    final dismissed = message.status == MessageStatus.cancelled;
    final note = message.isInternalNote;
    // The client's own words, relayed from their channel. The domain already promotes
    // the `client_message` block's text onto the message, so the surface draws that
    // and the handle names where it came from.
    final isChannel =
        message.isFromClient || message.clientMessageFrom != null;

    final radius = BorderRadius.only(
      topLeft: const Radius.circular(16),
      topRight: const Radius.circular(16),
      bottomLeft: Radius.circular(message.isFromStaff ? 16 : 6),
      bottomRight: Radius.circular(message.isFromStaff ? 6 : 16),
    );

    final overline = _overline(staged: staged, approved: approved, dismissed: dismissed, note: note);

    // A block-only message must never be empty: the `sign_off` block's own words stand in
    // for missing text, and a message with neither falls back to a plain label below.
    final body = message.text.isNotEmpty
        ? message.text
        : (message.signOffBlock != null
              ? threadBlockSummary(message.signOffBlock!)
              : '');

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 3),
      child: Column(
        crossAxisAlignment: message.isFromStaff
            ? CrossAxisAlignment.end
            : CrossAxisAlignment.start,
        children: [
          if (quotedParent != null) ...[
            _QuoteLine(
              key: ValueKey('thread_quote_${message.id}'),
              parent: quotedParent!,
            ),
            const SizedBox(height: 3),
          ],
          if (message.isFromAgent) ...[
            _AgentName(agentKey: message.agentKey),
            const SizedBox(height: 3),
          ],
          if (overline != null) ...[
            _Overline(
              key: ValueKey(overline.$2),
              label: overline.$1,
              color: note ? _noteInk(scheme) : scheme.primary,
            ),
            const SizedBox(height: 3),
          ],
          Row(
            mainAxisAlignment: message.isFromStaff
                ? MainAxisAlignment.end
                : MainAxisAlignment.start,
            crossAxisAlignment: CrossAxisAlignment.end,
            children: [
              if (message.isFromAgent) ...[
                const ConversationAvatar.aveline(size: 28),
                const SizedBox(width: 8),
              ],
              Flexible(
                child: ConstrainedBox(
                  constraints: BoxConstraints(
                    maxWidth: MediaQuery.of(context).size.width * 0.78,
                  ),
                  child: isChannel
                      ? _channelSurface(context, body, radius)
                      : Container(
                          key: ValueKey('thread_message_${message.id}'),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 9,
                          ),
                          decoration: BoxDecoration(
                            color: _fill(
                              scheme,
                              note: note,
                              dismissed: dismissed,
                            ),
                            borderRadius: radius,
                            border: !message.isFromStaff || note || dismissed
                                ? Border.all(
                                    color: note
                                        ? _noteInk(scheme).withValues(alpha: 0.35)
                                        : scheme.outlineVariant,
                                  )
                                : null,
                          ),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            mainAxisSize: MainAxisSize.min,
                            children: [
                              if (body.isNotEmpty ||
                                  message.bodyBlocks.isEmpty)
                                Text(
                                  body.isEmpty ? 'Update' : body,
                                  style: _bodyStyle(
                                    context,
                                    message.isFromStaff
                                        ? (note || dismissed
                                              ? scheme.onSurfaceVariant
                                              : scheme.onPrimary)
                                        : scheme.onSurface,
                                  ),
                                ),
                              ThreadMessageBlocks(
                                message: message,
                                onSelectCustomer: onSelectCustomer,
                                loadAttachment: loadAttachment,
                                openAttachment: openAttachment,
                                bridge: bridge,
                              ),
                            ],
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
          ] else if (approved && onRevoke != null) ...[
            const SizedBox(height: 4),
            _RevokeAction(messageId: message.id, onRevoke: onRevoke!),
          ] else ...[
            const SizedBox(height: 2),
            _Footer(message: message, onRetry: onRetry),
          ],
        ],
      ),
    );
  }

  /// The bubble's overline: the label and the widget key it wears.
  ///
  /// The order is deliberate. A `SignOff` is drawn by its kind before its status, so an
  /// approved one is `APPROVED` and not the `NOTE · NOT SENT` its `Published` status would
  /// otherwise earn; a dismissed one is `DISMISSED`; a staged one is `AWAITING APPROVAL`; and
  /// only everything else that is `Published` and not the client's is a note.
  (String, String)? _overline({
    required bool staged,
    required bool approved,
    required bool dismissed,
    required bool note,
  }) {
    if (message.kind == MessageKind.signOff) {
      if (staged) {
        return ('AWAITING APPROVAL', 'thread_draft_${message.id}');
      }
      if (approved) {
        return ('APPROVED', 'thread_approved_${message.id}');
      }
      if (dismissed) {
        return ('DISMISSED', 'thread_dismissed_${message.id}');
      }
    }
    if (staged) {
      return ('AWAITING APPROVAL', 'thread_draft_${message.id}');
    }
    if (note) {
      return ('NOTE · NOT SENT', 'thread_note_${message.id}');
    }
    if (dismissed) {
      return ('DISMISSED', 'thread_dismissed_${message.id}');
    }
    return null;
  }

  /// The bubble's fill: the associate's own is the brand, the counterparty's is
  /// paper, and a note that reached no customer channel is neither.
  Color _fill(ColorScheme scheme, {required bool note, required bool dismissed}) {
    if (note || dismissed) {
      return scheme.surfaceContainerHigh;
    }
    if (message.isFromStaff) {
      return scheme.primary;
    }
    // The counterparty's paper: the client's forwarded words and an agent's reply
    // share the card surface the web gives both.
    return scheme.surfaceContainerLowest;
  }

  /// The ink a note wears, so "not sent" reads before the label is read.
  static Color _noteInk(ColorScheme scheme) => const Color(0xFF9A6B2F);

  /// The customer's relayed message, drawn as the channel surface.
  ///
  /// The surface *is* the message here rather than a card inside a bubble: a bubble
  /// wrapped around the channel would be two frames for one message and would set the
  /// client's words a level deeper than everyone else's, which is the opposite of what
  /// the treatment is for. Anything else the message carries — a photo they sent —
  /// rides on the channel's canvas under their words.
  Widget _channelSurface(
    BuildContext context,
    String words,
    BorderRadius radius,
  ) {
    return ClientChannelSurface(
      key: ValueKey('thread_message_${message.id}'),
      text: words.isEmpty ? 'Update' : words,
      handle: message.clientMessageFrom,
      borderRadius: radius,
      child: ThreadMessageBlocks(
        message: message,
        onSelectCustomer: onSelectCustomer,
        loadAttachment: loadAttachment,
        openAttachment: openAttachment,
        bridge: bridge,
      ),
    );
  }
}

/// A message's prose: the web's `text-sm`.
///
/// A chat surface carries more words per screen than anything else in the app, and
/// the theme's 16px body made a thread read as a document rather than as a
/// conversation. The larger body sizes stay for the forms and panels they suit.
TextStyle? _bodyStyle(BuildContext context, Color ink) =>
    Theme.of(context).textTheme.bodyMedium?.copyWith(
          fontSize: 14,
          height: 1.55,
          color: ink,
        );

/// The name to credit an agent's words to, falling back to the umbrella brand.
String agentPersonaName(String? agentKey) => personaForAgent(agentKey).name;

/// The persona a reply is credited to, above its bubble.
///
/// The thread is the shop's record of who said what, and an agent's words read as
/// the umbrella brand's unless the bubble names the persona that wrote them.
class _AgentName extends StatelessWidget {
  const _AgentName({required this.agentKey});

  final String? agentKey;

  @override
  Widget build(BuildContext context) {
    // Each persona keeps its own accent, as it does in the Salon. A thread where every
    // agent's name is the same wine reads as one voice, and the point of crediting a
    // message to Elle or Ava is that they are not the same voice.
    final persona = personaForAgent(agentKey);

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 6),
      child: Text(
        persona.name,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
          color: persona.accent,
          fontSize: 11,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
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

/// One line of the message a reply answers.
class _QuoteLine extends StatelessWidget {
  const _QuoteLine({super.key, required this.parent});

  final ThreadMessage parent;

  /// Who the parent is from, in the words the thread uses for them.
  String get _who => switch (parent.author) {
    MessageAuthor.client => parent.clientMessageFrom ?? 'Client',
    MessageAuthor.staff => 'You',
    MessageAuthor.agent =>
      parent.agentKey == null || parent.agentKey!.isEmpty
          ? 'Aveline'
          : '${parent.agentKey![0].toUpperCase()}${parent.agentKey!.substring(1)}',
    MessageAuthor.system => 'Client',
  };

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final words = parent.text.isNotEmpty
        ? parent.text
        : (parent.blocks.isEmpty
              ? 'A message'
              : threadBlockSummary(parent.blocks.first));

    return Container(
      constraints: BoxConstraints(
        maxWidth: MediaQuery.of(context).size.width * 0.78,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
      decoration: BoxDecoration(
        color: scheme.surfaceContainer,
        borderRadius: BorderRadius.circular(8),
        border: Border(
          left: BorderSide(color: scheme.primary.withValues(alpha: 0.5), width: 2),
        ),
      ),
      child: Text(
        '$_who: $words',
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: theme.textTheme.labelSmall?.copyWith(
          color: scheme.onSurfaceVariant,
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

/// The way back from an approval, for a supervisor.
class _RevokeAction extends StatelessWidget {
  const _RevokeAction({required this.messageId, required this.onRevoke});

  final String messageId;
  final VoidCallback onRevoke;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(right: 2),
      child: TextButton(
        key: ValueKey('thread_revoke_$messageId'),
        onPressed: onRevoke,
        child: const Text('Revoke'),
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

    // A tick is a delivery claim, so it is drawn only for a status that claims delivery:
    // a note went nowhere, and an approved SignOff is a decision rather than a delivery.
    final showTick =
        message.isFromBoutique &&
        (message.status == MessageStatus.sent || message.isDelivered);

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
