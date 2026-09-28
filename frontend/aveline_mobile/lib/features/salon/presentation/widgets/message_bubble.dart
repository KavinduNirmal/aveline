import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../../../../shared/persona.dart';
import '../../../../shared/widgets/blossom.dart';
import '../../../conversations/domain/thread_message.dart';
import '../../../conversations/presentation/widgets/block_action_rail.dart';
import '../../../conversations/presentation/widgets/client_channel_surface.dart';
import '../../domain/salon_message.dart';
import '../../../conversations/domain/tile_blocks.dart';
import '../../../conversations/presentation/widgets/bubble_tone.dart';
import '../../../conversations/presentation/widgets/message_blocks.dart';
import 'typewriter_text.dart';

/// A single message bubble in the Salon. Agent messages are attributed to their
/// persona (Aveline/Ava/Elle/Lina) with the persona accent; staff messages align
/// right in the primary colour. Mirrors the web `MessageBubble`.
class MessageBubble extends StatelessWidget {
  const MessageBubble({
    super.key,
    required this.message,
    this.onStreamProgress,
    this.onSelectCustomer,
    this.onSignOff,
    this.loadAttachment,
    this.openAttachment,
    this.bridge,
  });

  final SalonMessage message;

  /// Called as a streamed message types out, so the thread can keep the tail in view.
  final VoidCallback? onStreamProgress;

  /// Called when the staff picks a customer from a resolution `choice` block (Issue #161).
  final ValueChanged<String>? onSelectCustomer;

  /// Called with the decision when the associate releases or drops a staged reply.
  ///
  /// The whole message travels rather than just the boolean: the API binds a decision
  /// to the content hash the approver was shown, and only the message carries it.
  final void Function(SalonMessage message, bool approved)? onSignOff;

  /// Fetches an attachment's bytes through the authenticated client.
  final Future<Uint8List> Function(String attachmentId)? loadAttachment;

  /// Opens a document through the platform viewer.
  final Future<void> Function(Uint8List bytes, String fileName)? openAttachment;

  /// The action rail's wiring for the Salon.
  ///
  /// The Salon draws the same AI content blocks as a client thread, so it draws the same
  /// rail. `null` leaves the cards exactly as they were, which is what a preview or a
  /// test with no surface behind it wants.
  final BlockActionBridge? bridge;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final persona = personaForAuthor(message.authorKind, message.agentKey);
    final isOwn = message.isOwn;
    final isAgent = message.authorKind == 'Agent';
    final tone = isOwn ? BubbleTone.own : BubbleTone.other;
    final blocks = message.contentBlocks;
    // A row of tiles has to count its columns against a definite width, and the bubble
    // is otherwise shrink-to-fit: without this the row resolves to a single column and
    // the pieces stack. A message with a photograph in it already reached the same
    // width through the image's own intrinsic size.
    final tileRow = hasTileRow(blocks);
    // A customer's relayed message is not prose in a bubble: it is the channel, so it
    // becomes the message surface rather than a card inside one.
    final clientBlock = _clientBlock(blocks);

    return LayoutBuilder(
      builder: (context, constraints) {
        // Web caps the bubble at `max-w-[78%]` and, for a message carrying a tile
        // row, also sets `w-full` — which resolves to that same 78%, but as a
        // definite width the row can divide into columns.
        final maxBubble = constraints.maxWidth * 0.78;

        final radius = BorderRadius.only(
          topLeft: const Radius.circular(16),
          topRight: const Radius.circular(16),
          bottomLeft: Radius.circular(isOwn ? 16 : 5),
          bottomRight: Radius.circular(isOwn ? 5 : 16),
        );

        return Row(
          mainAxisAlignment:
              isOwn ? MainAxisAlignment.end : MainAxisAlignment.start,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (!isOwn && persona != null) ...[
              _AgentAvatar(persona: persona),
              const SizedBox(width: 8),
            ],
            Flexible(
              child: ConstrainedBox(
                constraints: BoxConstraints(maxWidth: maxBubble),
                child: SizedBox(
                  key: ValueKey('salon_bubble_${message.id}'),
                  width: tileRow ? maxBubble : null,
                  child: Column(
                    crossAxisAlignment: isOwn
                        ? CrossAxisAlignment.end
                        : CrossAxisAlignment.start,
                    children: [
                      if (persona != null)
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 4),
                          child: Text(
                            persona.name,
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: persona.accent,
                              fontSize: 11,
                            ),
                          ),
                        ),
                      const SizedBox(height: 2),
                      if (clientBlock != null)
                        _ClientSurface(
                          messageId: message.id,
                          block: clientBlock,
                          rest: [
                            for (final block in blocks)
                              if (block.type != 'client_message') block,
                          ],
                          radius: radius,
                          persona: persona,
                          onSelectCustomer: onSelectCustomer,
                          loadAttachment: loadAttachment,
                          openAttachment: openAttachment,
                          bridge: bridge,
                        )
                      else
                        Container(
                          padding: const EdgeInsets.symmetric(
                            horizontal: 12,
                            vertical: 9,
                          ),
                          decoration: BoxDecoration(
                            color: isOwn
                                ? scheme.primary
                                : isAgent
                                    ? scheme.surfaceContainerLowest
                                    : scheme.surfaceContainerHigh,
                            borderRadius: radius,
                            border: isOwn
                                ? null
                                : Border.all(color: scheme.outlineVariant),
                          ),
                          child: _content(context, blocks, tone, persona),
                        ),
                      const SizedBox(height: 2),
                      _Footer(message: message, isOwn: isOwn),
                    ],
                  ),
                ),
              ),
            ),
          ],
        );
      },
    );
  }

  /// The customer's relayed block, when the message carries one.
  static ThreadBlock? _clientBlock(List<ThreadBlock> blocks) {
    for (final block in blocks) {
      if (block.type == 'client_message') {
        return block;
      }
    }
    return null;
  }

  /// The bubble's body.
  ///
  /// A live agent message whose content is purely text types out word by word; every
  /// other message draws its blocks, because streaming collapses the content to its
  /// first `text` block and would hide the cards of a rich answer until a reload.
  Widget _content(
    BuildContext context,
    List<ThreadBlock> blocks,
    BubbleTone tone,
    Persona? persona,
  ) {
    final scheme = Theme.of(context).colorScheme;
    final ink = tone == BubbleTone.own ? scheme.onPrimary : scheme.onSurface;

    if (message.streamsAsText) {
      return TypewriterText(
        text: message.text,
        style: _salonBody(context, ink),
        onProgress: onStreamProgress,
      );
    }

    if (blocks.isEmpty) {
      return Text(message.text, style: _salonBody(context, ink));
    }

    return MessageBlockList(
      messageId: message.id,
      blocks: blocks,
      persona: persona,
      tone: tone,
      onSelectCustomer: onSelectCustomer,
      // A decision is offered only for a reply that is actually staged: an approved or
      // dismissed one has been answered, and the API would refuse a second decision.
      onSignOff: message.needsSignOff && onSignOff != null
          ? (approved) => onSignOff!(message, approved)
          : null,
      loadAttachment: loadAttachment,
      openAttachment: openAttachment,
      bridge: bridge,
    );
  }
}

