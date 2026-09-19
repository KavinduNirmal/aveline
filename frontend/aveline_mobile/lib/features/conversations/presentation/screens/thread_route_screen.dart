import 'package:flutter/material.dart';

import '../../data/conversation_repository.dart';
import '../../data/thread_repository.dart';
import '../../domain/conversation.dart';
import 'client_thread_screen.dart';

/// One client thread, opened by id rather than handed the row.
///
/// The inbox already holds the `Conversation` it opens a thread with. A notification knows only
/// the id, so this screen reads the row first and then shows the same thread screen, anchored to
/// the message the notification named when it named one.
///
/// Three outcomes, kept apart on purpose: the row arrives (the thread), the row does not exist or
/// is not visible (its own state, not a network failure), and the organization id is not known
/// yet (a "not yet" the caller can retry).
class ThreadRouteScreen extends StatefulWidget {
  const ThreadRouteScreen({
    super.key,
    required this.conversationId,
    required this.conversationRepository,
    required this.threadRepository,
    this.messageId,
  });

  final String conversationId;
  final ConversationRepository conversationRepository;
  final ThreadRepository threadRepository;

  /// The message to open on, when the caller was sent to one.
  final String? messageId;

  @override
  State<ThreadRouteScreen> createState() => _ThreadRouteScreenState();
}

class _ThreadRouteScreenState extends State<ThreadRouteScreen> {
  Conversation? _conversation;
  bool _loading = true;
  bool _waitingForOrg = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _waitingForOrg = false;
    });

    try {
      final conversation = await widget.conversationRepository.fetchConversation(
        widget.conversationId,
      );
      if (!mounted) {
        return;
      }
      setState(() {
        _conversation = conversation;
        _loading = false;
      });
    } on OrgContextUnavailable {
      if (!mounted) {
        return;
      }
      // A "not yet": the boutique's id arrives with `/orgs/my`, so the screen waits rather than
      // claiming the thread could not be opened.
      setState(() {
        _waitingForOrg = true;
        _loading = false;
      });
    } catch (_) {
      if (!mounted) {
        return;
      }
      setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final conversation = _conversation;
    if (conversation != null) {
      return ClientThreadScreen(
        conversation: conversation,
        repository: widget.threadRepository,
        aroundMessageId: widget.messageId,
      );
    }

    return Scaffold(
      appBar: AppBar(leading: BackButton(onPressed: () => Navigator.of(context).maybePop())),
      body: Center(
        child: Padding(
          padding: const EdgeInsets.all(28),
          child: _loading
              ? const SizedBox(
                  key: Key('thread_route_loading'),
                  width: 24,
                  height: 24,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : _Message(
                  key: _waitingForOrg
                      ? const Key('thread_route_waiting')
                      : const Key('thread_route_not_found'),
                  message: _waitingForOrg
                      ? 'The boutique is still loading.'
                      : 'This conversation is no longer available.',
                  onRetry: _load,
                ),
        ),
      ),
    );
  }
}

/// The stand-in state for a thread that has not arrived.
class _Message extends StatelessWidget {
  const _Message({super.key, required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(Icons.forum_outlined, size: 28, color: theme.colorScheme.primary),
        const SizedBox(height: 14),
        Text(
          message,
          textAlign: TextAlign.center,
          style: theme.textTheme.bodyMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 12),
        TextButton.icon(
          key: const Key('thread_route_retry'),
          onPressed: onRetry,
          icon: const Icon(Icons.refresh_rounded, size: 16),
          label: const Text('Try again'),
        ),
      ],
    );
  }
}
