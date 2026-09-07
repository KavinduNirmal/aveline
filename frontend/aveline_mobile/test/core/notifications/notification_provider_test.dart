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
