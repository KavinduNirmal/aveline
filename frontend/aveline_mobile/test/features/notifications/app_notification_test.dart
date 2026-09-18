import 'package:aveline_mobile/features/notifications/domain/app_notification.dart';
import 'package:aveline_mobile/features/notifications/domain/notification_kind.dart';
import 'package:aveline_mobile/features/notifications/domain/notification_page.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AppNotification.fromJson', () {
    test('reads the inbox item the API documents', () {
      final item = AppNotification.fromJson(const {
        'id': 'un_1',
        'notificationId': 'nr_1',
        'type': 'PaymentConfirmed',
        'title': 'Payment received',
        'body': 'Order #4816 was paid in full.',
        'data': {'orderId': '4816', 'amount': '84500'},
        'isRead': true,
        'readAt': '2026-09-17T09:15:00Z',
        'deliveredAt': '2026-09-17T09:14:02Z',
        'createdAt': '2026-09-17T09:14:00Z',
      });

      expect(item.id, 'un_1');
      expect(item.notificationId, 'nr_1');
      expect(item.type, 'PaymentConfirmed');
      expect(item.kind, NotificationKind.paymentConfirmed);
      expect(item.title, 'Payment received');
      expect(item.body, 'Order #4816 was paid in full.');
      expect(item.data, {'orderId': '4816', 'amount': '84500'});
      expect(item.isRead, isTrue);
      expect(item.readAt, DateTime.utc(2026, 9, 17, 9, 15));
      expect(item.deliveredAt, DateTime.utc(2026, 9, 17, 9, 14, 2));
      expect(item.createdAt, DateTime.utc(2026, 9, 17, 9, 14));
    });

    test('tolerates a payload with nothing but the title', () {
      final item = AppNotification.fromJson(const {'title': 'Hello'});

      expect(item.id, '');
      expect(item.type, '');
      expect(item.kind, NotificationKind.unknown);
      expect(item.title, 'Hello');
      expect(item.body, '');
      expect(item.data, isEmpty);
      expect(item.isRead, isFalse);
      expect(item.readAt, isNull);
      expect(item.deliveredAt, isNull);
    });

    test('drops a data block that is not a map of scalars', () {
      final item = AppNotification.fromJson(const {
        'title': 'Hello',
        'data': ['not', 'a', 'map'],
      });

      expect(item.data, isEmpty);
    });

    test('stringifies a data value the API sent as a number', () {
      final item = AppNotification.fromJson(const {
        'title': 'Hello',
        'data': {'count': 3},
      });

      expect(item.data['count'], '3');
    });

    test('keeps null data values null rather than inventing an empty string', () {
      final item = AppNotification.fromJson(const {
        'title': 'Hello',
        'data': {'orderId': null},
      });

      expect(item.data.containsKey('orderId'), isTrue);
      expect(item.data['orderId'], isNull);
    });

    test('an unreadable timestamp reads as absent rather than crashing', () {
      final item = AppNotification.fromJson(const {
        'title': 'Hello',
        'createdAt': 'not a date',
      });

      expect(item.createdAt, isNotNull);
    });
  });

  group('AppNotification.copyWith', () {
    final original = AppNotification(
      id: 'un_1',
      notificationId: 'nr_1',
      type: 'NewMessage',
      title: 'Nadeesha replied',
      body: 'Can the wine saree be altered?',
      createdAt: DateTime.utc(2026, 9, 17, 9),
    );

    test('marks read without touching anything else', () {
      final read = original.copyWith(
        isRead: true,
        readAt: DateTime.utc(2026, 9, 17, 10),
      );

      expect(read.isRead, isTrue);
      expect(read.readAt, DateTime.utc(2026, 9, 17, 10));
      expect(read.id, original.id);
      expect(read.title, original.title);
      expect(read.body, original.body);
      expect(read.createdAt, original.createdAt);
      expect(read.kind, original.kind);
    });

    test('leaves an already-read item unread when asked for nothing', () {
      final untouched = original.copyWith();

      expect(untouched.isRead, isFalse);
      expect(untouched.readAt, isNull);
    });
  });

  group('NotificationPage.fromJson', () {
    test('reads the paged envelope the API documents', () {
      final page = NotificationPage.fromJson(const {
        'total': 45,
        'page': 2,
        'pageSize': 20,
        'items': [
          {'id': 'un_21', 'title': 'One'},
          {'id': 'un_22', 'title': 'Two'},
        ],
      });

      expect(page.total, 45);
      expect(page.page, 2);
      expect(page.pageSize, 20);
      expect(page.items.map((item) => item.id), ['un_21', 'un_22']);
      expect(page.hasMore, isTrue);
    });

    test('reports the end of the inbox when every item is on the page', () {
      final page = NotificationPage.fromJson(const {
        'total': 2,
        'page': 1,
        'pageSize': 20,
        'items': [
          {'id': 'un_1', 'title': 'One'},
          {'id': 'un_2', 'title': 'Two'},
        ],
      });

      expect(page.hasMore, isFalse);
    });

    test('reads an empty envelope without inventing items', () {
      final page = NotificationPage.fromJson(const {});

      expect(page.items, isEmpty);
      expect(page.total, 0);
      expect(page.page, 1);
      expect(page.hasMore, isFalse);
    });

    test('takes the total from the envelope, not from what arrived', () {
      // A page the server trimmed is still part of a longer inbox.
      final page = NotificationPage.fromJson(const {
        'total': 9,
        'page': 1,
        'pageSize': 4,
        'items': [
          {'id': 'un_1', 'title': 'One'},
          {'id': 'un_2', 'title': 'Two'},
          {'id': 'un_3', 'title': 'Three'},
          {'id': 'un_4', 'title': 'Four'},
        ],
      });

      expect(page.items, hasLength(4));
      expect(page.hasMore, isTrue);
    });
  });
}
