import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Conversation.fromJson', () {
    test('reads the conversation the API documents today', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_1',
        'kind': 'Salon',
        'customerId': null,
        'threadId': 'thread_1',
        'status': 'Active',
        'lastMessageAt': '2026-09-18T09:14:00Z',
      });

      expect(conversation.id, 'cnv_1');
      expect(conversation.kind, ConversationKind.aveline);
      expect(conversation.customerId, isNull);
      expect(conversation.threadId, 'thread_1');
      expect(conversation.status, ConversationStatus.active);
      expect(conversation.lastMessageAt, DateTime.utc(2026, 9, 18, 9, 14));
    });

    test('a thread that names a client is a client thread', () {
      // The backend only emits `Salon` today, so a client thread is recognised by
      // the client it is bound to. That rule keeps working when the kind is added.
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_2',
        'kind': 'Direct',
        'customerId': 'cus_9',
        'customerName': 'Nadeesha Perera',
        'lastMessagePreview': 'Can the wine saree be taken in?',
        'lastMessageAuthor': 'Customer',
        'unreadCount': 2,
      });

      expect(conversation.kind, ConversationKind.customer);
      expect(conversation.customerName, 'Nadeesha Perera');
      expect(conversation.lastMessagePreview, 'Can the wine saree be taken in?');
      expect(conversation.lastMessageAuthor, ConversationAuthor.customer);
      expect(conversation.unreadCount, 2);
      expect(conversation.isUnread, isTrue);
    });

    test('a thread about nobody in particular is an announcement', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_3',
        'kind': 'Digest',
        'status': 'Resolved',
      });

      expect(conversation.kind, ConversationKind.system);
      expect(conversation.customerId, isNull);
    });

    test('an empty customer id does not make an announcement a client thread', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_4',
        'kind': 'Announcement',
        'customerId': '',
      });

      expect(conversation.kind, ConversationKind.system);
    });

    test('tolerates a payload with nothing but an id', () {
      final conversation = Conversation.fromJson(const {'id': 'cnv_5'});

      expect(conversation.id, 'cnv_5');
      expect(conversation.kind, ConversationKind.system);
      expect(conversation.status, ConversationStatus.unknown);
      expect(conversation.lastMessageAt, isNull);
      expect(conversation.lastMessagePreview, isNull);
      expect(conversation.lastMessageAuthor, isNull);
      expect(conversation.unreadCount, 0);
      expect(conversation.isUnread, isFalse);
    });

    test('a status it has never met does not throw', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_6',
        'status': 'Escalated',
      });

      expect(conversation.status, ConversationStatus.unknown);
    });

    test('a negative unread count reads as none', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_7',
        'unreadCount': -3,
      });

      expect(conversation.unreadCount, 0);
      expect(conversation.isUnread, isFalse);
    });
  });

  group('Conversation.title', () {
    test('the Salon is Aveline', () {
      const conversation = Conversation(
        id: 'cnv_1',
        kind: ConversationKind.aveline,
      );

      expect(conversation.title, 'Aveline');
    });

    test('a client thread wears the client', () {
      const conversation = Conversation(
        id: 'cnv_2',
        kind: ConversationKind.customer,
        customerId: 'cus_9',
        customerName: 'Nadeesha Perera',
      );

      expect(conversation.title, 'Nadeesha Perera');
    });

    test('a client thread the name never arrived for still has a title', () {
      // The conversation list carries no client name yet, so the row must have
      // something to print rather than an empty line.
      const conversation = Conversation(
        id: 'cnv_3',
        kind: ConversationKind.customer,
        customerId: 'cus_9',
      );

      expect(conversation.title, 'Client');
    });
  });

  group('ConversationKind', () {
    test('names the thread the app pins', () {
      expect(ConversationKind.aveline, isNotNull);
      expect(Conversation.fromJson(const {'id': 'a', 'kind': 'Salon'}).isAveline, isTrue);
    });
  });
}
