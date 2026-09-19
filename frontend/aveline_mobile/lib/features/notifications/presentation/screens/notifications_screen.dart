import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/blossom_refresh.dart';
import '../../../../shared/widgets/brand_section_title.dart';
import '../../../../shared/widgets/filter_pill.dart';
import '../../data/demo_notification_repository.dart';
import '../../domain/app_notification.dart';
import '../notifications_controller.dart';
import '../widgets/notification_tile.dart';

/// Notifications dock tab: the boutique's own inbox.
///
/// A column of the notifications the gateway has dispatched to this user, newest
/// first, with the three gestures the inbox earns: swipe right to mark read,
/// swipe left to delete, and tap to unfold the whole thing in place. Only one is
/// ever unfolded, so a long inbox stays scannable.
///
/// The screen owns no inbox state of its own. It reads the app-wide
/// [NotificationsController] so the header's badge and this list are the same
/// number, and the header's badge cannot claim unread work the list has already
/// cleared. The controller is injectable for tests and previews, and when no
/// provider is above the screen - a widget test mounting it alone - it falls back
/// to a demo inbox rather than throwing.
/// Where a notification goes when it is tapped, or `null` when it goes nowhere.
///
/// Pure, so the rule is testable without a router. A thread notification addresses the
/// **conversation** rather than the client - the thread may be one whose client is not
/// identified yet - so it opens the thread, anchored to the message the notification named. One
/// that addresses a client opens the client book, which is where it went before.
String? notificationRouteFor(AppNotification item) {
  final conversationId = item.conversationId;
  if (conversationId != null) {
    return AppRoutes.thread(conversationId, messageId: item.messageId);
  }

  final customerId = item.customerId;
  return customerId == null ? null : AppRoutes.customer(customerId);
}

class NotificationsScreen extends StatefulWidget {
  const NotificationsScreen({super.key, this.controller, this.boutiqueName});

  /// Overrides the inbox's source, for tests and previews.
  final NotificationsController? controller;

  /// Overrides the boutique name, for tests and previews. When `null`, the name
  /// is read from [BoutiqueProvider], falling back to the brand.
  final String? boutiqueName;

  @override
  State<NotificationsScreen> createState() => _NotificationsScreenState();
}

class _NotificationsScreenState extends State<NotificationsScreen> {
  /// What the title reads before a boutique name is known.
  static const String _fallbackName = 'Aveline';

  /// How close to the end of the page the next one is asked for.
  static const double _loadMoreThreshold = 400;

  late final NotificationsController _controller;

  /// Whether this screen made the controller, and so has to dispose it.
  bool _ownsController = false;

  final ScrollController _scrollController = ScrollController();

  /// The notification that is unfolded, or `null` when the inbox is closed up.
  String? _expandedId;

  /// The refresh revision last seen, so a pull is told apart from a build.
  int _refreshRevision = 0;
  bool _hasSeenFirstDependencies = false;

  @override
  void initState() {
    super.initState();
    _resolveController();

    // Started before the listener is attached: `load` notifies synchronously,
    // and that must not reach `setState` from `initState`. Nothing is lost,
    // because the first build already reads the loading state.
    if (_controller.hasLoadedOnce) {
      _controller.refresh();
    } else {
      _controller.load();
    }

    _controller.addListener(_onControllerChanged);
    _scrollController.addListener(_onScroll);
  }

  /// The controller to drive, in order of preference: the one handed in, the one
  /// the app provides, and finally one of this screen's own over the demo inbox.
  void _resolveController() {
    final injected = widget.controller;
    if (injected != null) {
      _controller = injected;
      return;
    }

    NotificationsController? provided;
    try {
      provided = context.read<NotificationsController>();
    } catch (_) {
      provided = null;
    }

    _controller =
        provided ?? NotificationsController(DemoNotificationRepository());
    _ownsController = provided == null;
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();

    // A pull re-fetches what the API owns, and the inbox is one of those things.
    // The first call is the initial build rather than a pull, so it is skipped.
    final revision = RefreshScope.revisionOf(context);
    if (!_hasSeenFirstDependencies) {
      _hasSeenFirstDependencies = true;
      _refreshRevision = revision;
      return;
    }
    if (revision != _refreshRevision) {
      _refreshRevision = revision;
      _controller.refresh();
    }
  }

  @override
  void dispose() {
    _scrollController
      ..removeListener(_onScroll)
      ..dispose();
    _controller.removeListener(_onControllerChanged);
    if (_ownsController) {
      _controller.dispose();
    }
    super.dispose();
  }

