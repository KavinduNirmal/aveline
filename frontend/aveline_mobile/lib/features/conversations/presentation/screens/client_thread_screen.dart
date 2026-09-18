import 'package:flutter/material.dart';

import '../../../../shared/utils/date_formatter.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../data/demo_thread_repository.dart';
import '../../data/thread_repository.dart';
import '../../domain/conversation.dart';
import '../../domain/thread_message.dart';
import '../client_thread_controller.dart';
import '../widgets/conversation_avatar.dart';
import '../widgets/thread_composer.dart';
import '../widgets/thread_message_bubble.dart';

/// The thread with one client.
///
/// It reads from the oldest word downwards and opens at the newest, which is why
/// the list is reversed: a thread that opened at the top would open at the wrong
/// end of the story, and every send would have to scroll it by hand.
///
/// The header names the client and opens their profile. What is deliberately not
/// here: the client's grade, their history and anything else the client book
/// already says better. This screen is the conversation.
class ClientThreadScreen extends StatefulWidget {
  const ClientThreadScreen({
    super.key,
    required this.conversation,
    this.repository,
    this.pageSize = 50,
    this.onOpenClient,
  });

  /// The thread being read.
  final Conversation conversation;

  /// Overrides the thread's source, for tests and previews. Defaults to the demo
  /// repository, which holds the exchanges the inbox's previews promise.
  final ThreadRepository? repository;

  /// How many messages a page holds. Injectable so a test can reach the history
  /// above the window without seeding a hundred messages.
  final int pageSize;

  /// Overrides what the header opens. When `null`, the header is inert.
  final VoidCallback? onOpenClient;

  @override
  State<ClientThreadScreen> createState() => _ClientThreadScreenState();
}

class _ClientThreadScreenState extends State<ClientThreadScreen> {
  late final ClientThreadController _controller;

  @override
  void initState() {
    super.initState();
    _controller = ClientThreadController(
      widget.conversation,
      widget.repository ?? DemoThreadRepository(),
      pageSize: widget.pageSize,
    );

    // Started before the listener is attached: `load` notifies synchronously, and
    // that must not reach `setState` from `initState`. Nothing is lost, because
    // the first build already reads the loading state.
    _controller.load();
    _controller.addListener(_onControllerChanged);
  }

  @override
  void dispose() {
    _controller
      ..removeListener(_onControllerChanged)
      ..dispose();
    super.dispose();
  }

  void _onControllerChanged() {
    if (!mounted) {
      return;
    }
    setState(() {});

    // A refused send or decision is worth saying out loud: the message is still
    // there, marked, and the toast says what happened to it.
    final error = _controller.actionError;
    if (error != null) {
      _controller.clearActionError();
      AppToast.show(context, error, error: true);
    }
  }

