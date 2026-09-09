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
import '../../domain/agent_state.dart';
import '../../domain/salon_message.dart';
import '../widgets/agent_activity_bubble.dart';
import '../widgets/agent_avatar.dart';
import '../widgets/message_bubble.dart';
import '../widgets/salon_composer.dart';

/// Aveline's in-progress reasoning, shown as a live activity bubble until a reply lands.
class _AgentActivity {
  const _AgentActivity({required this.startedAt, required this.currentState});

  final DateTime startedAt;
  final AgentState currentState;
}

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

class _SalonScreenState extends State<SalonScreen> {
  final List<SalonMessage> _messages = _seedMessages();
  final ScrollController _scrollController = ScrollController();
  final AgentStateProvider _agentStateProvider = AgentStateProvider();
  ConversationRealtimeService? _realtimeService;
  ConversationApi? _conversationApi;
  String? _organizationId;
  String? _conversationId;
  _AgentActivity? _agentActivity;
  bool _sending = false;

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
    _connectRealtime();
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

      final service = ConversationRealtimeService(defaultRealtimeConnectionFactory);
      _realtimeService = service;
      await service.connect(
        baseUrl: config.apiBaseUrl,
        getToken: authRepository.getToken,
        organizationId: organizationId,
        conversationId: conversation.id,
        onAgentState: (payload) {
          final state = AgentState.fromWire(payload.state);
          _agentStateProvider.apply(state);
          setState(() {
            _agentActivity = _agentActivity == null
                ? _AgentActivity(startedAt: DateTime.now(), currentState: state)
                : _AgentActivity(
                    startedAt: _agentActivity!.startedAt,
                    currentState: state,
                  );
          });
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
    setState(() {
      if (_messages.any((m) => m.id == message.id)) return;

      final activity = _agentActivity;
      final isAgent = message.authorKind == 'Agent';
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
    _scrollToBottom();
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
        _agentActivity = _AgentActivity(
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

  void _scrollToBottom() {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (_scrollController.hasClients) {
        _scrollController.animateTo(
          _scrollController.position.maxScrollExtent,
          duration: const Duration(milliseconds: 250),
          curve: Curves.easeOut,
        );
      }
    });
  }

  @override
  void dispose() {
    _realtimeService?.disconnect();
    _agentStateProvider.dispose();
    _scrollController.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

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
                : ListView.builder(
                    controller: _scrollController,
                    padding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 16,
                    ),
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
                            onStreamProgress: _scrollToBottom,
                          ),
                        ),
                      );
                    },
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
