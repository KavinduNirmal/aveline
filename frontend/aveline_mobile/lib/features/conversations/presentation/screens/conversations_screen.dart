import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';
import 'package:provider/provider.dart';

import '../../../../core/config/app_config.dart';
import '../../../../core/network/auth_token_provider.dart';
import '../../../../core/network/conversation_realtime_service.dart';
import '../../../../core/notifications/realtime_connection_factory.dart';
import '../../../../core/providers/boutique_provider.dart';
import '../../../../core/router/route_guards.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/blossom_refresh.dart';
import '../../../../shared/widgets/brand_section_title.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../../../shared/widgets/section_search_field.dart';
import '../../../salon/presentation/screens/salon_screen.dart';
import '../../data/conversation_repository.dart';
import '../../data/empty_conversation_repository.dart';
import '../../data/empty_thread_repository.dart';
import '../../data/thread_repository.dart';
import '../../domain/conversation.dart';
import '../conversations_controller.dart';
import '../widgets/aveline_conversation_tile.dart';
import '../widgets/conversation_tile.dart';
import 'client_thread_screen.dart';

/// Messages dock tab: the boutique's inbox.
///
/// A column of threads, newest word first, with the Salon pinned above them. The
/// order is the controller's, not this screen's: the concierge is a fixed
/// destination rather than one of the results, and the client threads follow it by
/// whatever was said last.
///
/// The screen owns no inbox state of its own. It reads a [ConversationsController]
/// - injectable for tests and previews, otherwise whatever the app provides, and
/// failing that an empty repository, so no production path renders invented data.
///
/// Opening a thread is handed up: the Salon is a real screen and this one pushes
/// it, and a client row opens [ClientThreadScreen] with the thread repository it
/// was given. A row whose thread has no injected source opens the honest empty
/// state rather than a seeded exchange.
class ConversationsScreen extends StatefulWidget {
  const ConversationsScreen({
    super.key,
    this.boutiqueName,
    this.repository,
    this.threadRepository,
    this.pageSize = 50,
    this.onOpenAveline,
    this.onOpenConversation,
  });

  /// Overrides the inbox's source, for tests and previews. Defaults to an empty
  /// repository, so a screen built with nothing injected renders the honest empty
  /// state rather than a seed of invented threads (D5).
  final ConversationRepository? repository;

  /// Overrides the source of the threads the client rows open. Defaults to an
  /// empty repository, so a screen built with nothing injected (the registry's
  /// `const ConversationsScreen()`) renders the honest empty state rather than a
  /// seed of invented exchanges.
  final ThreadRepository? threadRepository;

  /// How many messages a page of an opened thread holds.
  final int pageSize;

  /// Overrides the boutique name, for tests and previews. When `null`, the name
  /// is read from [BoutiqueProvider], falling back to the brand.
  final String? boutiqueName;

  /// Overrides what the pinned Salon row opens. When `null`, the Salon screen is
  /// pushed.
  final VoidCallback? onOpenAveline;

  /// Overrides what a client row opens. When `null`, the row says that the thread
  /// screen is not built yet.
  final void Function(Conversation conversation)? onOpenConversation;

  @override
  State<ConversationsScreen> createState() => _ConversationsScreenState();
}

class _ConversationsScreenState extends State<ConversationsScreen> {
  /// What the title reads before a boutique name is known.
  static const String _fallbackName = 'Aveline';

  late final ConversationsController _controller;

  /// The inbox's own realtime connection.
  ///
  /// One connection per screen, following the Salon's pattern: the service holds a single
  /// connection and a single listener set, so sharing an instance would make the two screens
  /// fight over each other's callbacks.
  ConversationRealtimeService? _realtimeService;

  final TextEditingController _searchController = TextEditingController();
  final ScrollController _scrollController = ScrollController();

  /// The refresh revision last seen, so a pull is told apart from a build.
  int _refreshRevision = 0;
  bool _hasSeenFirstDependencies = false;

  @override
  void initState() {
    super.initState();
    _controller = ConversationsController(
      widget.repository ?? const EmptyConversationRepository(),
    );

    // Started before the listener is attached: `load` notifies synchronously,
    // and that must not reach `setState` from `initState`. Nothing is lost,
    // because the first build already reads the loading state.
    if (_controller.hasLoadedOnce) {
      _controller.refresh();
    } else {
      _controller.load();
    }

    _controller.addListener(_onControllerChanged);

    // Best-effort: with no providers above the screen (tests, previews) the inbox simply
    // stays as it was read and a pull-to-refresh is the fallback.
    _connectRealtime();
  }

