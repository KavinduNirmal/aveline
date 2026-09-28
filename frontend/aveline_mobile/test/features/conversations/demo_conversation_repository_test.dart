import 'package:aveline_mobile/features/conversations/data/demo_conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:flutter_test/flutter_test.dart';

/// The clock the demo inbox is measured from, so "8 minutes ago" means the same
/// thing on every run.
final DateTime _now = DateTime.utc(2026, 9, 18, 12);

DemoConversationRepository _inbox() =>
    DemoConversationRepository(clock: () => _now, latency: Duration.zero);

/// The fixture's threads, without the envelope around them.
Future<List<Conversation>> _items() async =>
    (await _inbox().fetchConversations()).items;

void main() {
  group('DemoConversationRepository.fetchConversations', () {
    test('holds the Salon, so the pinned thread is always there', () async {
      final conversations = await _items();

      final aveline = conversations.where((item) => item.isAveline).toList();
      expect(aveline, hasLength(1));
      expect(aveline.single.title, 'Aveline');
    });

    test('holds several client threads', () async {
      final conversations = await _items();

      final clients = conversations
          .where((item) => item.kind == ConversationKind.customer)
          .toList();
      expect(clients.length, greaterThan(2));
      expect(clients.every((item) => item.customerId != null), isTrue);
      expect(clients.every((item) => item.customerName != null), isTrue);
    });

    test('gives every thread a preview and a time in the past', () async {
      final conversations = await _items();

      for (final conversation in conversations) {
        expect(
          conversation.lastMessagePreview,
          isNotNull,
          reason: '${conversation.title} has no preview',
        );
        expect(conversation.lastMessagePreview, isNotEmpty);
        expect(
          conversation.lastMessageAt,
          isNotNull,
          reason: '${conversation.title} has no time',
        );
        expect(conversation.lastMessageAt!.isAfter(_now), isFalse);
      }
    });

    test('marks who spoke last, so the row can say who it was', () async {
      final conversations = await _items();

      expect(
        conversations.every((item) => item.lastMessageAuthor != null),
        isTrue,
      );
      expect(
        conversations
            .where((item) => item.lastMessageAuthor == ConversationAuthor.staff),
        isNotEmpty,
      );
    });

    test('carries a thread that wants a decision, and one that is settled', () async {
      final conversations = await _items();

      expect(
        conversations.where(
          (item) => item.status == ConversationStatus.awaitingSignOff,
        ),
        isNotEmpty,
      );
      expect(
        conversations.where((item) => item.status == ConversationStatus.active),
        isNotEmpty,
      );
    });

    test('serves its fixture as one complete page', () async {
      final page = await _inbox().fetchConversations();

      expect(page.page, 1);
      expect(page.total, page.items.length);
      expect(page.items.length, greaterThan(2));
    });

    test('hands back a list a caller cannot mutate under the next one', () async {
      final inbox = _inbox();

      final first = (await inbox.fetchConversations()).items;
      expect(
        () => first.add(
          const Conversation(id: 'x', kind: ConversationKind.customer),
        ),
        throwsUnsupportedError,
      );

      final second = (await inbox.fetchConversations()).items;
      expect(second.map((item) => item.id), hasLength(first.length));
    });

    test('measures its ages from the injected clock', () async {
      final conversations = await _items();

      expect(
        conversations.every(
          (item) => item.lastMessageAt!.isBefore(_now) || item.lastMessageAt == null,
        ),
        isTrue,
      );
      // The test's clock is a fixed 2026-09-18T12:00Z, not the wall clock.
      final newest = conversations
          .map((item) => item.lastMessageAt!)
          .reduce((a, b) => a.isAfter(b) ? a : b);
      expect(_now.difference(newest).inDays, lessThan(1));
    });
  });
}