/// The Salon's prose size: the web's `text-sm`, which a chat surface wants.
TextStyle? _salonBody(BuildContext context, Color ink) =>
    Theme.of(context).textTheme.bodyMedium?.copyWith(
          color: ink,
          fontSize: 14,
          height: 1.55,
        );

/// The customer's relayed message, as the message surface.
///
/// The surface is what the message is drawn on, so the bubble's own frame is not
/// wrapped around it: a card inside a bubble is two frames for one message and puts
/// the customer's words a level deeper than everyone else's. Anything else the
/// message carries is drawn under the surface rather than inside it.
class _ClientSurface extends StatelessWidget {
  const _ClientSurface({
    required this.messageId,
    required this.block,
    required this.rest,
    required this.radius,
    required this.persona,
    required this.onSelectCustomer,
    required this.loadAttachment,
    required this.openAttachment,
    this.bridge,
  });

  final String messageId;
  final ThreadBlock block;
  final List<ThreadBlock> rest;
  final BorderRadius radius;
  final Persona? persona;
  final ValueChanged<String>? onSelectCustomer;
  final Future<Uint8List> Function(String attachmentId)? loadAttachment;
  final Future<void> Function(Uint8List bytes, String fileName)? openAttachment;
  final BlockActionBridge? bridge;

  @override
  Widget build(BuildContext context) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      mainAxisSize: MainAxisSize.min,
      children: [
        ClientChannelSurface(
          text: block.text ?? '',
          handle: block.from,
          borderRadius: radius,
        ),
        if (rest.isNotEmpty) ...[
          const SizedBox(height: 8),
          MessageBlockList(
            messageId: messageId,
            blocks: rest,
            persona: persona,
            onSelectCustomer: onSelectCustomer,
            loadAttachment: loadAttachment,
            openAttachment: openAttachment,
            bridge: bridge,
          ),
        ],
      ],
    );
  }
}

/// The footer under a bubble: a Sending…/Failed status for optimistic staff messages, the
/// sent timestamp once confirmed, and a "Thought for Xs" caption on agent messages.
class _Footer extends StatelessWidget {
  const _Footer({required this.message, required this.isOwn});

  final SalonMessage message;
  final bool isOwn;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final baseStyle = theme.textTheme.labelSmall?.copyWith(
      color: scheme.onSurfaceVariant,
      fontSize: 10,
    );

    final children = <Widget>[];

    if (message.isSending) {
      children.add(Text('Sending…', style: baseStyle));
    } else if (message.isFailed) {
      children.add(Text(
        'Failed to send',
        style: baseStyle?.copyWith(color: scheme.error),
      ));
    } else {
      children.add(Text(_timeLabel(message.createdAt), style: baseStyle));
    }

    if (!message.isOwn &&
        message.thoughtSeconds != null &&
        !message.isSending &&
        !message.isFailed) {
      children.add(Text(
        'Thought for ${message.thoughtSeconds!.toStringAsFixed(2)}s',
        style: baseStyle?.copyWith(fontStyle: FontStyle.italic),
      ));
    }

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 4),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (var i = 0; i < children.length; i++) ...[
            if (i > 0) const SizedBox(width: 6),
            children[i],
          ],
        ],
      ),
    );
  }

  String _timeLabel(DateTime date) {
    final local = date.toLocal();
    final hour = local.hour.toString().padLeft(2, '0');
    final minute = local.minute.toString().padLeft(2, '0');
    return '$hour:$minute';
  }
}

/// The avatar shown for an agent message. Aveline uses the blossom; the other
/// personas use an initial on their accent.
class _AgentAvatar extends StatelessWidget {
  const _AgentAvatar({required this.persona});

  final Persona persona;

  @override
  Widget build(BuildContext context) {
    if (persona.name == 'Aveline') {
      return Container(
        width: 32,
        height: 32,
        alignment: Alignment.center,
        // The mark takes the persona's own ink: `Blossom` falls back to the ambient
        // icon colour and then to black, and a black blob is not the brand.
        child: Blossom(size: 22, color: persona.accent),
      );
    }
    return Container(
      width: 32,
      height: 32,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: persona.accent,
      ),
      child: Text(
        persona.name[0],
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: Colors.white,
              fontWeight: FontWeight.w600,
            ),
      ),
    );
  }
}
