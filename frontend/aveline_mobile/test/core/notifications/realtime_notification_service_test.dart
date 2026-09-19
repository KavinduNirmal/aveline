import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/realtime_connection.dart';
import 'package:aveline_mobile/core/notifications/realtime_notification_service.dart';
import 'package:flutter_test/flutter_test.dart';

class FakeRealtimeConnection implements RealtimeConnection {
  final handlers = <String, void Function(List<Object?>?)>{};
  final invokedMethods = <String>[];
  final invokedArguments = <List<Object?>>[];
  bool started = false;
  bool stopped = false;
  String? url;
  Future<String?> Function()? accessTokenFactory;

  /// Whether the reconnect hooks were already in place when [start] ran.
  ///
  /// The service must register them before starting the socket: a reconnect can
  /// fire before `start()` returns, and a hook registered after that is missed.
  bool startSawReconnectedHook = false;
  bool startSawClosedHook = false;

  void Function()? reconnectedHandler;
  void Function(Object?)? closedHandler;

  /// When set, every [invoke] throws it, so a failed re-join can be exercised.
  Object? failInvoke;

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) {
    handlers[method] = handler;
  }

  @override
  Future<void> invoke(String method, List<Object?> arguments) async {
    if (failInvoke != null) {
      throw failInvoke!;
    }
    invokedMethods.add(method);
    invokedArguments.add(arguments);
  }

  @override
  Future<void> start() async {
    started = true;
    startSawReconnectedHook = reconnectedHandler != null;
    startSawClosedHook = closedHandler != null;
  }

  @override
  Future<void> stop() async {
    stopped = true;
  }

  @override
  void onReconnected(void Function() handler) => reconnectedHandler = handler;

  @override
  void onClosed(void Function(Object? error) handler) => closedHandler = handler;

  void emit(String method, List<Object?>? args) => handlers[method]?.call(args);

  void emitReconnected() => reconnectedHandler?.call();

  void emitClosed(Object? error) => closedHandler?.call(error);
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

    test('registers the reconnect hooks before starting the socket', () async {
      final connection = FakeRealtimeConnection();
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) => connection,
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (_) {},
      );

      // A socket can reconnect before start() returns; a hook registered after
      // that point would miss the first reconnection and go silently deaf.
      expect(connection.startSawReconnectedHook, isTrue);
      expect(connection.startSawClosedHook, isTrue);
    });

    test('a reconnect re-subscribes exactly once and refreshes', () async {
      final connection = FakeRealtimeConnection();
      var refreshes = 0;
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) => connection,
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (_) {},
        onReconnected: () => refreshes++,
      );

      connection.emitReconnected();
      await Future<void>.delayed(Duration.zero);

      expect(
        connection.invokedMethods.where((method) => method == 'SubscribeAsync'),
        hasLength(1),
      );
      expect(connection.invokedArguments.single, isEmpty);
      expect(refreshes, 1);
    });

    test('a failed re-join is surfaced rather than swallowed', () async {
      final connection = FakeRealtimeConnection()
        ..failInvoke = StateError('hub unreachable');
      Object? reported;
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) => connection,
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (_) {},
        onRejoinFailed: (error) => reported = error,
      );

      connection.emitReconnected();
      await Future<void>.delayed(Duration.zero);

      expect(reported, isA<StateError>());
    });

    test('a reconnect without a refresh callback still re-subscribes', () async {
      final connection = FakeRealtimeConnection();
      final service = RealtimeNotificationService(
        ({required url, required accessTokenFactory}) => connection,
      );

      await service.connect(
        baseUrl: 'http://localhost:5091',
        getToken: () async => 'tok',
        onNotification: (_) {},
      );

      connection.emitReconnected();
      await Future<void>.delayed(Duration.zero);

      expect(connection.invokedMethods, contains('SubscribeAsync'));
    });
  });
}
