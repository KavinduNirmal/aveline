import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../core/config/app_config.dart';
import '../../../../core/network/auth_token_provider.dart';
import '../../../../core/network/conversation_realtime_service.dart';
import '../../../../core/notifications/realtime_connection_factory.dart';
import '../../../../core/providers/agent_state_provider.dart';
import '../../../../core/providers/user_provider.dart';
import '../../data/conversation_api.dart';
import '../../domain/agent_activity.dart';
import '../../domain/agent_state.dart';
import '../../domain/salon_message.dart';
import '../widgets/agent_activity_bubble.dart';
import '../widgets/agent_avatar.dart';
import '../widgets/message_bubble.dart';
import '../widgets/salon_composer.dart';

/// The full-screen Salon conversation. Opened from the dock's center launcher;
/// the dock is hidden here. Mirrors the web SalonPanel/MessageThread in a
/// mobile-first, single-thread layout.
///
/// Staff messages are sent optimistically (shown immediately with a Sending… status), and
/// Aveline's live reasoning is reflected in an activity bubble until her reply lands.
class SalonScreen extends StatefulWidget {
  const SalonScreen({super.key});

  @override
  State<SalonScreen> createState() => _SalonScreenState();
}

class _SalonScreenState extends State<SalonScreen> with WidgetsBindingObserver {
  final List<SalonMessage> _messages = _seedMessages();
  final ScrollController _scrollController = ScrollController();
  final AgentStateProvider _agentStateProvider = AgentStateProvider();
  ConversationRealtimeService? _realtimeService;
  ConversationApi? _conversationApi;
  String? _organizationId;
  String? _conversationId;
  AgentActivity? _agentActivity;
  bool _sending = false;

  /// True while the thread is scrolled to (or within [_atBottomThreshold] of) the newest
  /// message. Drives both auto-following replies and the jump-to-latest button.
  bool _atBottom = true;

  /// Set while we scroll the thread ourselves, so the scroll listener does not mistake our
  /// own animation frames for the reader scrolling away.
  bool _autoScrolling = false;

  /// How close to the bottom still counts as being at the newest message.
  static const double _atBottomThreshold = 48;

