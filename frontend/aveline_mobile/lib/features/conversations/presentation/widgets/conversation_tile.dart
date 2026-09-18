import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../domain/conversation.dart';
import 'conversation_avatar.dart';

/// One client thread in the message inbox.
///
/// Laid out the way a phone's message inbox is: the face, the client's name, the
/// last word, and when it landed. There is no unread badge and no read/unread
/// emphasis: the release ships no read model, so a row claims nothing about
/// whether its words have been seen.
///
/// The last word is prefixed with who said it, because "the blouse is pinned and
/// ready" reads as the client reporting progress when it was the associate. A
/// client's own message goes unprefixed, the way a message inbox does it, and an
/// agent's words are credited to the persona that wrote them rather than to the
/// umbrella brand.
///
/// A row also wears the first marker the server derived for it, in the same
/// trailing position: the server has already sorted them by priority, so the row
/// need not know what `approval`, `choice` and `draft` mean.
class ConversationTile extends StatelessWidget {
  const ConversationTile({
    super.key,
    required this.conversation,
    this.onTap,
    this.avatarSize = 54,
  });

  final Conversation conversation;

  /// `null` leaves the row inert.
  final VoidCallback? onTap;

  final double avatarSize;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Material(
      type: MaterialType.transparency,
      child: InkWell(
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 20, vertical: 12),
          child: Row(
            children: [
              ConversationAvatar.client(
                name: conversation.customerName,
                size: avatarSize,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            conversation.title,
                            key: ValueKey('conversation_name_${conversation.id}'),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.titleMedium?.copyWith(
                              fontWeight: FontWeight.w600,
                              color: scheme.onSurface.withValues(alpha: 0.8),
                            ),
                          ),
                        ),
                        const SizedBox(width: 10),
                        // A thread opened from the client book that nobody has
                        // spoken in yet has no time to show, and "Just now" for a
                        // thread that never was would be a small lie.
                        if (conversation.lastMessageAt case final at?)
                          Text(
                            relativeMoment(at),
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: scheme.onSurfaceVariant,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                      ],
                    ),
                    const SizedBox(height: 3),
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            preview,
                            key: ValueKey('conversation_preview_${conversation.id}'),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: scheme.onSurfaceVariant,
                            ),
                          ),
                        ),
                        // The server sorts a row's markers by priority, so the
                        // first one is the one the row acts on.
                        if (conversation.markers case [final marker, ...]) ...[
                          const SizedBox(width: 8),
                          _ConversationMarker(
                            conversationId: conversation.id,
                            label: marker.label,
                          ),
                        ],
                      ],
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

  /// The last word, prefixed with who said it.
  ///
  /// An agent's words wear the persona that wrote them ([lastMessageAgentKey]),
  /// falling back to the umbrella brand when the key is absent or unknown.
  String get preview {
    final text = conversation.lastMessagePreview;
    if (text == null || text.isEmpty) {
      return 'No messages yet';
    }
    return switch (conversation.lastMessageAuthor) {
      ConversationAuthor.staff => 'You: $text',
      ConversationAuthor.agent =>
        '${_personaName(conversation.lastMessageAgentKey)}: $text',
      _ => text,
    };
  }
}

/// The persona names an agent's last word can wear.
const Map<String, String> _personaNames = {
  'aveline': 'Aveline',
  'ava': 'Ava',
  'elle': 'Elle',
  'lina': 'Lina',
};

/// The name to credit an agent's words to, falling back to the brand.
String _personaName(String? agentKey) {
  if (agentKey == null) {
    return Conversation.avelineTitle;
  }
  return _personaNames[agentKey.toLowerCase()] ?? Conversation.avelineTitle;
}

/// The one marker a row wears.
///
/// The server derives the actionable states and sorts them, so the row prints
/// the first: whatever it says has already been decided upstream.
class _ConversationMarker extends StatelessWidget {
  const _ConversationMarker({
    required this.conversationId,
    required this.label,
  });

  final String conversationId;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: ValueKey('conversation_marker_$conversationId'),
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.28)),
      ),
      child: Text(
        label,
        style: theme.textTheme.labelSmall?.copyWith(
          color: scheme.primary,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
