import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('ThreadMessage.fromJson', () {
    test('reads a staff note', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_1',
        'authorKind': 'User',
        'kind': 'Note',
        'status': 'Published',
        'contentBlocks': [
          {'type': 'text', 'text': 'I have put it aside for her.'},
        ],
        'createdAt': '2026-09-18T09:14:00Z',
      });

      expect(message.id, 'msg_1');
      expect(message.author, MessageAuthor.staff);
      expect(message.kind, MessageKind.note);
      expect(message.status, MessageStatus.published);
      expect(message.text, 'I have put it aside for her.');
      expect(message.createdAt, DateTime.utc(2026, 9, 18, 9, 14));
    });

    test('draws a forwarded client message as the client\u2019s own', () {
      // The backend forwards inbound WhatsApp content authored by `System`,
      // because a customer is external and never a sender on the wire. The
      // content is still the client's, and the thread has to draw it as theirs.
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_2',
        'authorKind': 'System',
        'kind': 'ClientMessage',
        'status': 'Published',
        'contentBlocks': [
          {'type': 'text', 'text': 'Can the wine saree be taken in?'},
        ],
      });

      expect(message.author, MessageAuthor.client);
      expect(message.isFromClient, isTrue);
      expect(message.isFromBoutique, isFalse);
      expect(message.isInternalNote, isFalse);
    });

    test('reads an agent message', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_3',
        'authorKind': 'Agent',
        'agentKey': 'ava',
        'kind': 'Suggestion',
        'status': 'Published',
        'contentBlocks': [
          {'type': 'text', 'text': 'She last bought evening wear.'},
        ],
      });

      expect(message.author, MessageAuthor.agent);
      expect(message.agentKey, 'ava');
      expect(message.isFromBoutique, isTrue);
    });

    test('keeps the hash a sign-off decision is bound to', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_4',
        'authorKind': 'Agent',
        'kind': 'Note',
        'status': 'AwaitingSignOff',
        'contentHash': 'abc123',
        'contentBlocks': [
          {'type': 'text', 'text': 'Shall I confirm the fitting?'},
        ],
      });

      expect(message.contentHash, 'abc123');
      expect(message.needsSignOff, isTrue);
    });

    test('tolerates a payload with nothing but an id', () {
      final message = ThreadMessage.fromJson(const {'id': 'msg_5'});

      expect(message.id, 'msg_5');
      expect(message.author, MessageAuthor.system);
      expect(message.kind, MessageKind.unknown);
      expect(message.status, MessageStatus.unknown);
      expect(message.text, '');
      expect(message.contentHash, isNull);
      expect(message.agentKey, isNull);
    });

    test('a message with no text block reads as empty rather than throwing', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_6',
        'kind': 'Look',
        'contentBlocks': [
          {'type': 'image', 'url': 'https://example.test/a.png'},
        ],
      });

      expect(message.text, '');
      expect(message.kind, MessageKind.look);
    });

    test('a kind and a status it has never met do not throw', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_7',
        'kind': 'Carrier',
        'status': 'Escalated',
      });

      expect(message.kind, MessageKind.unknown);
      expect(message.status, MessageStatus.unknown);
    });
  });

  group('ThreadMessage classification', () {
    ThreadMessage of({
      MessageAuthor author = MessageAuthor.staff,
      MessageKind kind = MessageKind.note,
      MessageStatus status = MessageStatus.sent,
      MessageDeliveryStatus? deliveryStatus,
    }) => ThreadMessage(
      id: 'msg',
      author: author,
      kind: kind,
      status: status,
      text: 'hello',
      createdAt: DateTime.utc(2026, 9, 18),
      deliveryStatus: deliveryStatus,
    );

    test('a published staff note is an internal note the client never saw', () {
      expect(
        of(author: MessageAuthor.staff, status: MessageStatus.published)
            .isInternalNote,
        isTrue,
      );
      expect(
        of(author: MessageAuthor.staff, status: MessageStatus.sent)
            .isInternalNote,
        isFalse,
      );
    });
    test('an agent reply is drawn as a reply, not as a note', () {
      // The side is who spoke and the treatment is where it went. Reading
      // `!isFromClient` as the side test put every persona on the associate's own
      // side and read every published reply as `NOTE · NOT SENT`.
      final reply = of(
        author: MessageAuthor.agent,
        status: MessageStatus.published,
      );

      expect(reply.isInternalNote, isFalse);
      expect(reply.isFromStaff, isFalse);
      expect(reply.isFromAgent, isTrue);
    });

    test('the side test separates the associate from a persona', () {
      // The wire has no `Client` author, so `!isFromClient` is true of staff,
      // agents and system alike and cannot decide which side a bubble sits on.
      expect(of(author: MessageAuthor.staff).isFromStaff, isTrue);
      expect(of(author: MessageAuthor.agent).isFromStaff, isFalse);
      expect(of(author: MessageAuthor.client).isFromStaff, isFalse);
      expect(of(author: MessageAuthor.system).isFromStaff, isFalse);
    });

    test('a client message is never an internal note', () {
      expect(
        of(
          author: MessageAuthor.client,
          kind: MessageKind.clientMessage,
          status: MessageStatus.published,
        ).isInternalNote,
        isFalse,
      );
    });

    test('a staged draft is waiting on the associate', () {
      final draft = of(status: MessageStatus.awaitingSignOff);

      expect(draft.needsSignOff, isTrue);
      expect(draft.isInternalNote, isFalse);
    });

    test('the ticks follow the delivery state', () {
      expect(of(status: MessageStatus.sent).isDelivered, isFalse);
      expect(of(status: MessageStatus.delivered).isDelivered, isTrue);
      expect(of(status: MessageStatus.delivered).isRead, isFalse);
      expect(of(status: MessageStatus.read).isRead, isTrue);
    });

    test('an optimistic message reports its own sending state', () {
      final sending = of(deliveryStatus: MessageDeliveryStatus.sending);
      final failed = of(deliveryStatus: MessageDeliveryStatus.failed);

      expect(sending.isSending, isTrue);
      expect(sending.isFailed, isFalse);
      expect(failed.isFailed, isTrue);
      expect(failed.isSending, isFalse);
    });

    test('copyWith changes the delivery state without touching the text', () {
      final original = of(status: MessageStatus.sent);

      final failed = original.copyWith(
        deliveryStatus: MessageDeliveryStatus.failed,
      );

      expect(failed.deliveryStatus, MessageDeliveryStatus.failed);
      expect(failed.text, original.text);
      expect(failed.status, MessageStatus.sent);
      expect(failed.createdAt, original.createdAt);
    });

    test('copyWith can clear the delivery state', () {
      final failed = of(deliveryStatus: MessageDeliveryStatus.failed);

      expect(failed.copyWith(clearDeliveryStatus: true).deliveryStatus, isNull);
    });
  });

  group('ThreadMessage briefing blocks', () {
    test('reads a client_message block as the client\u2019s words and channel handle', () {
      // The block's own field is `text`, and `from` is the channel handle. A thread that
      // read only the first `text` block drew this as an empty bubble.
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_8',
        'authorKind': 'System',
        'kind': 'ClientMessage',
        'status': 'Published',
        'contentBlocks': [
          {'type': 'client_message', 'from': 'whatsapp:+94771234567', 'text': 'Is it ready?'},
        ],
      });

      expect(message.author, MessageAuthor.client);
      expect(message.isFromClient, isTrue);
      expect(message.text, 'Is it ready?');
      expect(message.clientMessageFrom, 'whatsapp:+94771234567');
    });

    test('falls back to the first text block when there is no client_message', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_9',
        'kind': 'Note',
        'contentBlocks': [
          {'type': 'piece', 'name': 'Silk Slip Dress'},
          {'type': 'text', 'text': 'Three pieces match the brief.'},
          {'type': 'at_a_glance', 'columns': ['Size'], 'rows': [['M']]},
        ],
      });

      expect(message.text, 'Three pieces match the brief.');
    });

    test('keeps every block, in order, so the renderer can draw each one', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_10',
        'kind': 'Note',
        'contentBlocks': [
          {'type': 'text', 'text': 'A summary.'},
          {'type': 'piece', 'name': 'Silk Slip Dress', 'price': 24000},
          {'type': 'at_a_glance', 'columns': ['Size', 'Stock'], 'rows': [['M', '2']]},
        ],
      });

      expect(message.blocks.map((block) => block.type), [
        'text',
        'piece',
        'at_a_glance',
      ]);
      expect(message.blocks[1].name, 'Silk Slip Dress');
      expect(message.blocks[1].amount, 24000);
      expect(message.blocks[2].rows, hasLength(1));
      expect(message.blocks[2].columns, ['Size', 'Stock']);
    });

    test('reads a choice block with its options', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_11',
        'kind': 'Note',
        'contentBlocks': [
          {
            'type': 'choice',
            'prompt': 'Which one did you mean?',
            'options': [
              {'customerId': 'aaaa', 'fullName': 'Nadeesha Perera', 'status': 'active'},
              {'customerId': 'bbbb', 'fullName': 'Nadeesha Silva', 'status': 'new'},
            ],
          },
        ],
      });

      final choice = message.blocks.single;
      expect(choice.prompt, 'Which one did you mean?');
      expect(choice.options, hasLength(2));
      expect(choice.options.first['customerId'], 'aaaa');
      expect(choice.options.first['fullName'], 'Nadeesha Perera');
    });

    test('carries the clientMessageId a retry reuses', () {
      final message = ThreadMessage.fromJson(const {
        'id': 'msg_12',
        'authorKind': 'User',
        'clientMessageId': '22222222-2222-4222-8222-222222222222',
      });

      expect(message.clientMessageId, '22222222-2222-4222-8222-222222222222');
    });

    test('an approved SignOff is not an internal note', () {
      // A SignOff is published once approved. Drawn by status alone it would read
      // `NOTE · NOT SENT`, which is the record lying about a decision that was made.
      final approved = ThreadMessage.fromJson(const {
        'id': 'msg_13',
        'authorKind': 'Agent',
        'kind': 'SignOff',
        'status': 'Published',
        'contentBlocks': [
          {'type': 'sign_off', 'amount': 48000, 'reason': 'above discretionary limit'},
        ],
      });

      expect(approved.isInternalNote, isFalse);
      expect(approved.isApprovedSignOff, isTrue);
      expect(approved.isDismissedSignOff, isFalse);
    });

    test('a dismissed SignOff is dismissed rather than a note', () {
      final dismissed = ThreadMessage.fromJson(const {
        'id': 'msg_14',
        'authorKind': 'Agent',
        'kind': 'SignOff',
        'status': 'Cancelled',
      });

      expect(dismissed.isInternalNote, isFalse);
      expect(dismissed.isDismissedSignOff, isTrue);
      expect(dismissed.isApprovedSignOff, isFalse);
    });

    test('a staged SignOff is awaiting, not a note', () {
      final staged = ThreadMessage.fromJson(const {
        'id': 'msg_15',
        'authorKind': 'Agent',
        'kind': 'SignOff',
        'status': 'AwaitingSignOff',
      });

      expect(staged.needsSignOff, isTrue);
      expect(staged.isInternalNote, isFalse);
    });

    test('copyWith carries the blocks and the send key', () {
      final original = ThreadMessage.fromJson(const {
        'id': 'msg_16',
        'clientMessageId': '22222222-2222-4222-8222-222222222222',
        'contentBlocks': [
          {'type': 'text', 'text': 'hello'},
        ],
      });

      final copy = original.copyWith(status: MessageStatus.sent);

      expect(copy.clientMessageId, original.clientMessageId);
      expect(copy.blocks.map((block) => block.type), ['text']);
      expect(copy.text, 'hello');
    });
  });
}
