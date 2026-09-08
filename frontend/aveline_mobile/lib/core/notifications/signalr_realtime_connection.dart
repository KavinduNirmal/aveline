import 'package:signalr_netcore/signalr_client.dart';

import 'realtime_connection.dart';

/// A [RealtimeConnection] backed by the `signalr_netcore` SignalR client.
class SignalRRealtimeConnection implements RealtimeConnection {
  SignalRRealtimeConnection._(this._connection);

  final HubConnection _connection;

  /// Builds a connection to [url], authenticating with [accessTokenFactory].
  factory SignalRRealtimeConnection.create({
    required String url,
    required Future<String?> Function() accessTokenFactory,
  }) {
    final connection = HubConnectionBuilder()
        .withUrl(
          url,
          options: HttpConnectionOptions(
            accessTokenFactory: () async => await accessTokenFactory() ?? '',
          ),
        )
        .withAutomaticReconnect()
        .build();
    return SignalRRealtimeConnection._(connection);
  }

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) {
    _connection.on(method, handler);
  }

  @override
  Future<void> start() async {
    await _connection.start();
  }

  @override
  Future<void> stop() => _connection.stop();
}
