import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/notification_provider.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('NotificationProvider', () {
    test('starts with no latest notification', () {
      final provider = NotificationProvider();
      expect(provider.latest, isNull);
    });

    test('push stores the payload and notifies listeners', () {
      final provider = NotificationProvider();
      var notified = 0;
      provider.addListener(() => notified++);

      final payload = NotificationPayload(
        type: 'PaymentConfirmed',
        title: 'Paid',
        body: 'Order paid',
        data: const {'orderId': 'ord-1'},
      );
      provider.push(payload);

      expect(provider.latest, same(payload));
      expect(notified, 1);
    });

    test('clear removes the latest and notifies once', () {
      final provider = NotificationProvider();
      provider.push(const NotificationPayload(type: 'x', title: 't', body: 'b'));
      var notified = 0;
      provider.addListener(() => notified++);

      provider.clear();

      expect(provider.latest, isNull);
      expect(notified, 1);
    });

    test('clear when already empty does not notify', () {
      final provider = NotificationProvider();
      var notified = 0;
      provider.addListener(() => notified++);

      provider.clear();

      expect(notified, 0);
    });
  });

  group('NotificationProvider unread count', () {
    test('starts with no count, so the badge knows to fall back to a dot', () {
      final provider = NotificationProvider();

      expect(provider.unreadCount, 0);
      expect(provider.hasUnreadCount, isFalse);
    });

    test('reports the count the inbox handed it, and notifies', () {
      final provider = NotificationProvider();
      var notified = 0;
      provider.addListener(() => notified++);

      provider.setUnreadCount(3);

      expect(provider.unreadCount, 3);
      expect(provider.hasUnreadCount, isTrue);
      expect(notified, 1);
    });

    test('the same count twice is not news, so it does not notify again', () {
      final provider = NotificationProvider();
      provider.setUnreadCount(3);
      var notified = 0;
      provider.addListener(() => notified++);

      provider.setUnreadCount(3);

      expect(notified, 0);
    });

    test('a count that moves to zero is still worth notifying', () {
      // This is the mark-all-read: the dot has to go, and that is a change.
      final provider = NotificationProvider();
      provider.setUnreadCount(3);
      var notified = 0;
      provider.addListener(() => notified++);

      provider.setUnreadCount(0);

      expect(provider.unreadCount, 0);
      expect(notified, 1);
    });

    test('a negative count reads as nothing unread rather than as a negative', () {
      final provider = NotificationProvider();

      provider.setUnreadCount(-4);

      expect(provider.unreadCount, 0);
    });

    test('clear forgets the count, so the next session waits for its own', () {
      final provider = NotificationProvider();
      provider.setUnreadCount(3);

      provider.clear();

      expect(provider.unreadCount, 0);
      expect(provider.hasUnreadCount, isFalse);
    });

    test('clear still notifies when only the count was known', () {
      final provider = NotificationProvider();
      provider.setUnreadCount(3);
      var notified = 0;
      provider.addListener(() => notified++);

      provider.clear();

      expect(notified, 1);
    });
  });

  group('NotificationPayload.fromJson', () {
    test('parses type, title, body and data', () {
      final payload = NotificationPayload.fromJson({
        'type': 'NewMessage',
        'title': 'New message',
        'body': 'A customer messaged',
        'data': {'customerId': 'c-1'},
      });

      expect(payload.type, 'NewMessage');
      expect(payload.title, 'New message');
      expect(payload.body, 'A customer messaged');
      expect(payload.data['customerId'], 'c-1');
    });

    test('defaults missing fields', () {
      final payload = NotificationPayload.fromJson(const {});
      expect(payload.type, '');
      expect(payload.title, '');
      expect(payload.body, '');
      expect(payload.data, isEmpty);
    });
  });
}