  /// Opens the inbox's own hub connection so a thread that changes - or appears - while the
  /// list is open updates without a re-list.
  Future<void> _connectRealtime() async {
    try {
      final config = context.read<AppConfig>();
      final authRepository = context.read<AuthTokenProvider>();

      // No conversation id: the org and user groups are joined on connect, which is exactly
      // what the list needs. The Salon's `JoinSalon` is not.
      final service = ConversationRealtimeService(
        defaultRealtimeConnectionFactory,
      );
      _realtimeService = service;
      await service.connect(
        baseUrl: config.apiBaseUrl,
        getToken: authRepository.getToken,
        onConversationChanged: _controller.applyChanged,
      );
    } catch (error) {
      debugPrint('[conversations] realtime connect skipped/failed: $error');
    }
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();

    // A pull re-reads the inbox, which is one of the things the API owns. The
    // first call is the initial build rather than a pull, so it is skipped.
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
    _realtimeService?.disconnect();
    _scrollController.dispose();
    _searchController.dispose();
    _controller
      ..removeListener(_onControllerChanged)
      ..dispose();
    super.dispose();
  }

  void _onControllerChanged() {
    if (mounted) {
      setState(() {});
    }
  }

  /// The boutique name, or `null` when no provider is above the screen.
  String? _boutiqueNameOrNull(BuildContext context) {
    try {
      return context.watch<BoutiqueProvider>().name;
    } catch (_) {
      return null;
    }
  }

  void _onSearchChanged(String value) => _controller.search(value);

  void _clearSearch() {
    _searchController.clear();
    _controller.search('');
  }

  void _openAveline() {
    final override = widget.onOpenAveline;
    if (override != null) {
      override();
      return;
    }
    Navigator.of(context).push(
      MaterialPageRoute<void>(builder: (_) => const SalonScreen()),
    );
  }

  void _openConversation(Conversation conversation) {
    final override = widget.onOpenConversation;
    if (override != null) {
      override(conversation);
      return;
    }
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => ClientThreadScreen(
          conversation: conversation,
          repository: widget.threadRepository ?? const EmptyThreadRepository(),
          // The same inbox the list was read from, so a forward can offer the other
          // client threads.
          conversationRepository:
              widget.repository ?? const EmptyConversationRepository(),
          pageSize: widget.pageSize,
          onOpenClient: () => _openClient(conversation),
        ),
      ),
    );
  }

  /// Opens the client this thread is with, when the thread names one.
  void _openClient(Conversation conversation) {
    final customerId = conversation.customerId;
    if (customerId == null) {
      return;
    }
    // The client book resolves the profile from the id in the location, so
    // nothing has to survive the router re-parsing the route.
    GoRouter.maybeOf(context)?.push(AppRoutes.customer(customerId));
  }

  @override
  Widget build(BuildContext context) {
    final boutiqueName =
        widget.boutiqueName ?? _boutiqueNameOrNull(context) ?? _fallbackName;

    return Stack(
      children: [
        const Positioned.fill(child: BrandBackdrop()),
        CustomScrollView(
          key: const Key('conversations_scroll'),
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
    return Padding(
      padding: const EdgeInsets.fromLTRB(20, 20, 20, 0),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          BrandSectionTitle(
            boutiqueName: boutiqueName,
            section: 'Messages',
            titleKey: const Key('conversations_title'),
          ),
          const SizedBox(height: 14),
          SectionSearchField(
            controller: _searchController,
            hintText: 'Search messages...',
            hasQuery: _controller.isSearching,
            onChanged: _onSearchChanged,
            onClear: _clearSearch,
            fieldKey: const Key('conversations_search_field'),
            clearKey: const Key('conversations_search_clear'),
          ),
          const SizedBox(height: 16),
        ],
      ),
    );
  }

  /// The inbox, and whatever stands in its place while it has nothing to show.
  List<Widget> _inboxSlivers() {
    // A missing org id is a "not yet": the inbox stays in its loading state
    // rather than showing the error card, which is the server's refusal.
    if (_controller.isWaitingForOrg ||
        (_controller.isLoading && !_controller.hasLoadedOnce)) {
      return const [
        SliverToBoxAdapter(
          child: Padding(
            padding: EdgeInsets.symmetric(vertical: 48),
            child: Center(
              child: SizedBox(
                key: Key('conversations_loading'),
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
          sliver: SliverToBoxAdapter(child: _InboxEmpty(query: _controller.query)),
        ),
      ];
    }

    final aveline = _controller.aveline;

    return [
      // The Salon, pinned. It is drawn whatever the search is, which is what
      // "always at the top" means: the concierge is not one of the results.
      if (aveline != null)
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 4),
          sliver: SliverToBoxAdapter(
            child: AvelineConversationTile(
              key: const Key('conversations_aveline_row'),
              conversation: aveline,
              onTap: _openAveline,
            ),
          ),
        ),
      ..._section(
        label: 'Clients',
        overlineKey: const Key('conversations_clients_overline'),
        conversations: _controller.clients,
      ),
      ..._section(
        label: 'Notices',
        overlineKey: const Key('conversations_announcements_overline'),
        conversations: _controller.announcements,
      ),
      if (_controller.isSearching && !_controller.hasMatches)
        SliverPadding(
          padding: const EdgeInsets.fromLTRB(20, 12, 20, 0),
          sliver: SliverToBoxAdapter(
            child: _NoMatches(query: _controller.query, onClear: _clearSearch),
          ),
        ),
      if (!_controller.isSearching)
        SliverToBoxAdapter(
          child: _InboxFooter(
            loaded: _controller.items.length,
            total: _controller.total,
            hasMore: _controller.hasMore,
            isLoading: _controller.isLoadingMore,
            onLoadMore: _controller.loadMore,
          ),
        ),
    ];
  }

  /// One named group of threads.
  List<Widget> _section({
    required String label,
    required Key overlineKey,
    required List<Conversation> conversations,
  }) {
    if (conversations.isEmpty) {
      return const [];
    }

    return [
      SliverPadding(
        padding: const EdgeInsets.fromLTRB(20, 14, 20, 4),
        sliver: SliverToBoxAdapter(
          child: SectionOverline(label, key: overlineKey),
        ),
      ),
      SliverList.separated(
        itemCount: conversations.length,
        // Tight, the way a message inbox stacks its rows: the row's own padding
        // is what separates them, and a rule between every pair would make the
        // column read as a table.
        separatorBuilder: (context, index) => const SizedBox(height: 2),
        itemBuilder: (context, index) {
          final conversation = conversations[index];
          return ConversationTile(
            key: ValueKey('conversation_tile_${conversation.id}'),
            conversation: conversation,
            onTap: () => _openConversation(conversation),
          );
        },
      ),
    ];
  }
}

/// The quiet panel shown when the inbox could not be read.
class _InboxError extends StatelessWidget {
  const _InboxError({required this.message, required this.onRetry});

  final String message;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('conversations_error'),
      padding: const EdgeInsets.all(24),
      decoration: _panelDecoration(scheme),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.cloud_off_rounded, size: 28, color: scheme.primary),
          const SizedBox(height: 14),
          Text(
            'The inbox could not load',
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            message,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('conversations_retry'),
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 16),
            label: const Text('Try again'),
          ),
        ],
      ),
    );
  }
}

