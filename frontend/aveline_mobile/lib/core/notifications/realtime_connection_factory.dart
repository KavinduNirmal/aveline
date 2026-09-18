import 'realtime_connection.dart';
import 'signalr_realtime_connection.dart';

/// Default [RealtimeConnectionFactory] backed by the SignalR client.
RealtimeConnection defaultRealtimeConnectionFactory({
  required String url,
  required Future<String?> Function() accessTokenFactory,
}) {
  return SignalRRealtimeConnection.create(
    url: url,
    accessTokenFactory: accessTokenFactory,
  );
}
