import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:flutter_test/flutter_test.dart';

/// The realtime payload gained `notificationId` and `unreadCount` additively.
///
/// An older server and an older client can meet, so a payload without either key
/// must parse and behave exactly as it did before: the client falls back to a
/// full inbox refresh.
void main() {
  group('NotificationPayload.fromJson', () {
    test('carries the identity and count when the server sends them', () {
      final payload = NotificationPayload.fromJson({
        'type': 'NewMessage',
        'title': 'Hi',
        'body': 'A customer messaged',
        'data': {'conversationId': 'cnv-1'},
        'notificationId': 'un-42',
        'unreadCount': 3,
      });

      expect(payload.type, 'NewMessage');
      expect(payload.notificationId, 'un-42');
      expect(payload.unreadCount, 3);
      expect(payload.data['conversationId'], 'cnv-1');
    });

    test('defaults both new fields when an older server omits them', () {
      final payload = NotificationPayload.fromJson({
        'type': 'EventReminder',
        'title': 'Fitting tomorrow',
        'body': 'Chathurika at 10:30',
        'data': {'customerId': 'cus-1'},
      });

      expect(payload.notificationId, isNull);
      expect(payload.unreadCount, isNull);
    });

    test('tolerates a null unreadCount without inventing a number', () {
      final payload = NotificationPayload.fromJson({
        'type': 'SystemAlert',
        'title': 'Alert',
        'body': 'Detail',
        'unreadCount': null,
      });

      expect(payload.unreadCount, isNull);
    });

    test('tolerates a missing data map', () {
      final payload = NotificationPayload.fromJson({
        'type': 'SystemAlert',
        'title': 'Alert',
        'body': 'Detail',
      });

      expect(payload.data, isEmpty);
    });
  });
}
