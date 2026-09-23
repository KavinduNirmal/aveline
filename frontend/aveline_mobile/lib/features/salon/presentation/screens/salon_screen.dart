import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:provider/provider.dart';

import '../../../../core/config/app_config.dart';
import '../../../../core/network/auth_token_provider.dart';
import '../../../../core/network/conversation_realtime_service.dart';
import '../../../../core/notifications/realtime_connection_factory.dart';
import '../../../../core/providers/agent_state_provider.dart';
import '../../../../core/providers/user_provider.dart';
import '../../../conversations/presentation/attachment_picker.dart';
import '../../../conversations/presentation/client_thread_controller.dart';
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
  const SalonScreen({super.key, this.attachmentPicker = pickWithImagePicker});

  /// Opens the platform picker. Injectable so a widget test never touches the plugin.
  final AttachmentPicker attachmentPicker;

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

  /// Collapses an activity bubble the workflow never confirmed as finished.
  Timer? _activityTimeout;

  /// How many agent messages have arrived in this session. It is the run's generation:
  /// the hub can deliver the reply *before* the send's own HTTP response resolves, and
  /// the response must not then open a bubble for a run that has already answered.
  int _repliesSeen = 0;

  /// How long a live bubble may go without any word from the workflow before it is
  /// treated as stale.
  static const Duration _activityStaleAfter = Duration(seconds: 60);
  bool _sending = false;

  /// The files picked and not yet sent, keyed by the local id the tray draws.
  final Map<String, PendingThreadAttachment> _attachments = {};
  int _attachmentCount = 0;

  /// Why a pick was refused or a file could not be read, in the API's own words where it
  /// has them.
  String? _attachmentError;

  /// The staged replies whose decision is in flight, so a second tap cannot record a
  /// second decision on the same message.
  final Set<String> _deciding = {};

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
          // nothing would ever close again. `endsRun` rather than `isTerminal`: a run that
          // paused for a decision has stopped, and its `waiting` state is what closes the
          // bubble.
          if (activity == null && !state.endsRun) return;

          _agentStateProvider.apply(state);
          setState(() => _setActivity(nextAgentActivity(activity, state)));
        },
        onMessage: (payload) {
          // The realtime service hands over the raw `MessageDto` the hub sent, so `core`
          // needs no dependency on this feature's model.
          _handleIncomingMessage(SalonMessage.fromJson(payload));
        },
      );
    } catch (e) {
      debugPrint('[salon] realtime connect skipped/failed: $e');
    }
  }

  /// Sets the live activity bubble and re-arms the backstop that collapses it.
  ///
  /// The bubble is opened optimistically the moment a note is sent, and it is confirmed by
  /// whatever arrives next: a state, or the reply itself. A run that finishes without either
  /// — a dropped event, or a socket that went away mid-run — would otherwise leave the bubble
  /// up for the rest of the session, which is what "it never goes away" means.
  void _setActivity(AgentActivity? activity) {
    _agentActivity = activity;
    _activityTimeout?.cancel();
    if (activity == null) {
      return;
    }
    _activityTimeout = Timer(_activityStaleAfter, () {
      if (!mounted || _agentActivity == null) return;
      setState(() => _setActivity(null));
      _agentStateProvider.reset();
    });
  }

  /// Handles an incoming (agent) message: collapses the live activity into a "Thought for
  /// Xs" caption and appends the message.
  void _handleIncomingMessage(SalonMessage message) {
    if (!mounted) return;
    final wasAtBottom = _atBottom;
    final isAgent = message.authorKind == 'Agent';
    // A message can arrive more than once — the hub publishes to more than one group the
    // Salon belongs to, and a reconnect can replay one it already holds. A replay is not
    // added twice, but it *is* still the run's visible end, so it settles the activity
    // either way. Dropping it whole left the bubble that [send] opened spinning for good.
    final isReplay = _messages.any((m) => m.id == message.id);

    setState(() {
      if (!isReplay) {
        final activity = _agentActivity;
        final thoughtSeconds = isAgent && activity != null
            ? DateTime.now().difference(activity.startedAt).inMilliseconds / 1000.0
            : null;

        _messages.add(
          message.copyWith(
            thoughtSeconds: thoughtSeconds,
            streamIn: isAgent,
          ),
        );
      }
      if (isAgent) {
        _repliesSeen += 1;
        _setActivity(null);
      }
    });
    // The reply is the visible end of the run, so settle the avatar/status with it. A terminal
    // state normally follows, but it must not be the only thing that stops the header spinning.
    if (isAgent) {
      _agentStateProvider.reset();
    }
    // Follow the conversation only when the reader is already at the newest message;
    // otherwise leave their position alone so the jump-to-latest button surfaces instead.
    if (!isReplay && wasAtBottom) _scrollToBottom();
  }

  /// Whether every held file is stored. A send waits for its uploads rather than naming
  /// bytes the server has not written.
  bool get _attachmentsReady =>
      _attachments.values.every((attachment) => attachment.isUploaded);

  /// Picks from [source] and uploads each file, holding it until the send binds it.
  Future<void> _attach(AttachmentSource source) async {
    final organizationId = _organizationId;
    final conversationId = _conversationId;
    final api = _conversationApi;

    final List<PickedAttachment> picked;
    try {
      picked = await widget.attachmentPicker(source);
    } catch (_) {
      if (mounted) {
        setState(() => _attachmentError = 'That file could not be read.');
      }
      return;
    }

    for (final file in picked) {
      // The server refuses a sixth file when the send binds it, so refusing here means
      // nothing is uploaded that could never be bound.
      if (_attachments.length >= ClientThreadController.maxAttachmentsPerMessage) {
        if (mounted) {
          setState(
            () => _attachmentError =
                ClientThreadController.attachmentCapMessage,
          );
        }
        return;
      }

      final pending = PendingThreadAttachment(
        localId: 'local_att_${++_attachmentCount}',
        fileName: file.fileName,
        contentType: file.contentType,
        bytes: file.bytes,
      );

      if (mounted) {
        setState(() {
          _attachments[pending.localId] = pending;
          _attachmentError = null;
        });
      }

      // Without a live conversation there is nowhere to store the bytes (tests, or a Salon
      // that has not connected yet), so the file is held and marked rather than dropped.
      if (organizationId == null || conversationId == null || api == null) {
        pending.error = 'Could not upload ${pending.fileName}.';
        if (mounted) setState(() {});
        continue;
      }

      pending.uploading = true;
      await _upload(
        pending,
        api: api,
        organizationId: organizationId,
        conversationId: conversationId,
      );
    }
  }

  /// Stores one held file. The upload is its own step: the message binds the id afterwards,
  /// so a file the associate removes before sending stays unbound and the API sweeps it.
  Future<void> _upload(
    PendingThreadAttachment pending, {
    required ConversationApi api,
    required String organizationId,
    required String conversationId,
  }) async {
    try {
      final id = await api.uploadAttachment(
        organizationId: organizationId,
        conversationId: conversationId,
        bytes: pending.bytes,
        contentType: pending.contentType,
        fileName: pending.fileName,
        width: pending.width,
        height: pending.height,
      );
      pending.attachmentId = id.isEmpty ? null : id;
      // Named after the file: the tray draws this beside the retry control, and "Could not
      // upload." would not say which of several held files failed.
      pending.error = pending.attachmentId == null
          ? 'Could not upload ${pending.fileName}.'
          : null;
    } catch (_) {
      pending.attachmentId = null;
      pending.error = 'Could not upload ${pending.fileName}.';
    } finally {
      pending.uploading = false;
      if (mounted) setState(() {});
    }
  }

  /// Tries a failed upload again, with the bytes the associate already picked.
  Future<void> _retryAttachment(String localId) async {
    final pending = _attachments[localId];
    final organizationId = _organizationId;
    final conversationId = _conversationId;
    final api = _conversationApi;
    if (pending == null || !pending.isFailed) return;
    if (api == null || organizationId == null || conversationId == null) return;

    setState(() {
      pending.error = null;
      pending.uploading = true;
    });
    await _upload(
      pending,
      api: api,
      organizationId: organizationId,
      conversationId: conversationId,
    );
  }

  /// Drops a held file before it is sent.
  void _removeAttachment(String localId) {
    setState(() {
      _attachments.remove(localId);
      _attachmentError = null;
    });
  }

  /// Drops the files a send just bound.
  void _clearAttachments() {
    if (_attachments.isEmpty) return;
    if (!mounted) {
      _attachments.clear();
      return;
    }
    setState(() => _attachments.clear());
  }

  Future<void> _send(String text) async {
    final trimmed = text.trim();
    // A send waits for its uploads rather than naming bytes the server has not stored.
    if (trimmed.isEmpty || _sending || !_attachmentsReady) return;

    final attachmentIds = [
      for (final attachment in _attachments.values)
        if (attachment.attachmentId != null) attachment.attachmentId!,
    ];

    final optimistic = SalonMessage(
      id: 'local-${DateTime.now().microsecondsSinceEpoch}',
      authorKind: 'User',
      text: trimmed,
      createdAt: DateTime.now(),
      deliveryStatus: MessageDeliveryStatus.sending,
    );

    final repliesBeforeSend = _repliesSeen;

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
      _clearAttachments();
      return;
    }

    try {
      final confirmed = await api.sendMessage(
        organizationId: organizationId,
        conversationId: conversationId,
        text: trimmed,
        attachmentIds: attachmentIds,
      );
      _confirmMessage(optimistic.id, confirmed);
      _clearAttachments();

      // Only once the user message is confirmed does Aveline's activity bubble appear.
      // The reply can land before this response does — the hub publishes it the moment the
      // workflow answers, while the send's own response is still in flight. Opening the
      // bubble regardless left a "Thinking…" card under every reply that nothing would ever
      // close, because the only thing that closes one is a reply that has already arrived.
      if (_repliesSeen == repliesBeforeSend) {
        setState(() {
          _setActivity(AgentActivity(
            startedAt: DateTime.now(),
            currentState: AgentState.thinking,
          ));
        });
        _agentStateProvider.apply(AgentState.thinking);
      }
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
        _setActivity(AgentActivity(
          startedAt: DateTime.now(),
          currentState: AgentState.thinking,
        ));
      });
      _agentStateProvider.apply(AgentState.thinking);
    } catch (_) {
      // Best-effort; the agent reply (or lack of one) surfaces over the realtime channel.
    }
  }

  /// Reads one attachment's bytes for the thread's image cards.
  ///
  /// The stored route is deliberately not used as an `<Image.network>` source: an
  /// image element cannot carry the bearer token, so the bytes are fetched through
  /// the same authenticated client every other call uses.
  Future<Uint8List> _loadAttachment(String attachmentId) {
    final api = _conversationApi;
    final organizationId = _organizationId;
    final conversationId = _conversationId;
    if (api == null || organizationId == null || conversationId == null) {
      throw StateError('The Salon is not connected to a conversation.');
    }
    return api.fetchAttachmentBytes(
      organizationId: organizationId,
      conversationId: conversationId,
      attachmentId: attachmentId,
    );
  }

  /// Records the associate's decision on a staged reply (ADR-024).
  ///
  /// The decision is bound to the content hash the associate was shown, so the whole
  /// message is needed rather than just the boolean. The server's own copy of the
  /// decided message replaces the staged one, which is what stops the buttons hanging
  /// around after they have been pressed.
  Future<void> _decideSignOff(SalonMessage message, bool approved) async {
    final api = _conversationApi;
    final organizationId = _organizationId;
    final conversationId = _conversationId;
    final hash = message.contentHash;
    if (api == null ||
        organizationId == null ||
        conversationId == null ||
        hash == null ||
        hash.isEmpty) {
      return;
    }
    // A second tap while the first is in flight would be a second decision on the same
    // message, which the API refuses. The set keeps the button honest instead.
    if (!_deciding.add(message.id)) {
      return;
    }
    setState(() {});

    try {
      final decided = await api.decideSignOff(
        organizationId: organizationId,
        conversationId: conversationId,
        messageId: message.id,
        contentHash: hash,
        approved: approved,
      );
      _confirmMessage(message.id, decided);
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('That decision could not be recorded.')),
        );
      }
    } finally {
      if (mounted) {
        setState(() => _deciding.remove(message.id));
      } else {
        _deciding.remove(message.id);
      }
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
  ///
  /// The list is built lazily, so the first `maxScrollExtent` is an estimate drawn from the
  /// children that happened to fit. An immediate jump lands a few pixels short of the true
  /// end, which is enough for the thread to open with the newest bubble clipped. One more
  /// pass after that frame has settled lands on the end the list actually has.
  void _scrollToBottom({bool animate = true}) {
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted || !_scrollController.hasClients) return;

      final target = _scrollController.position.maxScrollExtent;
      _autoScrolling = true;

      if (!animate) {
        _scrollController.jumpTo(target);
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!mounted || !_scrollController.hasClients) return;
          final settled = _scrollController.position.maxScrollExtent;
          if (settled != target) {
            _scrollController.jumpTo(settled);
          }
          _autoScrolling = false;
          if (!_atBottom) setState(() => _atBottom = true);
        });
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
    _activityTimeout?.cancel();
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
              onSignOff: _decideSignOff,
              loadAttachment: _loadAttachment,
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
          if (_attachmentError != null)
            _AttachmentNotice(message: _attachmentError!),
          SalonComposer(
            onSend: (text) => _send(text),
            onAttach: _attach,
            attachments: _attachments.values.toList(growable: false),
            sendReady: _attachmentsReady,
            onRemoveAttachment: _removeAttachment,
            onRetryAttachment: _retryAttachment,
          ),
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
/// Why a file was refused, above the composer that would have held it.
class _AttachmentNotice extends StatelessWidget {
  const _AttachmentNotice({required this.message});

  final String message;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: const Key('salon_attachment_notice'),
      width: double.infinity,
      color: scheme.errorContainer,
      padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      child: Row(
        children: [
          Icon(Icons.error_outline, size: 16, color: scheme.onErrorContainer),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              message,
              style: theme.textTheme.bodySmall?.copyWith(
                color: scheme.onErrorContainer,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

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
