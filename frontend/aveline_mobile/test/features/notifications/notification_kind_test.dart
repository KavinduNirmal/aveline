import 'package:aveline_mobile/features/notifications/domain/notification_kind.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('NotificationKind.fromType', () {
    test('maps every type the gateway dispatches today', () {
      expect(
        NotificationKind.fromType('NewMessage'),
        NotificationKind.newMessage,
      );
      expect(
        NotificationKind.fromType('ApprovalNeeded'),
        NotificationKind.approvalNeeded,
      );
      expect(
        NotificationKind.fromType('PaymentConfirmed'),
        NotificationKind.paymentConfirmed,
      );
      expect(
        NotificationKind.fromType('VipAtRisk'),
        NotificationKind.vipAtRisk,
      );
      expect(
        NotificationKind.fromType('EventReminder'),
        NotificationKind.eventReminder,
      );
      expect(NotificationKind.fromType('NewMatch'), NotificationKind.newMatch);
    });

    test('parses case-insensitively, because the type is a server string', () {
      expect(
        NotificationKind.fromType('paymentconfirmed'),
        NotificationKind.paymentConfirmed,
      );
      expect(
        NotificationKind.fromType('  VIPATRISK  '),
        NotificationKind.vipAtRisk,
      );
    });

    test('falls back to unknown rather than throwing on a type it has not met', () {
      // A newer backend can dispatch a kind this build has never heard of, and
      // an inbox that refuses to open is worse than one tile without a label.
      expect(
        NotificationKind.fromType('SomethingBrandNew'),
        NotificationKind.unknown,
      );
      expect(NotificationKind.fromType(''), NotificationKind.unknown);
    });
  });

  group('NotificationKind', () {
    test('every kind carries the words the tile shows', () {
      for (final kind in NotificationKind.values) {
        expect(kind.label, isNotEmpty, reason: '$kind has no label');
      }
    });

    test('unknown wears a neutral label rather than an empty one', () {
      expect(NotificationKind.unknown.label, 'Notification');
    });

    test('the API value round-trips through fromType', () {
      for (final kind in NotificationKind.values) {
        if (kind == NotificationKind.unknown) {
          continue;
        }
        expect(NotificationKind.fromType(kind.apiValue), kind);
      }
    });
  });
}
