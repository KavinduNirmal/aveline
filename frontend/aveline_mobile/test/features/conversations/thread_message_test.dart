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

    test('an agent message that was never sent is an internal note too', () {
      expect(
        of(author: MessageAuthor.agent, status: MessageStatus.published)
            .isInternalNote,
        isTrue,
      );
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
}
