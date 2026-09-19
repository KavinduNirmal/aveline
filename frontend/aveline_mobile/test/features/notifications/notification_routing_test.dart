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
}