  static List<SalonMessage> _seedMessages() {
    final now = DateTime.now();
    return [
      SalonMessage(
        id: 'm1',
        authorKind: 'Agent',
        agentKey: 'aveline',
        text:
            'Good day. I am Aveline, your boutique concierge. Ask me about a customer, a piece, or a price.',
        createdAt: now.subtract(const Duration(minutes: 12)),
      ),
      SalonMessage(
        id: 'm2',
        authorKind: 'User',
        text: 'Can you remind me what Mrs. Perera preferred last season?',
        createdAt: now.subtract(const Duration(minutes: 10)),
      ),
      SalonMessage(
        id: 'm3',
        authorKind: 'Agent',
        agentKey: 'ava',
        text:
            'Mrs. Perera favoured silk kurtas in muted rose and gold, and asked to be notified of any new linen arrivals.',
        createdAt: now.subtract(const Duration(minutes: 9)),
      ),
      SalonMessage(
        id: 'm4',
        authorKind: 'Agent',
        agentKey: 'aveline',
        text:
            'I have noted that. Would you like me to draft a short note to her for the new linen collection?',
        createdAt: now.subtract(const Duration(minutes: 8)),
      ),
    ];
  }

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _scrollController.addListener(_onScroll);
    _connectRealtime();
    // Open on the newest message rather than the top of the thread.
    _scrollToBottom(animate: false);
  }

  /// Keeps the newest message visible when the viewport changes size, most importantly when
  /// the on-screen keyboard opens and shortens the thread.
  @override
  void didChangeMetrics() {
    if (_atBottom) _scrollToBottom(animate: false);
  }

  /// Resolves the Aveline Salon and opens a realtime connection to receive agent states and
  /// messages. Best-effort: when the user/org or providers are unavailable (e.g. in tests or
  /// before onboarding), the Salon simply stays static.
  Future<void> _connectRealtime() async {
    try {
      final user = context.read<UserProvider>().user;
      final organizationId = user?.organizationId;
      if (organizationId == null || organizationId.isEmpty) return;

      final dio = context.read<Dio>();
      final config = context.read<AppConfig>();
      final authRepository = context.read<AuthTokenProvider>();

      final api = ConversationApi(dio);
      final conversation = await api.getOrCreateAvelineSalon(organizationId);
      if (!mounted) return;

      _conversationApi = api;
      _organizationId = organizationId;
      _conversationId = conversation.id;

      // Load the persisted history so the thread survives a refresh/restart instead of
      // resetting to placeholder messages. Best-effort: fall back to the demo seed offline.
      try {
        final history = await api.fetchMessages(
          organizationId: organizationId,
          conversationId: conversation.id,
        );
        if (!mounted) return;
        setState(() {
          _messages
            ..clear()
            ..addAll(history);
        });
        // History is served oldest-first, so without this the thread stays at the top of the
        // conversation and appears to open on the oldest message.
        _scrollToBottom(animate: false);
      } catch (_) {
        if (mounted) setState(() => _messages.addAll(_seedMessages()));
      }

      final service = ConversationRealtimeService(defaultRealtimeConnectionFactory);
      _realtimeService = service;
      await service.connect(
        baseUrl: config.apiBaseUrl,
        getToken: authRepository.getToken,
        organizationId: organizationId,
        conversationId: conversation.id,
        onAgentState: (payload) {
          if (!mounted) return;

          final state = AgentState.fromWire(payload.state);
          final activity = _agentActivity;

          // States are published next to the messages they describe but on separate channels,
          // so one can arrive after the reply it belonged to. Tracking only a run that is
          // actually in flight keeps a late state from reopening the activity bubble, which
          // nothing would ever close again.
          if (activity == null && !state.isTerminal) return;

          _agentStateProvider.apply(state);
          setState(() => _agentActivity = nextAgentActivity(activity, state));
        },
        onMessage: (message) {
          _handleIncomingMessage(message);
        },
      );
    } catch (e) {
      debugPrint('[salon] realtime connect skipped/failed: $e');
    }
  }

  /// Handles an incoming (agent) message: collapses the live activity into a "Thought for
  /// Xs" caption and appends the message.
  void _handleIncomingMessage(SalonMessage message) {
    if (!mounted) return;
    final wasAtBottom = _atBottom;
    var isAgent = false;
    setState(() {
      if (_messages.any((m) => m.id == message.id)) return;

      final activity = _agentActivity;
      isAgent = message.authorKind == 'Agent';
      final thoughtSeconds = isAgent && activity != null
          ? DateTime.now().difference(activity.startedAt).inMilliseconds / 1000.0
          : null;

      _messages.add(
        message.copyWith(
          thoughtSeconds: thoughtSeconds,
          streamIn: isAgent,
        ),
      );
      if (isAgent) {
        _agentActivity = null;
      }
    });
    // The reply is the visible end of the run, so settle the avatar/status with it. A terminal
    // state normally follows, but it must not be the only thing that stops the header spinning.
    if (isAgent) {
      _agentStateProvider.reset();
    }
    // Follow the conversation only when the reader is already at the newest message;
    // otherwise leave their position alone so the jump-to-latest button surfaces instead.
    if (wasAtBottom) _scrollToBottom();
  }

  Future<void> _send(String text) async {
    final trimmed = text.trim();
    if (trimmed.isEmpty || _sending) return;

    final optimistic = SalonMessage(
      id: 'local-${DateTime.now().microsecondsSinceEpoch}',
      authorKind: 'User',
      text: trimmed,
      createdAt: DateTime.now(),
      deliveryStatus: MessageDeliveryStatus.sending,
    );

    setState(() {
      _messages.add(optimistic);
      _sending = true;
    });
    _scrollToBottom();

    final organizationId = _organizationId;
    final conversationId = _conversationId;
    final api = _conversationApi;

    // Without a live conversation (e.g. tests / not connected) there is nothing to send to;
    // confirm the optimistic bubble locally.
    if (organizationId == null || conversationId == null || api == null) {
      _confirmMessage(optimistic.id, optimistic.copyWith(deliveryStatus: null));
      return;
    }

    try {
      final confirmed = await api.sendMessage(
        organizationId: organizationId,
        conversationId: conversationId,
        text: trimmed,
      );
      _confirmMessage(optimistic.id, confirmed);

      // Only once the user message is confirmed does Aveline's activity bubble appear.
      setState(() {
        _agentActivity = AgentActivity(
          startedAt: DateTime.now(),
          currentState: AgentState.thinking,
        );
      });
      _agentStateProvider.apply(AgentState.thinking);
    } catch (_) {
      _confirmMessage(
        optimistic.id,
        optimistic.copyWith(deliveryStatus: MessageDeliveryStatus.failed),
      );
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  /// Replaces an optimistic message with its confirmed/failed counterpart.
  void _confirmMessage(String id, SalonMessage replacement) {
    if (!mounted) return;
    setState(() {
      final index = _messages.indexWhere((m) => m.id == id);
      if (index != -1) {
        _messages[index] = replacement;
      }
    });
  }

  /// Binds the Salon to a customer picked from a resolution `choice` block and re-triggers
  /// the agent with that customer in context (Issue #161).
  Future<void> _selectCustomer(String customerId) async {
    final organizationId = _organizationId;
    final conversationId = _conversationId;
    final api = _conversationApi;
    if (organizationId == null || conversationId == null || api == null) return;

    // Re-run the last staff question (or none) against the resolved customer; the backend
    // falls back to a summary prompt when no query is supplied.
    final lastStaff = _messages.lastWhere(
      (m) => m.authorKind == 'User' && !m.isFailed,
      orElse: () => SalonMessage(
        id: '',
        authorKind: 'User',
        text: '',
        createdAt: DateTime.now(),
      ),
    );

    try {
      await api.selectCustomer(
        organizationId: organizationId,
        conversationId: conversationId,
        customerId: customerId,
        query: lastStaff.text.isEmpty ? null : lastStaff.text,
      );
      if (!mounted) return;
      setState(() {
        _agentActivity = AgentActivity(
          startedAt: DateTime.now(),
          currentState: AgentState.thinking,
        );
      });
      _agentStateProvider.apply(AgentState.thinking);
    } catch (_) {
      // Best-effort; the agent reply (or lack of one) surfaces over the realtime channel.
    }
  }

  /// Tracks whether the reader is following the newest message, so an incoming reply only
  /// pulls the thread down when they are already at the bottom.
  void _onScroll() {
    if (_autoScrolling || !_scrollController.hasClients) return;
    _updateAtBottom();
  }

  void _updateAtBottom() {
    final position = _scrollController.position;
    final atBottom =
        position.maxScrollExtent - position.pixels <= _atBottomThreshold;
    if (atBottom != _atBottom && mounted) {
      setState(() => _atBottom = atBottom);
    }
  }

  /// Scrolls the thread to its newest message.
  ///
  /// Deferred to the next frame so the list has laid out and `maxScrollExtent` accounts for
  /// the messages that were just added; scrolling before that leaves the thread parked at
  /// the top of the conversation.
  void _scrollToBottom({bool animate = true}) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_scrollController.hasClients) return;

      final target = _scrollController.position.maxScrollExtent;
      _autoScrolling = true;

      if (!animate) {
        _scrollController.jumpTo(target);
        _autoScrolling = false;
        if (!_atBottom) setState(() => _atBottom = true);
        return;
      }

      _scrollController
          .animateTo(
            target,
            duration: const Duration(milliseconds: 250),
            curve: Curves.easeOut,
          )
          .whenComplete(() {
        _autoScrolling = false;
        if (mounted && !_atBottom) setState(() => _atBottom = true);
      });
    });
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _scrollController.removeListener(_onScroll);
    _realtimeService?.disconnect();
    _agentStateProvider.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    final thread = ListView.builder(
      controller: _scrollController,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 16),
      itemCount: _messages.length + (_agentActivity != null ? 1 : 0),
      itemBuilder: (context, index) {
        if (index >= _messages.length) {
          return Padding(
            padding: const EdgeInsets.only(bottom: 12),
            child: _AnimatedEntry(
              child: AgentActivityBubble(
                state: _agentActivity!.currentState,
              ),
            ),
          );
        }
        final message = _messages[index];
        return Padding(
          padding: const EdgeInsets.only(bottom: 12),
          child: _AnimatedEntry(
            child: MessageBubble(
              message: message,
              // Only follow a streaming reply while the reader is at the newest message.
              onStreamProgress: () {
                if (_atBottom) _scrollToBottom();
              },
              onSelectCustomer: _selectCustomer,
            ),
          ),
        );
      },
    );

    return Scaffold(
      appBar: AppBar(
        leading: IconButton(
          icon: const Icon(Icons.arrow_back_ios_new_rounded),
          onPressed: () => Navigator.of(context).pop(),
          tooltip: 'Back',
        ),
        titleSpacing: 0,
        title: Row(
          children: [
            ListenableBuilder(
              listenable: _agentStateProvider,
              builder: (context, _) {
                return AgentAvatar(
                  state: _agentStateProvider.state,
                  size: 22,
                  color: scheme.primary,
                );
              },
            ),
            const SizedBox(width: 10),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('The Salon', style: theme.textTheme.titleMedium),
                ListenableBuilder(
                  listenable: _agentStateProvider,
                  builder: (context, _) {
                    return Text(
                      _statusLabel(_agentStateProvider.state),
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                        fontSize: 11,
                      ),
                    );
                  },
                ),
              ],
            ),
          ],
        ),
      ),
      body: Column(
        children: [
          Expanded(
            child: _messages.isEmpty && _agentActivity == null
                ? const _EmptySalon()
                : Stack(
                    children: [
                      Positioned.fill(child: thread),
                      // Only offered while the reader has scrolled away from the newest
                      // message, so the thread can be returned to the bottom in one tap.
                      if (!_atBottom)
                        Positioned(
                          right: 12,
                          bottom: 12,
                          child: FloatingActionButton.small(
                            onPressed: () => _scrollToBottom(),
                            tooltip: 'Jump to latest',
                            child: const Icon(
                              Icons.keyboard_arrow_down_rounded,
                            ),
                          ),
                        ),
                    ],
                  ),
          ),
          SalonComposer(onSend: (text) => _send(text)),
        ],
      ),
    );
  }

  String _statusLabel(AgentState state) {
    return switch (state) {
      AgentState.idle => 'Aveline & specialists',
      AgentState.thinking => 'Thinking…',
      AgentState.searching => 'Searching…',
      AgentState.processing => 'Working…',
      AgentState.toolCall => 'Using a tool…',
      AgentState.waiting => 'Awaiting your decision…',
      AgentState.success => 'Done',
      AgentState.error => 'Something went wrong',
      AgentState.response => '',
    };
  }
}

