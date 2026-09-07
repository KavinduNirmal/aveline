import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/realtime_connection.dart';
import 'package:aveline_mobile/core/notifications/realtime_notification_service.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeRealtimeConnection implements RealtimeConnection {
  final handlers = <String, void Function(List<Object?>?)>{};
  bool started = false;
  bool stopped = false;
  String? url;
  Future<String?> Function()? accessTokenFactory;

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) {
    handlers[method] = handler;
  }

  @override
  Future<void> start() async {
    started = true;
  }

  @override
  Future<void> stop() async {
    stopped = true;
  }

  void emit(String method, List<Object?>? args) => handlers[method]?.call(args);
}

void main() {
  group('RealtimeNotificationService', () {
    test('connect builds a connection to the hub and starts it', () async {
      FakeRealtimeConnection? created;
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) {
          created = FakeRealtimeConnection()
            ..url = url
            ..accessTokenFactory = accessTokenFactory;
          return created!;
        },
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (_) {},
      );

      expect(created, isNotNull);
      expect(created!.url, 'http://localhost:5091/hubs/notifications');
      expect(created!.started, isTrue);
      expect(await created!.accessTokenFactory!(), 'tok');
    });

    test('forwards ReceiveNotification payloads to the callback', () async {
      final connection = FakeRealtimeConnection();
      NotificationPayload? received;
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) => connection,
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (p) => received = p,
      );

      connection.emit('ReceiveNotification', [
        {'type': 'NewMessage', 'title': 'Hi', 'body': 'A customer messaged', 'data': {'customerId': 'c-1'}},
      ]);

      expect(received, isNotNull);
      expect(received!.type, 'NewMessage');
      expect(received!.title, 'Hi');
      expect(received!.data['customerId'], 'c-1');
    });

    test('disconnect stops the active connection', () async {
      final connection = FakeRealtimeConnection();
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) => connection,
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (_) {},
      );
      await service.disconnect();

      expect(connection.stopped, isTrue);
    });
  });
}
