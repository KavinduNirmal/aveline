/// A minimal realtime (SignalR) connection abstraction so the realtime service can be
/// unit tested without a live hub. Implementations wrap a concrete SignalR client.
abstract interface class RealtimeConnection {
  /// Registers a handler for a hub method (e.g. `ReceiveNotification`).
  void on(String method, void Function(List<Object?>? arguments) handler);

  /// Invokes a client-to-server hub method (e.g. `JoinSalon`) with the given arguments.
  Future<void> invoke(String method, List<Object?> arguments);

  /// Starts the connection.
  Future<void> start();

  /// Stops the connection.
  Future<void> stop();

  /// Registers a handler for a successful reconnection.
  ///
  /// `withAutomaticReconnect` rebuilds the socket, but a rebuilt socket is no longer in
  /// any group the old one joined, so a caller with group membership has to re-join.
  /// Without this hook the connection looks alive and silently receives nothing.
  void onReconnected(void Function() handler);

  /// Registers a handler for the connection closing, with the error when there was one.
  void onClosed(void Function(Object? error) handler);
}

/// Builds a [RealtimeConnection] for a hub URL, authenticating with the supplied token.
typedef RealtimeConnectionFactory = RealtimeConnection Function({
  required String url,
  required Future<String?> Function() accessTokenFactory,
});