/// Fades + slides a message bubble in once when it first appears.
class _AnimatedEntry extends StatefulWidget {
  const _AnimatedEntry({required this.child});

  final Widget child;

  @override
  State<_AnimatedEntry> createState() => _AnimatedEntryState();
}

class _AnimatedEntryState extends State<_AnimatedEntry>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _opacity;
  late final Animation<Offset> _slide;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 250),
    )..forward();
    _opacity = CurvedAnimation(parent: _controller, curve: Curves.easeOut);
    _slide = Tween<Offset>(
      begin: const Offset(0, 0.04),
      end: Offset.zero,
    ).animate(CurvedAnimation(parent: _controller, curve: Curves.easeOut));
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return FadeTransition(
      opacity: _opacity,
      child: SlideTransition(position: _slide, child: widget.child),
    );
  }
}

/// Shown when the Salon has no messages yet.
class _EmptySalon extends StatelessWidget {
  const _EmptySalon();

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Center(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'The Salon is quiet',
            style: theme.textTheme.titleLarge?.copyWith(
              fontFamily: 'Playfair Display',
              color: scheme.onSurfaceVariant,
            ),
          ),
          const SizedBox(height: 8),
          Text(
            'Ask Aveline anything about a customer, a piece, or a price.',
            textAlign: TextAlign.center,
            style: theme.textTheme.bodyMedium?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
        ],
      ),
    );
  }
}