/// The quiet panel shown when the boutique has no conversations at all.
class _InboxEmpty extends StatelessWidget {
  const _InboxEmpty({required this.query});

  final String query;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('conversations_empty'),
      padding: const EdgeInsets.all(24),
      decoration: _panelDecoration(scheme),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.forum_outlined, size: 28, color: scheme.primary),
          const SizedBox(height: 14),
          Text('No conversations yet', style: theme.textTheme.headlineSmall),
          const SizedBox(height: 8),
          Text(
            query.isEmpty
                ? 'Client threads land here as they are opened. Aveline is one tap '
                      'away in the meantime.'
                : 'Nothing matches "$query".',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}

/// The panel shown when a search matches no client thread.
class _NoMatches extends StatelessWidget {
  const _NoMatches({required this.query, required this.onClear});

  final String query;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('conversations_no_matches'),
      padding: const EdgeInsets.all(24),
      decoration: _panelDecoration(scheme),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(Icons.search_off_rounded, size: 28, color: scheme.primary),
          const SizedBox(height: 14),
          Text(
            'No conversations match "$query"',
            style: theme.textTheme.headlineSmall,
          ),
          const SizedBox(height: 8),
          Text(
            'Try another name, or a word from the last message. Search covers the '
            'conversations loaded so far, not the whole inbox.',
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 12),
          TextButton.icon(
            key: const Key('conversations_clear_search'),
            onPressed: onClear,
            icon: const Icon(Icons.close_rounded, size: 16),
            label: const Text('Clear search'),
          ),
        ],
      ),
    );
  }
}

/// What sits under the last thread.
///
/// Truthful about how much of the inbox is on screen: `total` is the server's
/// count for the whole org, so the column only claims to be complete when every
/// thread has been loaded.
class _InboxFooter extends StatelessWidget {
  const _InboxFooter({
    required this.loaded,
    required this.total,
    required this.hasMore,
    required this.isLoading,
    required this.onLoadMore,
  });

  final int loaded;
  final int total;
  final bool hasMore;
  final bool isLoading;
  final VoidCallback onLoadMore;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);

    if (!hasMore) {
      return Padding(
        padding: const EdgeInsets.only(top: 24),
        child: Center(
          child: Text(
            'That is every conversation.',
            key: const Key('conversations_end_of_list'),
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
        ),
      );
    }

    return Padding(
      padding: const EdgeInsets.only(top: 24),
      child: Column(
        children: [
          Text(
            'Showing $loaded of $total',
            key: const Key('conversations_loaded_of_total'),
            style: theme.textTheme.labelSmall?.copyWith(
              color: theme.colorScheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 8),
          TextButton.icon(
            key: const Key('conversations_load_more'),
            onPressed: isLoading ? null : onLoadMore,
            icon: isLoading
                ? const SizedBox(
                    width: 16,
                    height: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.expand_more_rounded, size: 16),
            label: Text(isLoading ? 'Loading…' : 'Load more'),
          ),
        ],
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
