import 'dart:async';

import 'notification_payload.dart';
import 'realtime_connection.dart';

/// Establishes a foreground realtime (SignalR) connection to the notifications hub and
/// forwards incoming `ReceiveNotification` messages to a callback. Connects on sign-in and
/// disconnects on sign-out.
///
/// Group membership is not preserved across a reconnect: `withAutomaticReconnect`
/// rebuilds the socket, but a rebuilt socket is in no group the old one joined, so
/// the hub's `user:{id}` and `org:{id}` groups must be re-joined from the client.
/// [connect] registers the reconnect hooks before starting the socket and invokes the
/// hub's idempotent `SubscribeAsync` on every reconnection, then tells [onReconnected]
/// so the caller can re-read what the outage missed.
class RealtimeNotificationService {
  RealtimeNotificationService(this._connectionFactory);

  final RealtimeConnectionFactory _connectionFactory;
  RealtimeConnection? _connection;

  /// Connects to the notifications hub at [baseUrl]/hubs/notifications, authenticating
  /// with [getToken]. Incoming notifications are delivered to [onNotification].
  ///
  /// [onReconnected] fires once per successful reconnection, after the re-join was
  /// attempted: anything that arrived while the socket was down was never delivered and
  /// is only recoverable by re-reading the API.
  ///
  /// [onRejoinFailed] is told when the re-join itself failed, rather than the failure
  /// being swallowed: a connection that never re-joined looks exactly like a quiet
  /// inbox.
  Future<void> connect({
    required String baseUrl,
    required Future<String?> Function() getToken,
    required void Function(NotificationPayload) onNotification,
    void Function()? onReconnected,
    void Function(Object error)? onRejoinFailed,
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

    // Registered before `start()`: a rebuilt socket can fire a reconnection before the
    // call below returns, and a hook registered after that would miss it and receive
    // nothing for the rest of the session.
    connection.onReconnected(() {
      unawaited(_handleReconnect(connection, onReconnected, onRejoinFailed));
    });

    connection.onClosed((error) {
      // Nothing to do but leave the state available to a caller that cares; the
      // automatic reconnect owns the retry.
    });

    await connection.start();
  }

  /// Re-joins the hub's groups after a reconnect, then tells the caller.
  ///
  /// The re-join is attempted before [onReconnected] is told, so a re-read cannot
  /// race the group membership.
  Future<void> _handleReconnect(
    RealtimeConnection connection,
    void Function()? onReconnected,
    void Function(Object error)? onRejoinFailed,
  ) async {
    try {
      await connection.invoke('SubscribeAsync', const []);
    } catch (error) {
      onRejoinFailed?.call(error);
    }
    onReconnected?.call();
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
