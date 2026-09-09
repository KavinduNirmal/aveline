import 'package:aveline_mobile/features/salon/domain/salon_message.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('SalonMessage', () {
    test('parses a wire MessageDto into a SalonMessage', () {
      final message = SalonMessage.fromJson({
        'id': 'm1',
        'authorKind': 'Agent',
        'agentKey': 'aveline',
        'contentBlocks': [
          {'type': 'text', 'text': 'Hello there'},
        ],
        'createdAt': '2026-09-09T10:00:00Z',
      });

      expect(message.id, 'm1');
      expect(message.authorKind, 'Agent');
      expect(message.agentKey, 'aveline');
      expect(message.text, 'Hello there');
      expect(message.isOwn, isFalse);
    });

    test('parses a user message as own', () {
      final message = SalonMessage.fromJson({
        'id': 'm2',
        'authorKind': 'User',
        'contentBlocks': [
          {'type': 'text', 'text': 'Hi'},
        ],
        'createdAt': '2026-09-09T10:00:00Z',
      });

      expect(message.isOwn, isTrue);
    });

    test('delivery status flags sending and failed', () {
      final sending = SalonMessage(
        id: 'l1',
        authorKind: 'User',
        text: 'Hi',
        createdAt: DateTime.now(),
        deliveryStatus: MessageDeliveryStatus.sending,
      );
      expect(sending.isSending, isTrue);
      expect(sending.isFailed, isFalse);

      final failed = sending.copyWith(
        deliveryStatus: MessageDeliveryStatus.failed,
      );
      expect(failed.isSending, isFalse);
      expect(failed.isFailed, isTrue);
    });

    test('copyWith preserves thoughtSeconds and clears delivery status', () {
      final base = SalonMessage(
        id: 'l1',
        authorKind: 'User',
        text: 'Hi',
        createdAt: DateTime.now(),
        deliveryStatus: MessageDeliveryStatus.sending,
      );
      final confirmed = base.copyWith(deliveryStatus: null);
      expect(confirmed.deliveryStatus, isNull);
      expect(confirmed.text, 'Hi');
    });
  });
}