  /// The thread as a list of rows: each day opens with the day it was.
  List<_ThreadRow> get _rows {
    final rows = <_ThreadRow>[];
    DateTime? currentDay;

    for (final message in _controller.messages) {
      final local = message.createdAt.toLocal();
      final day = DateTime(local.year, local.month, local.day);
      if (currentDay == null || day != currentDay) {
        currentDay = day;
        rows.add(_ThreadRow(day: message.createdAt));
      }
      rows.add(_ThreadRow(message: message));
    }
    return rows;
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Scaffold(
      backgroundColor: scheme.surface,
      appBar: AppBar(
        // Explicit rather than implied: this screen is pushed, and the test that
        // mounts it on its own would otherwise have no route to pop and no button.
        leading: BackButton(onPressed: () => Navigator.of(context).maybePop()),
        titleSpacing: 0,
        title: _ClientTitle(
          conversation: widget.conversation,
          onTap: widget.onOpenClient,
        ),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(1),
          child: Divider(height: 1, color: scheme.outlineVariant),
        ),
      ),
      body: Stack(
        children: [
          const Positioned.fill(child: BrandBackdrop()),
          Column(
            children: [
              Expanded(child: _thread()),
              ThreadComposer(
                enabled: _controller.hasLoadedOnce,
                placeholder: 'Message ${widget.conversation.title}...',
                onSend: _controller.send,
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _thread() {
    if (_controller.isLoading && !_controller.hasLoadedOnce) {
      return const Center(
        child: SizedBox(
          key: Key('thread_loading'),
          width: 24,
          height: 24,
          child: CircularProgressIndicator(strokeWidth: 2),
        ),
      );
    }

    if (_controller.errorMessage != null && _controller.messages.isEmpty) {
      return Padding(
        padding: const EdgeInsets.all(20),
        child: _ThreadError(
          message: _controller.errorMessage!,
          onRetry: _controller.load,
        ),
      );
    }

    if (_controller.isEmpty) {
      return const _EmptyThread();
    }

    final rows = _rows;
    final showEarlier = _controller.hasEarlier || _controller.isLoadingEarlier;

    return ListView.builder(
      key: const Key('thread_scroll'),
      // Reversed, so the newest word sits at the foot of the screen and stays
      // there: a message sent is pinned to the bottom without scrolling by hand.
      reverse: true,
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
      itemCount: rows.length + (showEarlier ? 1 : 0),
      itemBuilder: (context, index) {
        // The history control sits above everything, which is the end of a
        // reversed list.
        if (index == rows.length) {
          return _LoadEarlier(
            loading: _controller.isLoadingEarlier,
            onLoad: _controller.loadEarlier,
          );
        }

        final row = rows[rows.length - 1 - index];
        if (row.day case final day?) {
          return _DaySeparator(day: day);
        }

        final message = row.message!;
        return ThreadMessageBubble(
          message: message,
          onRetry: message.isFailed ? () => _controller.retry(message) : null,
          onApproveDraft: message.needsSignOff
              ? () => _controller.decideDraft(message, approved: true)
              : null,
          onDismissDraft: message.needsSignOff
              ? () => _controller.decideDraft(message, approved: false)
              : null,
        );
      },
    );
  }
}

/// One row of the thread: either a day's opening label or a message.
class _ThreadRow {
  const _ThreadRow({this.day, this.message}) : assert(day != null || message != null);

  final DateTime? day;
  final ThreadMessage? message;
}

/// The client, and the way into their profile.
class _ClientTitle extends StatelessWidget {
  const _ClientTitle({required this.conversation, this.onTap});

  final Conversation conversation;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Align(
      alignment: Alignment.centerLeft,
      child: InkWell(
        // Inert without somewhere to go, rather than opening an empty profile.
        onTap: onTap,
        borderRadius: BorderRadius.circular(20),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 4),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              ConversationAvatar.client(
                name: conversation.customerName,
                size: 34,
              ),
              const SizedBox(width: 10),
              Flexible(
                child: Text(
                  conversation.title,
                  key: const Key('thread_client_name'),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.titleMedium?.copyWith(
                    color: scheme.onSurface,
                  ),
                ),
              ),
              if (onTap != null) ...[
                const SizedBox(width: 2),
                Icon(
                  Icons.chevron_right_rounded,
                  size: 20,
                  color: scheme.onSurfaceVariant,
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// The day a run of messages belongs to.
class _DaySeparator extends StatelessWidget {
  const _DaySeparator({required this.day});

  final DateTime day;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Center(
        child: Container(
          key: const Key('thread_day_separator'),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 3),
          decoration: BoxDecoration(
            color: scheme.surfaceContainer,
            borderRadius: BorderRadius.circular(10),
          ),
          child: Text(
            relativeDay(day),
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
              fontWeight: FontWeight.w500,
            ),
          ),
        ),
      ),
    );
  }
}

/// The control that pulls in the history above the window.
class _LoadEarlier extends StatelessWidget {
  const _LoadEarlier({required this.loading, required this.onLoad});

  final bool loading;
  final VoidCallback onLoad;

  @override
  Widget build(BuildContext context) {
    if (loading) {
      return const Padding(
        padding: EdgeInsets.symmetric(vertical: 12),
        child: Center(
          child: SizedBox(
            key: Key('thread_load_earlier'),
            width: 18,
            height: 18,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ),
      );
    }

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Center(
        child: TextButton(
          key: const Key('thread_load_earlier'),
          onPressed: onLoad,
          child: const Text('Load earlier messages'),
        ),
      ),
    );
  }
}

/// The panel shown when the two of them have never spoken.
class _EmptyThread extends StatelessWidget {
  const _EmptyThread();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Column(
          key: const Key('thread_empty'),
          mainAxisSize: MainAxisSize.min,
          children: [
            Icon(Icons.forum_outlined, size: 30, color: scheme.primary),
            const SizedBox(height: 14),
            Text('No messages yet', style: theme.textTheme.headlineSmall),
            const SizedBox(height: 8),
            Text(
              'Start the conversation below. Anything you send stays in the '
              'shop\u2019s record of what this client was told.',
              textAlign: TextAlign.center,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The panel shown when the thread could not be read.
class _ThreadError extends StatelessWidget {
  const _ThreadError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('thread_error'),
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(16),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.cloud_off_rounded, size: 28, color: scheme.primary),
          const SizedBox(height: 14),
          Text('The thread could not load', style: theme.textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            message,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('thread_retry_load'),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: const Text('Try again'),
          ),
        ],
      ),
    );
  }
}
