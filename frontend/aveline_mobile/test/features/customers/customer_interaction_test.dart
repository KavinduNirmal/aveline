import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('CustomerInteraction', () {
    final now = DateTime.utc(2026, 9, 23, 14, 30);

    test('formats purchase total nicely', () {
      final interaction = CustomerInteraction(
        id: 'int-1',
        channel: InteractionChannel.inPerson,
        direction: InteractionDirection.inbound,
        createdAtUtc: now,
        purchaseTotal: 48000,
        staffMemberName: 'Dilud',
        tags: const ['Silk', 'Wedding'],
      );

      expect(interaction.purchaseTotalLabel, 'LKR 48,000');
      expect(interaction.isInbound, isTrue);
      expect(interaction.channelLabel, 'In person');
      expect(interaction.directionLabel, 'From the client');
    });

    test('returns null for zero or negative purchase total', () {
      final interaction = CustomerInteraction(
        id: 'int-2',
        channel: InteractionChannel.whatsapp,
        direction: InteractionDirection.outbound,
        createdAtUtc: now,
        purchaseTotal: 0,
      );

      expect(interaction.purchaseTotalLabel, isNull);
      expect(interaction.isInbound, isFalse);
    });
  });

  group('CustomerInteractionQuery', () {
    final now = DateTime.utc(2026, 9, 23, 14, 30);
    final i1 = CustomerInteraction(
      id: 'int-1',
      channel: InteractionChannel.inPerson,
      direction: InteractionDirection.inbound,
      messageContent: 'Tried Crimson Dahlia Saree',
      staffMemberName: 'Dilud',
      tags: const ['Saree', 'Crimson'],
      createdAtUtc: now,
    );
    final i2 = CustomerInteraction(
      id: 'int-2',
      channel: InteractionChannel.whatsapp,
      direction: InteractionDirection.outbound,
      messageContent: 'Shared Lookbook link',
      staffMemberName: 'Salon Concierge',
      tags: const ['Lookbook'],
      createdAtUtc: now,
    );

    test('empty query matches all interactions', () {
      const query = CustomerInteractionQuery();
      expect(query.isEmpty, isTrue);
      expect(query.matches(i1), isTrue);
      expect(query.matches(i2), isTrue);
    });

    test('filters by channel', () {
      const query = CustomerInteractionQuery(channel: InteractionChannel.whatsapp);
      expect(query.matches(i1), isFalse);
      expect(query.matches(i2), isTrue);
    });

    test('filters by direction', () {
      const query = CustomerInteractionQuery(direction: InteractionDirection.inbound);
      expect(query.matches(i1), isTrue);
      expect(query.matches(i2), isFalse);
    });

    test('filters by search keyword matching message, staff or tags', () {
      const q1 = CustomerInteractionQuery(search: 'Crimson');
      expect(q1.matches(i1), isTrue);
      expect(q1.matches(i2), isFalse);

      const q2 = CustomerInteractionQuery(search: 'Concierge');
      expect(q2.matches(i1), isFalse);
      expect(q2.matches(i2), isTrue);

      const q3 = CustomerInteractionQuery(search: 'Saree');
      expect(q3.matches(i1), isTrue);
      expect(q3.matches(i2), isFalse);
    });
  });
}
