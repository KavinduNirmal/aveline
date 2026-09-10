import '../notifications/realtime_connection.dart';
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
/// forwards incoming `ReceiveMessage` / `ReceiveAgentState` messages to callbacks. Connects
/// on Salon open and disconnects when the Salon closes.
class ConversationRealtimeService {
  ConversationRealtimeService(this._connectionFactory);

  final RealtimeConnectionFactory _connectionFactory;
  RealtimeConnection? _connection;

  /// Connects to the conversation hub at [baseUrl]/hubs/conversations, authenticating with
  /// [getToken], joins the Salon group for [conversationId], and delivers incoming agent
  /// states to [onAgentState] and messages to [onMessage].
  Future<void> connect({
    required String baseUrl,
    required Future<String?> Function() getToken,
    required String organizationId,
    required String conversationId,
    required void Function(AgentStatePayload) onAgentState,
    void Function(SalonMessage)? onMessage,
  }) async {
    await disconnect();

    final connection = _connectionFactory(
      url: '$baseUrl/hubs/conversations',
      accessTokenFactory: getToken,
    );
    _connection = connection;

    connection.on('ReceiveAgentState', (arguments) {
      final first = arguments?.isNotEmpty == true ? arguments!.first : null;
      if (first is Map) {
        onAgentState(AgentStatePayload.fromJson(first.cast<String, dynamic>()));
      }
    });

    if (onMessage != null) {
      connection.on('ReceiveMessage', (arguments) {
        final first = arguments?.isNotEmpty == true ? arguments!.first : null;
        if (first is Map) {
          onMessage(SalonMessage.fromJson(first.cast<String, dynamic>()));
        }
      });
    }

    await connection.start();
    // Best-effort join; the hub verifies membership before adding to the group.
    await connection.invoke('JoinSalon', [organizationId, conversationId]);
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
