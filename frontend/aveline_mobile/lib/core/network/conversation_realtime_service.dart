import '../notifications/realtime_connection.dart';
import '../../features/conversations/domain/conversation.dart';

/// A single `ReceiveAgentState` payload from the conversation hub.
class AgentStatePayload {
  const AgentStatePayload({
    required this.conversationId,
    required this.state,
    this.agentKey,
    this.traceId,
  });

  final String conversationId;
  final String state;
  final String? agentKey;
  final String? traceId;

  /// The payload [json] describes, or `null` when it carries no state.
  ///
  /// A missing or blank `state` is a malformed payload, not an idle agent: coercing it to
  /// `idle` masks contract drift, so it is dropped and the last real state stands.
  static AgentStatePayload? tryFromJson(Map<String, dynamic> json) {
    final state = json['state'];
    if (state is! String || state.isEmpty) {
      return null;
    }
    return AgentStatePayload(
      conversationId: json['conversationId'] as String? ?? '',
      state: state,
      agentKey: json['agentKey'] as String?,
      traceId: json['traceId'] as String?,
    );
  }
}

/// Establishes a foreground realtime (SignalR) connection to the conversation hub and
/// forwards incoming `ReceiveMessage` / `ReceiveAgentState` / `ReceiveConversationChanged`
/// messages to its listeners. Each screen constructs its own instance, so the Salon and the
/// inbox can both be live without fighting over one connection.
///
/// The hub handlers are registered **unconditionally**, and the listeners are a set rather
/// than a single callback, so two subscribers can share one instance and a listener that
/// attaches after `connect` still receives events.
class ConversationRealtimeService {
  ConversationRealtimeService(this._connectionFactory);

  final RealtimeConnectionFactory _connectionFactory;
  RealtimeConnection? _connection;

  final Set<void Function(Map<String, dynamic> payload)> _messageListeners = {};
  final Set<void Function(AgentStatePayload payload)> _agentStateListeners = {};
  final Set<void Function(Conversation conversation)> _conversationListeners = {};
  final Set<void Function()> _reconnectedListeners = {};

  /// The groups this connection joined, so a rebuilt socket can join them again.
  String? _organizationId;
  String? _conversationId;

  /// Every route in this module is `{organizationId:guid}`/`{conversationId:guid}`, and the
  /// hub binds `JoinSalon(Guid, Guid)`. A non-UUID id would fail the bind inside the hub with
  /// nothing the caller could act on, so it is refused here instead.
  static final RegExp _uuid = RegExp(
    r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
  );

  /// Listens for a message payload (the raw `MessageDto` as the hub sent it).
  void addMessageListener(void Function(Map<String, dynamic> payload) listener) =>
      _messageListeners.add(listener);

  void removeMessageListener(void Function(Map<String, dynamic> payload) listener) =>
      _messageListeners.remove(listener);

  /// Listens for agent-state payloads.
  void addAgentStateListener(void Function(AgentStatePayload payload) listener) =>
      _agentStateListeners.add(listener);

  void removeAgentStateListener(void Function(AgentStatePayload payload) listener) =>
      _agentStateListeners.remove(listener);

  /// Listens for conversation-tile changes.
  void addConversationListener(void Function(Conversation conversation) listener) =>
      _conversationListeners.add(listener);

  void removeConversationListener(void Function(Conversation conversation) listener) =>
      _conversationListeners.remove(listener);

  /// Listens for a successful reconnection, which is when a caller with a window on screen
  /// should re-read it: anything said while the socket was down was never delivered.
  void addReconnectedListener(void Function() listener) =>
      _reconnectedListeners.add(listener);

  void removeReconnectedListener(void Function() listener) =>
      _reconnectedListeners.remove(listener);

