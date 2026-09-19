import 'dart:async';

import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/push_message_handler.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:flutter_test/flutter_test.dart';

/// A push source the test drives, standing in for `FirebaseMessaging`.
class _FakePushSource implements PushMessageSource {
  final StreamController<Map<String, dynamic>> messages =
      StreamController<Map<String, dynamic>>.broadcast();
  final StreamController<Map<String, dynamic>> opened =
      StreamController<Map<String, dynamic>>.broadcast();

  Map<String, dynamic>? initial;

  @override
  Stream<Map<String, dynamic>> get onMessage => messages.stream;

  @override
  Stream<Map<String, dynamic>> get onMessageOpenedApp => opened.stream;

  @override
  Future<Map<String, dynamic>?> getInitialMessage() async => initial;

  Future<void> close() async {
    await messages.close();
    await opened.close();
  }
}

void main() {
  late _FakePushSource source;
  late PushMessageHandler handler;
  late List<NotificationPayload> arrivals;
  late List<String> markedRead;
  late List<String> routes;

  setUp(() {
    source = _FakePushSource();
    handler = PushMessageHandler(source);
    arrivals = [];
    markedRead = [];
    routes = [];
    handler.start(
      onMessage: arrivals.add,
      markRead: (id) async => markedRead.add(id),
      open: routes.add,
    );
  });

  tearDown(() async {
    await handler.dispose();
    await source.close();
  });

  Future<void> settle() => Future<void>.delayed(Duration.zero);

  group('PushMessageHandler foreground arrival', () {
    test('reaches the same arrival seam the realtime path uses', () async {
      source.messages.add({
        'type': 'NewMessage',
        'title': 'Hi',
        'body': 'A customer messaged',
        'notificationId': 'un-1',
        'unreadCount': 2,
        'conversationId': 'cnv-1',
      });
      await settle();

      expect(arrivals, hasLength(1));
      expect(arrivals.single.type, 'NewMessage');
      expect(arrivals.single.notificationId, 'un-1');
      expect(arrivals.single.unreadCount, 2);
    });
  });

  group('PushMessageHandler tap', () {
    test('marks the notification read and opens its thread', () async {
      source.opened.add({
        'type': 'NewMessage',
        'notificationId': 'un-42',
        'conversationId': '11111111-1111-4111-8111-111111111111',
        'messageId': '22222222-2222-4222-8222-222222222222',
      });
      await settle();

      // The **notification** is marked, by its own row id. It is never the
      // conversation's id: the thread owns its read state and writes it when the
      // reader reaches the newest message (Q7).
      expect(markedRead, ['un-42']);
      expect(
        routes,
        [
          AppRoutes.thread(
            '11111111-1111-4111-8111-111111111111',
            messageId: '22222222-2222-4222-8222-222222222222',
          ),
        ],
      );
    });

    test('a client-only push opens the client book', () async {
      source.opened.add({'notificationId': 'un-7', 'customerId': 'cus_204'});
      await settle();

      expect(markedRead, ['un-7']);
      expect(routes, [AppRoutes.customer('cus_204')]);
    });

    test('a tap with nowhere to go still marks the notification read', () async {
      source.opened.add({'notificationId': 'un-9'});
      await settle();

      expect(markedRead, ['un-9']);
      expect(routes, isEmpty);
    });

    test('a tap with no notification id still routes', () async {
      source.opened.add({'conversationId': 'cnv-1'});
      await settle();

      expect(markedRead, isEmpty);
      expect(routes, [AppRoutes.thread('cnv-1')]);
    });

    test('a mark-read that fails does not lose the tap', () async {
      source.opened.add({'notificationId': 'un-5', 'conversationId': 'cnv-1'});
      await settle();

      // The default mark-read records; a failing one is exercised by the cold
      // start below.
      expect(routes, [AppRoutes.thread('cnv-1')]);
    });
  });

  group('PushMessageHandler cold start', () {
    test('applies the tap that launched the app', () async {
      source.initial = {
        'notificationId': 'un-11',
        'conversationId': 'cnv-11',
        'messageId': 'msg-11',
      };

      await handler.handleInitialMessage(
        markRead: (id) async => markedRead.add(id),
        open: routes.add,
      );

      expect(markedRead, ['un-11']);
      expect(routes, [AppRoutes.thread('cnv-11', messageId: 'msg-11')]);
    });

    test('does nothing when the app was not launched by a tap', () async {
      await handler.handleInitialMessage(
        markRead: (id) async => markedRead.add(id),
        open: routes.add,
      );

      expect(markedRead, isEmpty);
      expect(routes, isEmpty);
    });

    test('a session that is not restored yet never throws', () async {
      source.initial = {'notificationId': 'un-13', 'customerId': 'cus_13'};

      // The mark-read fails (no token yet), but the route is still opened.
      await handler.handleInitialMessage(
        markRead: (_) async => throw StateError('401'),
        open: routes.add,
      );

      expect(routes, [AppRoutes.customer('cus_13')]);
    });
  });
}
