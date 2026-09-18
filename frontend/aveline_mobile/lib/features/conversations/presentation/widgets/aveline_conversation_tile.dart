import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/count_badge.dart';
import '../../domain/conversation.dart';
import 'conversation_avatar.dart';

/// The Salon, pinned at the head of the inbox.
///
/// Drawn as a card rather than as one more row, because it is not one more row:
/// every other thread in the inbox is with a person, and the concierge is the one
/// channel that is always there and always answers. The blossom, the tinted fill
/// and the overline are what say so before the name is read.
///
/// The overline carries the section the Salon belongs to, so a pinned row does
/// not read as a client whose name happens to be Aveline.
class AvelineConversationTile extends StatelessWidget {
  const AvelineConversationTile({
    super.key,
    required this.conversation,
    this.onTap,
  });

  final Conversation conversation;

  /// `null` leaves the card inert.
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final unread = conversation.isUnread;

    return Container(
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.055),
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.18)),
      ),
      clipBehavior: Clip.antiAlias,
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.all(14),
            child: Row(
              children: [
                const ConversationAvatar.aveline(size: 50),
                const SizedBox(width: 14),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    mainAxisSize: MainAxisSize.min,
                    children: [
                      Text(
                        'YOUR CONCIERGE',
                        style: theme.textTheme.labelSmall?.copyWith(
                          color: scheme.primary,
                          letterSpacing: 1.2,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Row(
                        children: [
                          Expanded(
                            child: Text(
                              conversation.title,
                              key: const Key('conversations_aveline_name'),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: theme.textTheme.titleMedium?.copyWith(
                                fontWeight: FontWeight.w700,
                                color: scheme.onSurface,
                              ),
                            ),
                          ),
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
                              _preview,
                              key: const Key('conversations_aveline_preview'),
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: scheme.onSurfaceVariant,
                              ),
                            ),
                          ),
                          if (unread) ...[
                            const SizedBox(width: 8),
                            CountBadge(
                              key: const Key('conversations_aveline_unread'),
                              count: conversation.unreadCount,
                              semanticLabel:
                                  conversation.unreadCount == 1
                                  ? '1 unread message'
                                  : '${conversation.unreadCount} unread messages',
                            ),
                          ],
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 6),
                Icon(
                  Icons.chevron_right_rounded,
                  size: 22,
                  color: scheme.primary.withValues(alpha: 0.7),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  /// The Salon's last word.
  ///
  /// Only the associate's own words are prefixed: an agent's reply sits under a
  /// row that already says Aveline, and prefixing it would print her name twice.
  String get _preview {
    final text = conversation.lastMessagePreview;
    if (text == null || text.isEmpty) {
      return 'Ask Aveline anything about the boutique.';
    }
    return conversation.lastMessageAuthor == ConversationAuthor.staff
        ? 'You: $text'
        : text;
  }
}
