import '../notifications/realtime_connection.dart';
import '../../features/conversations/domain/conversation.dart';
import '../../features/salon/domain/salon_message.dart';

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

  factory AgentStatePayload.fromJson(Map<String, dynamic> json) {
    return AgentStatePayload(
      conversationId: json['conversationId'] as String? ?? '',
      state: json['state'] as String? ?? 'idle',
      agentKey: json['agentKey'] as String?,
      traceId: json['traceId'] as String?,
    );
  }
}

/// Establishes a foreground realtime (SignalR) connection to the conversation hub and
/// forwards incoming `ReceiveMessage` / `ReceiveAgentState` / `ReceiveConversationChanged`
/// messages to callbacks. Each screen constructs its own instance, so the Salon and the
/// inbox can both be live without fighting over one connection.
class ConversationRealtimeService {
  ConversationRealtimeService(this._connectionFactory);

  final RealtimeConnectionFactory _connectionFactory;
  RealtimeConnection? _connection;

  /// Connects to the conversation hub at [baseUrl]/hubs/conversations, authenticating with
  /// [getToken], and forwards the events the caller asked for.
  ///
  /// When [conversationId] is supplied the connection also invokes
  /// `JoinSalon(organizationId, conversationId)`. The inbox omits both: the org and user
  /// groups are joined on connect, so a `JoinSalon`-free connection already receives every
  /// `ReceiveConversationChanged` tile the caller may see.
  Future<void> connect({
    required String baseUrl,
    required Future<String?> Function() getToken,
    String? organizationId,
    String? conversationId,
    void Function(AgentStatePayload)? onAgentState,
    void Function(SalonMessage)? onMessage,
    void Function(Conversation)? onConversationChanged,
  }) async {
    await disconnect();

    final connection = _connectionFactory(
      url: '$baseUrl/hubs/conversations',
      accessTokenFactory: getToken,
    );
    _connection = connection;

    final agentState = onAgentState;
    if (agentState != null) {
      connection.on('ReceiveAgentState', (arguments) {
        final first = arguments?.isNotEmpty == true ? arguments!.first : null;
        if (first is Map) {
          agentState(AgentStatePayload.fromJson(first.cast<String, dynamic>()));
        }
      });
    }

    final message = onMessage;
    if (message != null) {
      connection.on('ReceiveMessage', (arguments) {
        final first = arguments?.isNotEmpty == true ? arguments!.first : null;
        if (first is Map) {
          message(SalonMessage.fromJson(first.cast<String, dynamic>()));
        }
      });
    }

    final conversationChanged = onConversationChanged;
    if (conversationChanged != null) {
      connection.on('ReceiveConversationChanged', (arguments) {
        final first = arguments?.isNotEmpty == true ? arguments!.first : null;
        if (first is Map) {
          conversationChanged(
            Conversation.fromJson(first.cast<String, dynamic>()),
          );
        }
      });
    }

    await connection.start();
    // Best-effort join; the hub verifies membership before adding to the group.
    if (organizationId != null && conversationId != null) {
      await connection.invoke('JoinSalon', [organizationId, conversationId]);
    }
  }

  /// Stops the connection if one is active.
  Future<void> disconnect() async {
    final connection = _connection;
    _connection = null;
    if (connection != null) {
      await connection.stop();
    }
  }
}
