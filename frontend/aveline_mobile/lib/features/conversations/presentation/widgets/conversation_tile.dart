import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/count_badge.dart';
import '../../domain/conversation.dart';
import 'conversation_avatar.dart';

/// One client thread in the message inbox.
///
/// Laid out the way a phone's message inbox is: the face, the client's name, the
/// last word, and when it landed. Read rows recede - a lighter preview - and an
/// unread row wears a count at the foot of the trailing edge, so the column can be
/// scanned for what still needs an answer without reading a word of it.
///
/// The last word is prefixed with who said it, because "the blouse is pinned and
/// ready" reads as the client reporting progress when it was the associate. A
/// client's own message goes unprefixed, the way a message inbox does it.
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
    final unread = conversation.isUnread;

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
                              fontWeight: unread
                                  ? FontWeight.w700
                                  : FontWeight.w600,
                              color: unread
                                  ? scheme.onSurface
                                  : scheme.onSurface.withValues(alpha: 0.8),
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
                              color: unread
                                  ? scheme.onSurface
                                  : scheme.onSurfaceVariant,
                              fontWeight: unread
                                  ? FontWeight.w600
                                  : FontWeight.w400,
                            ),
                          ),
                        ),
                        if (conversation.needsSignOff) ...[
                          const SizedBox(width: 8),
                          _SignOffMarker(conversationId: conversation.id),
                        ],
                        if (unread) ...[
                          const SizedBox(width: 8),
                          CountBadge(
                            key: ValueKey(
                              'conversation_unread_${conversation.id}',
                            ),
                            count: conversation.unreadCount,
                            semanticLabel: conversation.unreadCount == 1
                                ? '1 unread message'
                                : '${conversation.unreadCount} unread messages',
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
  String get preview {
    final text = conversation.lastMessagePreview;
    if (text == null || text.isEmpty) {
      return 'No messages yet';
    }
    return switch (conversation.lastMessageAuthor) {
      ConversationAuthor.staff => 'You: $text',
      ConversationAuthor.agent => '${Conversation.avelineTitle}: $text',
      _ => text,
    };
  }
}

/// The mark on a thread that is waiting on the associate for a decision.
///
/// A conversation paused for a human-in-the-loop approval is the one thread that
/// is waiting on the associate rather than the other way round, so it earns a
/// mark the inbox cannot give a merely unread one.
class _SignOffMarker extends StatelessWidget {
  const _SignOffMarker({required this.conversationId});

  final String conversationId;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: ValueKey('conversation_signoff_$conversationId'),
      padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 2),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(6),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.28)),
      ),
      child: Text(
        'Approval',
        style: theme.textTheme.labelSmall?.copyWith(
          color: scheme.primary,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
