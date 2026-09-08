import 'notification_payload.dart';
import 'realtime_connection.dart';

/// Establishes a foreground realtime (SignalR) connection to the notifications hub and
/// forwards incoming `ReceiveNotification` messages to a callback. Connects on sign-in and
/// disconnects on sign-out.
class RealtimeNotificationService {
  RealtimeNotificationService(this._connectionFactory);

  final RealtimeConnectionFactory _connectionFactory;
  RealtimeConnection? _connection;

  /// Connects to the notifications hub at [baseUrl]/hubs/notifications, authenticating
  /// with [getToken]. Incoming notifications are delivered to [onNotification].
  Future<void> connect({
    required String baseUrl,
    required Future<String?> Function() getToken,
    required void Function(NotificationPayload) onNotification,
  }) async {
    await disconnect();

    final connection = _connectionFactory(
      url: '$baseUrl/hubs/notifications',
      accessTokenFactory: getToken,
    );
    _connection = connection;

    connection.on('ReceiveNotification', (arguments) {
      final first = arguments?.isNotEmpty == true ? arguments!.first : null;
      if (first is Map) {
        onNotification(NotificationPayload.fromJson(first.cast<String, dynamic>()));
      }
    });

    await connection.start();
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
