import 'package:flutter/material.dart';

import '../../../../shared/widgets/blossom.dart';
import '../../domain/salon_message.dart';
import 'persona.dart';
import 'typewriter_text.dart';

/// A single message bubble in the Salon. Agent messages are attributed to their
/// persona (Aveline/Ava/Elle/Lina) with the persona accent; staff messages align
/// right in the primary colour. Mirrors the web `MessageBubble`.
class MessageBubble extends StatelessWidget {
  const MessageBubble({
    super.key,
    required this.message,
    this.onStreamProgress,
  });

  final SalonMessage message;

  /// Called as a streamed message types out, so the thread can keep the tail in view.
  final VoidCallback? onStreamProgress;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final persona = personaForAuthor(message.authorKind, message.agentKey);
    final isOwn = message.isOwn;

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
              Container(
                constraints: BoxConstraints(
                  maxWidth: MediaQuery.of(context).size.width * 0.78,
                ),
                padding: const EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 10,
                ),
                decoration: BoxDecoration(
                  color: isOwn
                      ? scheme.primary
                      : scheme.surfaceContainerLowest,
                  borderRadius: BorderRadius.only(
                    topLeft: const Radius.circular(16),
                    topRight: const Radius.circular(16),
                    bottomLeft: Radius.circular(isOwn ? 16 : 6),
                    bottomRight: Radius.circular(isOwn ? 6 : 16),
                  ),
                  border: isOwn
                      ? null
                      : Border.all(color: scheme.outlineVariant),
                ),
                child: message.streamIn
                    ? TypewriterText(
                        text: message.text,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: isOwn ? scheme.onPrimary : scheme.onSurface,
                        ),
                        onProgress: onStreamProgress,
                      )
                    : Text(
                        message.text,
                        style: theme.textTheme.bodyMedium?.copyWith(
                          color: isOwn ? scheme.onPrimary : scheme.onSurface,
                        ),
                      ),
              ),
              const SizedBox(height: 2),
              _Footer(message: message, isOwn: isOwn),
            ],
          ),
        ),
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
        child: const Blossom(size: 22),
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