  /// Connects to the conversation hub at [baseUrl]/hubs/conversations, authenticating with
  /// [getToken], and forwards the events it was asked for.
  ///
  /// When [conversationId] and [organizationId] are supplied the connection also invokes
  /// `JoinSalon(organizationId, conversationId)` and re-joins after a reconnect. The inbox
  /// omits both: the org and user groups are joined on connect, so a `JoinSalon`-free
  /// connection already receives every `ReceiveConversationChanged` tile the caller may see.
  ///
  /// [onJoinFailed] is told when the join itself failed, rather than the failure being
  /// swallowed: a thread that never joined looks exactly like a thread nobody is writing in.
  /// [onReconnected] fires only after the socket was rebuilt, never on the first start.
  Future<void> connect({
    required String baseUrl,
    required Future<String?> Function() getToken,
    String? organizationId,
    String? conversationId,
    void Function(AgentStatePayload)? onAgentState,
    void Function(Map<String, dynamic>)? onMessage,
    void Function(Conversation)? onConversationChanged,
    void Function(Object error)? onJoinFailed,
    void Function()? onReconnected,
  }) async {
    await disconnect();

    if (organizationId != null) {
      _requireUuid(organizationId, 'organizationId');
    }
    if (conversationId != null) {
      _requireUuid(conversationId, 'conversationId');
    }
    _organizationId = organizationId;
    _conversationId = conversationId;

    if (onAgentState != null) {
      addAgentStateListener(onAgentState);
    }
    if (onMessage != null) {
      addMessageListener(onMessage);
    }
    if (onConversationChanged != null) {
      addConversationListener(onConversationChanged);
    }
    if (onReconnected != null) {
      addReconnectedListener(onReconnected);
    }

    final connection = _connectionFactory(
      url: '$baseUrl/hubs/conversations',
      accessTokenFactory: getToken,
    );
    _connection = connection;

    // Registered whatever the caller asked for: a listener that attaches later still gets
    // the events, and the registration is what makes the hub deliver them at all.
    connection.on('ReceiveAgentState', (arguments) {
      final first = arguments?.isNotEmpty == true ? arguments!.first : null;
      if (first is! Map) {
        return;
      }
      final payload = AgentStatePayload.tryFromJson(first.cast<String, dynamic>());
      if (payload == null) {
        return;
      }
      for (final listener in [..._agentStateListeners]) {
        listener(payload);
      }
    });

    connection.on('ReceiveMessage', (arguments) {
      final first = arguments?.isNotEmpty == true ? arguments!.first : null;
      if (first is! Map) {
        return;
      }
      final payload = first.cast<String, dynamic>();
      for (final listener in [..._messageListeners]) {
        listener(payload);
      }
    });

    connection.on('ReceiveConversationChanged', (arguments) {
      final first = arguments?.isNotEmpty == true ? arguments!.first : null;
      if (first is! Map) {
        return;
      }
      final conversation = Conversation.fromJson(first.cast<String, dynamic>());
      for (final listener in [..._conversationListeners]) {
        listener(conversation);
      }
    });

    connection.onReconnected(() {
      // A rebuilt socket is in no group the old one joined, so re-join before the callers
      // are told, or a re-read would race the group membership.
      final org = _organizationId;
      final conv = _conversationId;
      if (org != null && conv != null) {
        connection.invoke('JoinSalon', [org, conv]).catchError((Object _) {});
      }
      for (final listener in [..._reconnectedListeners]) {
        listener();
      }
    });

    connection.onClosed((error) {
      // Nothing to do but leave the state available to a caller that cares; the automatic
      // reconnect owns the retry.
    });

    await connection.start();
    await _join(connection, onJoinFailed);
  }

  /// Joins the conversation's group, surfacing a failure instead of swallowing it.
  Future<void> _join(
    RealtimeConnection connection,
    void Function(Object error)? onJoinFailed,
  ) async {
    final org = _organizationId;
    final conv = _conversationId;
    if (org == null || conv == null) {
      return;
    }
    try {
      await connection.invoke('JoinSalon', [org, conv]);
    } catch (error) {
      onJoinFailed?.call(error);
    }
  }

  /// Stops the connection if one is active and drops every listener.
  Future<void> disconnect() async {
    final connection = _connection;
    _connection = null;
    _organizationId = null;
    _conversationId = null;
    _messageListeners.clear();
    _agentStateListeners.clear();
    _conversationListeners.clear();
    _reconnectedListeners.clear();
    if (connection != null) {
      await connection.stop();
    }
  }

  static void _requireUuid(String value, String name) {
    if (!_uuid.hasMatch(value)) {
      throw ArgumentError.value(value, name, 'must be a UUID');
    }
  }
}
