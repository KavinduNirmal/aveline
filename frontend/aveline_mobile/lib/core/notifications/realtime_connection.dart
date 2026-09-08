/// A minimal realtime (SignalR) connection abstraction so the realtime service can be
/// unit tested without a live hub. Implementations wrap a concrete SignalR client.
abstract interface class RealtimeConnection {
  /// Registers a handler for a hub method (e.g. `ReceiveNotification`).
  void on(String method, void Function(List<Object?>? arguments) handler);

  /// Starts the connection.
  Future<void> start();

  /// Stops the connection.
  Future<void> stop();
}

/// Builds a [RealtimeConnection] for a hub URL, authenticating with the supplied token.
typedef RealtimeConnectionFactory = RealtimeConnection Function({
  required String url,
  required Future<String?> Function() accessTokenFactory,
});