  void _onControllerChanged() {
    if (!mounted) {
      return;
    }
    setState(() {});

    // An action that failed is worth saying out loud - the tile has just moved
    // back - but it is not worth a screen state. Taken rather than read so the
    // toast is shown once.
    final error = _controller.actionError;
    if (error != null) {
      _controller.clearActionError();
      AppToast.show(context, error, error: true);
    }
  }

  void _onScroll() {
    if (!_scrollController.hasClients) {
      return;
    }
    if (_scrollController.position.extentAfter > _loadMoreThreshold) {
      return;
    }
    _controller.loadMore();
  }

  /// The boutique name, or `null` when no provider is above the screen.
  String? _boutiqueNameOrNull(BuildContext context) {
    try {
      return context.watch<BoutiqueProvider>().name;
    } catch (_) {
      return null;
    }
  }

  String get _summary {
    if (!_controller.hasLoadedOnce) {
      return 'Checking for updates';
    }
    final unread = _controller.unreadCount;
    return switch (unread) {
      0 => 'All caught up',
      1 => '1 unread',
      _ => '$unread unread',
    };
  }

  void _toggle(String id) {
    setState(() => _expandedId = _expandedId == id ? null : id);
  }

  /// Switches the narrowing and puts the inbox back at the top.
  void _applyFilter(bool unreadOnly) {
    if (_controller.unreadOnly == unreadOnly) {
      return;
    }
    setState(() => _expandedId = null);
    _controller.load(unreadOnly: unreadOnly);
    if (_scrollController.hasClients) {
      _scrollController.jumpTo(0);
    }
  }

  /// The rightward swipe, and the tile's own read action.
  Future<void> _markRead(AppNotification item) async {
    if (item.isRead) {
      // The swipe springs back either way, so a tile that had nothing to do
      // would read as a gesture that did not take.
      AppToast.show(context, 'Already marked as read.');
      return;
    }
    await _controller.markRead(item);
  }

  /// The leftward swipe, and the tile's own delete action.
  void _delete(AppNotification item) {
    // The unfolded tile is deliberately left unfolded rather than collapsed on
    // the way out: the tile is being removed in this same frame, and animating
    // an `AnimatedSize` shut while its subtree is disposed lays out a disposed
    // render object. Leaving the id in place costs nothing - no tile holds it -
    // and means an undo hands the notification back open, exactly as it was.
    _controller.dismiss(item);
    AppToast.show(
      context,
      'Notification deleted',
      actionLabel: 'Undo',
      onAction: _controller.undoDismiss,
      // As long as the deletion can still be taken back, so the offer and the
      // window it belongs to cannot drift apart.
      duration: _controller.dismissCommitDelay,
    );
  }

  /// Opens the client the notification is about.
  void _open(AppNotification item) {
    final location = notificationRouteFor(item);
    if (location != null) {
      GoRouter.maybeOf(context)?.push(location);
    }
  }

