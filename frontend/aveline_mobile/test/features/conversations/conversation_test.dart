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

    test('a thread bound to a client is a client thread, whatever kind says', () {
      // D1: the context axis is read first. The server stamps `Salon` on every
      // creation path, so a customer-bound thread must be recognised by its
      // context rather than by its kind.
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_2',
        'kind': 'Salon',
        'customerId': 'cus_9',
        'customerName': 'Nadeesha Perera',
        'lastMessagePreview': 'Can the wine saree be taken in?',
        'lastMessageAuthor': 'Customer',
      });

      expect(conversation.kind, ConversationKind.customer);
      expect(conversation.isAveline, isFalse);
      expect(conversation.customerName, 'Nadeesha Perera');
      expect(conversation.lastMessagePreview, 'Can the wine saree be taken in?');
      expect(conversation.lastMessageAuthor, ConversationAuthor.customer);
    });

    test('a channel thread whose customer is not identified is a client thread', () {
      // The phone is the channel reference: the thread exists for a client, only
      // the client's name is missing. It must not be pinned as the concierge.
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_channel',
        'kind': 'Salon',
        'customerId': null,
        'externalRef': '94771234567',
      });

      expect(conversation.kind, ConversationKind.customer);
      expect(conversation.isAveline, isFalse);
      expect(conversation.externalRef, '94771234567');
    });

    test('the general Salon with no context is Aveline', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_3',
        'kind': 'Salon',
        'customerId': null,
      });

      expect(conversation.kind, ConversationKind.aveline);
      expect(conversation.isAveline, isTrue);
    });

    test('a thread about nobody in particular is an announcement', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_4',
        'kind': 'Digest',
        'status': 'Resolved',
      });

      expect(conversation.kind, ConversationKind.system);
      expect(conversation.customerId, isNull);
    });

    test('an empty customer id does not make an announcement a client thread', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_5',
        'kind': 'Announcement',
        'customerId': '',
      });

      expect(conversation.kind, ConversationKind.system);
    });

    test('only one thread classifies as aveline', () {
      // The invariant the pinned slot rests on: a general Salon plus any number
      // of bound or channel threads yields exactly one `aveline`.
      final conversations = [
        Conversation.fromJson(const {'id': 'salon', 'kind': 'Salon'}),
        Conversation.fromJson(const {
          'id': 'bound',
          'kind': 'Salon',
          'customerId': 'cus_9',
        }),
        Conversation.fromJson(const {
          'id': 'channel',
          'kind': 'Salon',
          'externalRef': '94771234567',
        }),
        Conversation.fromJson(const {'id': 'digest', 'kind': 'Digest'}),
      ];

      expect(conversations.where((item) => item.isAveline), hasLength(1));
      expect(
        conversations.singleWhere((item) => item.isAveline).id,
        'salon',
      );
    });

    test('reads the row fields the list endpoint carries', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_6',
        'externalRef': '94771234567',
        'lastMessageBlock': 'suggestion',
        'lastMessageKind': 'Note',
        'lastMessageAgentKey': 'ava',
      });

      expect(conversation.externalRef, '94771234567');
      expect(conversation.lastMessageBlock, 'suggestion');
      expect(conversation.lastMessageKind, 'Note');
      expect(conversation.lastMessageAgentKey, 'ava');
    });

    test('the row fields are null when absent or empty', () {
      final absent = Conversation.fromJson(const {'id': 'cnv_7'});
      expect(absent.externalRef, isNull);
      expect(absent.lastMessageBlock, isNull);
      expect(absent.lastMessageKind, isNull);
      expect(absent.lastMessageAgentKey, isNull);

      final empty = Conversation.fromJson(const {
        'id': 'cnv_7b',
        'externalRef': '',
        'lastMessageBlock': '',
        'lastMessageKind': '',
        'lastMessageAgentKey': '',
      });
      expect(empty.externalRef, isNull);
      expect(empty.lastMessageBlock, isNull);
      expect(empty.lastMessageKind, isNull);
      expect(empty.lastMessageAgentKey, isNull);
    });

    test('markers parse in the priority order the server sorted them', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_8',
        'markers': ['choice', 'draft'],
      });

      expect(conversation.markers, [
        ConversationMarker.choice,
        ConversationMarker.draft,
      ]);
    });

    test('a marker outside the vocabulary is ignored, not fatal', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_9',
        'markers': ['approval', 'escalated', 'draft'],
      });

      expect(conversation.markers, [
        ConversationMarker.approval,
        ConversationMarker.draft,
      ]);
    });

    test('an absent marker set is empty', () {
      final conversation = Conversation.fromJson(const {'id': 'cnv_10'});

      expect(conversation.markers, isEmpty);
    });

    test('tolerates a payload with nothing but an id', () {
      final conversation = Conversation.fromJson(const {'id': 'cnv_5'});

      expect(conversation.id, 'cnv_5');
      expect(conversation.kind, ConversationKind.system);
      expect(conversation.status, ConversationStatus.unknown);
      expect(conversation.lastMessageAt, isNull);
      expect(conversation.lastMessagePreview, isNull);
      expect(conversation.lastMessageAuthor, isNull);
      expect(conversation.markers, isEmpty);
    });

    test('a status it has never met does not throw', () {
      final conversation = Conversation.fromJson(const {
        'id': 'cnv_6',
        'status': 'Escalated',
      });

      expect(conversation.status, ConversationStatus.unknown);
    });
  });

  group('ConversationMarker', () {
    test('names each marker the way the row prints it', () {
      expect(ConversationMarker.approval.label, 'Approval');
      expect(ConversationMarker.choice.label, 'Pick the client');
      expect(ConversationMarker.draft.label, 'Draft ready to copy');
    });

    test('does not know a marker outside the vocabulary', () {
      expect(ConversationMarker.fromJson('escalated'), isNull);
      expect(ConversationMarker.fromJson(null), isNull);
      expect(ConversationMarker.fromJson(7), isNull);
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
      // A channel thread whose customer is not identified has no name yet, so the
      // row must have something to print rather than an empty line.
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
      expect(
        Conversation.fromJson(const {'id': 'a', 'kind': 'Salon'}).isAveline,
        isTrue,
      );
    });
  });
}
