import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_detail.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:flutter_test/flutter_test.dart';

Customer _customer({
  String id = 'CUS-1005',
  String? name = 'Chamari Silva',
  String? nickname,
  double spent = 512000,
  int visits = 14,
  DateTime? lastVisit,
  DateTime? createdAt,
}) {
  return Customer(
    id: id,
    organizationId: 'org-1',
    phoneNumber: '+94 77 641 2098',
    level: CustomerLevel.vip,
    status: CustomerStatus.vip,
    fullName: name,
    nickname: nickname,
    email: 'chamari@example.com',
    totalSpent: spent,
    visitCount: visits,
    lastVisitAtUtc: lastVisit ?? DateTime.utc(2026, 9, 15),
    createdAtUtc: createdAt ?? DateTime.utc(2026, 3, 12),
  );
}

void main() {
  group('Customer profile labels', () {
    test('reads spend in rupees and visits in words', () {
      final customer = _customer();

      expect(customer.totalSpentLabel, 'Rs 512,000');
      expect(customer.visitCountLabel, '14 visits');
    });

    test('counts a single visit in the singular', () {
      expect(_customer(visits: 1).visitCountLabel, '1 visit');
    });

    test('says when a client has never been in', () {
      final customer = Customer(
        id: 'CUS-1',
        organizationId: 'org-1',
        phoneNumber: '+94 77 000 0000',
        level: CustomerLevel.level1,
        status: CustomerStatus.fresh,
      );

      expect(customer.lastVisitLabel, 'Never visited');
      expect(customer.clientSinceLabel, isNull);
    });

    test('dates the last visit and the client', () {
      final customer = _customer(
        lastVisit: DateTime.utc(2026, 9, 15, 12),
        createdAt: DateTime.utc(2026, 3, 12, 12),
      );

      expect(customer.lastVisitLabel, 'Last visit 15 Sep 2026');
      expect(customer.clientSinceLabel, 'Client since 12 Mar 2026');
    });
  });

  group('CustomerStatus.recommend', () {
    // `CustomerLoyaltyService.RecommendStatus`, at a fixed clock: the rule the
    // API runs when it recomputes a tier.
    final now = DateTime.utc(2026, 9, 17);

    test('leaves a new client at the entry tier', () {
      expect(
        CustomerStatus.recommend(
          totalSpent: 3200,
          visitCount: 1,
          lastVisitAtUtc: now.subtract(const Duration(days: 3)),
          now: now,
        ),
        CustomerStatus.fresh,
      );
    });

    test('lifts a client who has spent enough', () {
      expect(
        CustomerStatus.recommend(
          totalSpent: 11000,
          visitCount: 1,
          lastVisitAtUtc: now.subtract(const Duration(days: 3)),
          now: now,
        ),
        CustomerStatus.returning,
      );
    });

    test('lifts a client who keeps coming back', () {
      expect(
        CustomerStatus.recommend(
          totalSpent: 500,
          visitCount: 2,
          lastVisitAtUtc: now.subtract(const Duration(days: 3)),
          now: now,
        ),
        CustomerStatus.returning,
      );
    });

    test('needs both the spend and the visits for VIP', () {
      expect(
        CustomerStatus.recommend(
          totalSpent: 90000,
          visitCount: 2,
          lastVisitAtUtc: now.subtract(const Duration(days: 3)),
          now: now,
        ),
        CustomerStatus.returning,
      );
      expect(
        CustomerStatus.recommend(
          totalSpent: 90000,
          visitCount: 5,
          lastVisitAtUtc: now.subtract(const Duration(days: 3)),
          now: now,
        ),
        CustomerStatus.vip,
      );
    });

    test('dormancy outranks whatever was spent before it', () {
      expect(
        CustomerStatus.recommend(
          totalSpent: 900000,
          visitCount: 20,
          lastVisitAtUtc: now.subtract(const Duration(days: 90)),
          now: now,
        ),
        CustomerStatus.dormant,
      );
    });

    test('a client who has never visited is not dormant', () {
      expect(
        CustomerStatus.recommend(totalSpent: 0, visitCount: 0, now: now),
        CustomerStatus.fresh,
      );
    });
  });

  group('Consent', () {
    test('reads the API wire values', () {
      expect(ConsentStatus.parse('granted'), ConsentStatus.granted);
      expect(ConsentStatus.parse('revoked'), ConsentStatus.revoked);
      expect(ConsentStatus.parse('pending'), ConsentStatus.pending);
      // The profile falls back to this when a client has no consent row.
      expect(ConsentStatus.parse('unknown'), ConsentStatus.unknown);
      expect(ConsentStatus.parse(null), ConsentStatus.unknown);
    });

    test('only granted consent allows the boutique to make contact', () {
      expect(ConsentStatus.granted.allowsContact, isTrue);
      for (final status in [
        ConsentStatus.revoked,
        ConsentStatus.pending,
        ConsentStatus.unknown,
      ]) {
        expect(status.allowsContact, isFalse);
      }
    });

    test('says when consent was given or taken away', () {
      expect(
        CustomerConsent(
          status: ConsentStatus.granted,
          grantedAtUtc: DateTime.utc(2026, 8, 3, 12),
        ).detailLabel,
        'Granted 3 Aug 2026',
      );
      expect(
        CustomerConsent(
          status: ConsentStatus.revoked,
          revokedAtUtc: DateTime.utc(2026, 9, 12, 12),
        ).detailLabel,
        'Revoked 12 Sep 2026',
      );
      expect(
        CustomerConsent.unknown.detailLabel,
        'Never asked',
      );
    });
  });

  group('Memories', () {
    test('read the agent service vocabulary, with a fallback', () {
      expect(MemoryCategory.parse('preference'), MemoryCategory.preference);
      expect(MemoryCategory.parse('complaint'), MemoryCategory.complaint);
      expect(MemorySource.parse('staff_note'), MemorySource.staffNote);
      expect(MemorySource.parse('purchase'), MemorySource.purchase);
      // A vocabulary the API grows past this build still lands somewhere.
      expect(MemoryCategory.parse('nonsense'), MemoryCategory.fact);
      expect(MemorySource.parse('nonsense'), MemorySource.conversation);
    });

    test('say whether the client stated it or Aveline inferred it', () {
      final stated = CustomerMemory(
        id: 'm1',
        content: 'Prefers ivory.',
        category: MemoryCategory.preference,
        source: MemorySource.conversation,
        isExplicit: true,
        confidence: 0.92,
        createdAtUtc: DateTime.utc(2026, 9, 1, 12),
      );
      final inferred = CustomerMemory(
        id: 'm2',
        content: 'Usually shops with her daughter.',
        category: MemoryCategory.fact,
        source: MemorySource.inferred,
        confidence: 0.52,
        createdAtUtc: DateTime.utc(2026, 8, 1, 12),
      );

      expect(stated.originLabel, 'Stated by the client');
      expect(stated.confidenceLabel, '92%');
      expect(stated.createdLabel, '1 Sep 2026');
      expect(inferred.originLabel, 'Inferred');
      expect(inferred.confidenceLabel, '52%');
    });
  });

  group('Occasions', () {
    final now = DateTime.utc(2026, 9, 17, 12);

    CustomerEvent event({
      required String id,
      required DateTime date,
      CustomerEventType type = CustomerEventType.wedding,
      bool isActive = true,
    }) => CustomerEvent(id: id, type: type, dateUtc: date, isActive: isActive);

    test('reads the occasions the agent service detects, with a fallback', () {
      expect(CustomerEventType.parse('wedding'), CustomerEventType.wedding);
      expect(CustomerEventType.parse('birthday'), CustomerEventType.birthday);
      expect(CustomerEventType.parse('anniversary'), CustomerEventType.anniversary);
      expect(CustomerEventType.parse('office'), CustomerEventType.office);
      expect(CustomerEventType.parse('other'), CustomerEventType.other);
      expect(CustomerEventType.parse('nonsense'), CustomerEventType.other);
    });

    test('counts the days to an occasion in words', () {
      final soon = event(
        id: 'e1',
        date: now.add(const Duration(days: 4)),
      );
      final today = event(id: 'e2', date: now);

      expect(soon.countdownLabel(now: now), 'in 4 days');
      expect(soon.dateLabel, '21 Sep 2026');
      expect(today.countdownLabel(now: now), 'Today');
    });

    test('an occasion happening today is still ahead of the floor', () {
      expect(event(id: 'e1', date: now).isUpcoming(now: now), isTrue);
      expect(
        event(id: 'e2', date: now.subtract(const Duration(days: 1))).isUpcoming(
          now: now,
        ),
        isFalse,
      );
    });

    test('only watches the window the reminder job watches', () {
      // `EventReminderService` reminds the floor 30 days ahead.
      expect(
        event(id: 'e1', date: now.add(const Duration(days: 30))).isSoon(now: now),
        isTrue,
      );
      expect(
        event(id: 'e2', date: now.add(const Duration(days: 31))).isSoon(now: now),
        isFalse,
      );
    });

    test('splits the occasions into what is ahead and what has passed', () {
      final detail = CustomerDetail(
        customer: _customer(),
        events: [
          event(id: 'past', date: now.subtract(const Duration(days: 40))),
          event(id: 'far', date: now.add(const Duration(days: 60))),
          event(id: 'near', date: now.add(const Duration(days: 3))),
          event(
            id: 'closed',
            date: now.add(const Duration(days: 10)),
            isActive: false,
          ),
        ],
      );

      expect(
        [for (final e in detail.upcomingEvents(now: now)) e.id],
        ['near', 'far'],
      );
      // Most recent first, which puts the occasion the boutique closed after
      // the one that has simply happened.
      expect(
        [for (final e in detail.pastEvents(now: now)) e.id],
        ['closed', 'past'],
      );
    });
  });

  group('Interactions', () {
    test('read the agent service channels, with a fallback', () {
      expect(InteractionChannel.parse('whatsapp'), InteractionChannel.whatsapp);
      expect(InteractionChannel.parse('in_person'), InteractionChannel.inPerson);
      expect(InteractionChannel.parse('phone'), InteractionChannel.phone);
      expect(InteractionChannel.parse('nonsense'), InteractionChannel.whatsapp);
      expect(InteractionDirection.parse('outbound'), InteractionDirection.outbound);
      expect(InteractionDirection.parse(null), InteractionDirection.inbound);
    });

    test('stamp an exchange with the day and the time', () {
      final interaction = CustomerInteraction(
        id: 'i1',
        channel: InteractionChannel.whatsapp,
        direction: InteractionDirection.inbound,
        messageContent: 'Is the wine raw silk back in stock?',
        createdAtUtc: DateTime.utc(2026, 9, 17, 9),
      );

      expect(interaction.isInbound, isTrue);
      expect(interaction.channelLabel, 'WhatsApp');
      expect(interaction.directionLabel, 'From the client');
      expect(
        interaction.whenLabel(now: DateTime.utc(2026, 9, 17, 12)),
        startsWith('Today · '),
      );
    });
  });

  group('CustomerDetail', () {
    test('knows which parts of a client it holds', () {
      const empty = CustomerDetail(customer: Customer(
        id: 'CUS-1',
        organizationId: 'org-1',
        phoneNumber: '+94 77 000 0000',
        level: CustomerLevel.level1,
        status: CustomerStatus.fresh,
      ));

      expect(empty.hasPreferences, isFalse);
      expect(empty.hasMemories, isFalse);
      expect(empty.hasEvents, isFalse);
      expect(empty.hasInteractions, isFalse);
      expect(empty.consent.status, ConsentStatus.unknown);
    });

    test('separates what the client stated from what was inferred', () {
      final detail = CustomerDetail(
        customer: _customer(),
        memories: [
          CustomerMemory(
            id: 'm1',
            content: 'Stated.',
            category: MemoryCategory.preference,
            source: MemorySource.conversation,
            isExplicit: true,
            createdAtUtc: DateTime.utc(2026, 9, 1),
          ),
          CustomerMemory(
            id: 'm2',
            content: 'Inferred.',
            category: MemoryCategory.fact,
            source: MemorySource.inferred,
            createdAtUtc: DateTime.utc(2026, 9, 2),
          ),
        ],
      );

      expect([for (final m in detail.statedMemories) m.id], ['m1']);
    });
  });
}