  @override
  Widget build(BuildContext context) {
    final boutiqueName =
        widget.boutiqueName ?? _boutiqueNameOrNull(context) ?? _fallbackName;

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        CustomScrollView(
          key: const Key('notifications_scroll'),
          controller: _scrollController,
          // Always scrollable, so the shell's pull-to-refresh still arms on an
          // inbox shorter than the viewport.
          physics: const AlwaysScrollableScrollPhysics(),
          slivers: [
            SliverToBoxAdapter(child: _header(boutiqueName)),
            ..._inboxSlivers(),
            // The animated Blossom floats over the bottom of the shell, so the
            // inbox keeps enough room to scroll clear of it.
            const SliverToBoxAdapter(child: SizedBox(height: 96)),
          ],
        ),
      ],
    );
  }

  Widget _header(String boutiqueName) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final unread = _controller.unreadCount;

    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          BrandSectionTitle(
            boutiqueName: boutiqueName,
            section: 'Notifications',
            titleKey: const Key('notifications_title'),
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: Text(
                  _summary,
                  key: const Key('notifications_unread_summary'),
                  style: theme.textTheme.bodyMedium?.copyWith(
                    color: scheme.onSurfaceVariant,
                  ),
                ),
              ),
              // Only offered when there is something to do, so the row does not
              // carry a permanently dead control.
              if (unread > 0)
                TextButton.icon(
                  key: const Key('notifications_mark_all_read'),
                  onPressed: () {
                    _controller.markAllRead();
                  },
                  icon: const Icon(Icons.done_all_rounded, size: 16),
                  label: const Text('Mark all read'),
                ),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            children: [
              FilterPill(
                key: const Key('notifications_filter_all'),
                label: 'All',
                selected: !_controller.unreadOnly,
                onTap: () => _applyFilter(false),
              ),
              const SizedBox(width: 8),
              FilterPill(
                key: const Key('notifications_filter_unread'),
                label: 'Unread',
                selected: _controller.unreadOnly,
                onTap: () => _applyFilter(true),
              ),
            ],
          ),
          const SizedBox(height: 10),
        ],
      ),
    );
  }

  /// The inbox, and whatever stands in its place while it has nothing to show.
  List<Widget> _inboxSlivers() {
    if (_controller.isLoading && !_controller.hasLoadedOnce) {
      return const [
        SliverToBoxAdapter(
          child: Padding(
            padding: EdgeInsets.symmetric(vertical: 48),
            child: Center(
              child: SizedBox(
                key: Key('notifications_loading'),
                width: 24,
                height: 24,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ),
          ),
        ),
      ];
    }

    if (_controller.errorMessage != null && _controller.items.isEmpty) {
      return [
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverToBoxAdapter(
            child: _InboxError(
              message: _controller.errorMessage!,
              onRetry: () => _controller.load(),
            ),
          ),
        ),
      ];
    }

    if (_controller.isEmpty) {
      return [
        SliverPadding(
          padding: const EdgeInsets.symmetric(horizontal: 20),
          sliver: SliverToBoxAdapter(
            child: _InboxEmpty(unreadOnly: _controller.unreadOnly),
          ),
        ),
      ];
    }

    final items = _controller.items;
    return [
      SliverPadding(
        padding: const EdgeInsets.symmetric(horizontal: 20),
        sliver: SliverList.separated(
          itemCount: items.length,
          // 8px between the cards, which is the gap the design system uses to
          // show that stacked cards are one collection.
          separatorBuilder: (context, index) => const SizedBox(height: 8),
          itemBuilder: (context, index) {
            final item = items[index];
            return NotificationTile(
              key: ValueKey('notification_tile_${item.id}'),
              notification: item,
              expanded: _expandedId == item.id,
              onToggle: () => _toggle(item.id),
              onMarkRead: () => _markRead(item),
              onDelete: () => _delete(item),
              onOpen: item.isOpenable ? () => _open(item) : null,
            );
          },
        ),
      ),
      if (_controller.isLoadingMore)
        const SliverToBoxAdapter(
          child: Padding(
            padding: EdgeInsets.symmetric(vertical: 20),
            child: Center(
              child: SizedBox(
                width: 20,
                height: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            ),
          ),
        ),
      if (!_controller.hasMore) const SliverToBoxAdapter(child: _InboxFooter()),
    ];
  }
}

/// The quiet panel shown when the inbox could not be fetched.
class _InboxError extends StatelessWidget {
  const _InboxError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('notifications_error'),
      padding: const EdgeInsets.all(24),
      decoration: _panelDecoration(scheme),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.cloud_off_rounded, size: 28, color: scheme.primary),
          const SizedBox(height: 14),
          Text('The inbox could not load', style: theme.textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            message,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('notifications_retry'),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: const Text('Try again'),
          ),
        ],
      ),
    );
  }
}

/// The quiet panel shown when the inbox has nothing to show.
class _InboxEmpty extends StatelessWidget {
  const _InboxEmpty({required this.unreadOnly});

  /// Whether the emptiness is the narrowing's doing or the inbox's own.
  final bool unreadOnly;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('notifications_empty'),
      padding: const EdgeInsets.all(24),
      decoration: _panelDecoration(scheme),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            unreadOnly
                ? Icons.mark_email_read_outlined
                : Icons.notifications_none_rounded,
            size: 28,
            color: scheme.primary,
          ),
          const SizedBox(height: 14),
          Text(
            unreadOnly ? 'Nothing unread' : 'No notifications yet',
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            unreadOnly
                ? 'Every notification in the inbox has been read. Switch back to '
                      'All to see them again.'
                : 'Client messages, approvals, payments and agent finds land here '
                      'as they happen.',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

/// What sits under the last notification.
class _InboxFooter extends StatelessWidget {
  const _InboxFooter();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(top: 24),
      child: Center(
        child: Text(
          'That is everything for now.',
          key: const Key('notifications_end_of_list'),
          style: theme.textTheme.labelSmall?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
          ),
        ),
      ),
    );
  }
}

/// The card the inbox's stand-in panels wear: the brand's soft, warm shadow.
BoxDecoration _panelDecoration(ColorScheme scheme) => BoxDecoration(
  color: scheme.surfaceContainerLowest,
  borderRadius: BorderRadius.circular(16),
  boxShadow: [
    BoxShadow(
      color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
      blurRadius: 20,
      offset: const Offset(0, 4),
    ),
  ],
);
