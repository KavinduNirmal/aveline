import 'package:aveline_mobile/core/notifications/notification_route.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:aveline_mobile/features/notifications/domain/app_notification.dart';
import 'package:aveline_mobile/features/notifications/presentation/screens/notifications_screen.dart';
import 'package:flutter_test/flutter_test.dart';

AppNotification _notification(Map<String, String?> data) => AppNotification(
  id: 'n1',
  notificationId: 'n1',
  type: 'message',
  title: 'A title',
  body: 'A body',
  data: data,
  isRead: false,
  createdAt: DateTime.utc(2026, 9, 19),
);

void main() {
  group('notificationRouteFor', () {
    test('a thread notification opens the thread, anchored to its message', () {
      final location = notificationRouteFor(_notification({
        'conversationId': '11111111-1111-4111-8111-111111111111',
        'messageId': '22222222-2222-4222-8222-222222222222',
      }));

      expect(
        location,
        AppRoutes.thread(
          '11111111-1111-4111-8111-111111111111',
          messageId: '22222222-2222-4222-8222-222222222222',
        ),
      );
      expect(location, contains('messageId=22222222'));
    });

    test('a thread notification with no message opens the newest words', () {
      final location = notificationRouteFor(_notification({
        'conversationId': '11111111-1111-4111-8111-111111111111',
      }));

      expect(
        location,
        '/conversations/thread/11111111-1111-4111-8111-111111111111',
      );
    });

    test('a thread notification wins over a client one', () {
      // A thread with an identified client carries both; the thread is what was notified about.
      final location = notificationRouteFor(_notification({
        'conversationId': '11111111-1111-4111-8111-111111111111',
        'customerId': 'cus_204',
      }));

      expect(location, startsWith('/conversations/thread/'));
    });

    test('a client notification still opens the client book', () {
      expect(
        notificationRouteFor(_notification({'customerId': 'cus_204'})),
        AppRoutes.customer('cus_204'),
      );
    });

    test('a notification with nowhere to go has no route', () {
      expect(notificationRouteFor(_notification({})), isNull);
      expect(
        notificationRouteFor(_notification({'customerId': '', 'conversationId': ''})),
        isNull,
      );
    });
  });

  group('notificationRouteForIds', () {
    test('is the one rule the tile and a push tap share', () {
      final cases = <Map<String, String?>>[
        {
          'conversationId': '11111111-1111-4111-8111-111111111111',
          'messageId': '22222222-2222-4222-8222-222222222222',
        },
        {'conversationId': '11111111-1111-4111-8111-111111111111'},
        {'conversationId': 'c1', 'customerId': 'cus_204'},
        {'customerId': 'cus_204'},
        <String, String?>{},
      ];

      for (final data in cases) {
        expect(
          notificationRouteFor(_notification(data)),
          notificationRouteForIds(
            conversationId: data['conversationId'],
            messageId: data['messageId'],
            customerId: data['customerId'],
          ),
          reason: 'the tile must not keep a rule of its own',
        );
      }
    });

    test('a conversation without an identified client still opens', () {
      // An inbound thread whose sender is not on file has no customer id; the
      // conversation is the only target that always exists.
      expect(
        notificationRouteForIds(
          conversationId: '11111111-1111-4111-8111-111111111111',
        ),
        '/conversations/thread/11111111-1111-4111-8111-111111111111',
      );
    });

    test('a conversation wins over a client, and empty ids are absent', () {
      expect(
        notificationRouteForIds(conversationId: 'c1', customerId: 'cus_204'),
        startsWith('/conversations/thread/c1'),
      );
      expect(notificationRouteForIds(conversationId: ''), isNull);
      expect(notificationRouteForIds(customerId: ''), isNull);
      expect(notificationRouteForIds(), isNull);
    });
  });
}
