import 'package:aveline_mobile/features/conversations/data/thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/client_thread_controller.dart';
import 'package:flutter_test/flutter_test.dart';

final DateTime _now = DateTime.utc(2026, 9, 18, 12);

const Conversation _nadeesha = Conversation(
  id: 'cnv_nadeesha',
  kind: ConversationKind.customer,
  customerId: 'cus_204',
  customerName: 'Nadeesha Perera',
);

/// Five messages, oldest first.
class _FakeThread implements ThreadRepository {
  _FakeThread(this.items);

  List<ThreadMessage> items;

  bool failFetch = false;
  bool failSend = false;
  bool failDecide = false;

  final List<String> sent = [];
  final List<({String id, bool approved})> decided = [];
  final List<int> requestedPages = [];

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
  }) async {
    requestedPages.add(page);
    if (failFetch) {
      throw Exception('The thread is unavailable.');
    }
    final all = items;
    final start = (page - 1) * pageSize;
    final slice = pageSize <= 0 || start >= all.length
        ? <ThreadMessage>[]
        : all.sublist(start, (start + pageSize).clamp(0, all.length));
    return ThreadPage(
      items: List.unmodifiable(slice),
      total: all.length,
      page: page,
      pageSize: pageSize,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(String conversationId, String text) async {
    if (failSend) {
      throw Exception('The message could not be sent.');
    }
    sent.add(text);
    final stored = ThreadMessage(
      id: 'stored_${sent.length}',
      author: MessageAuthor.staff,
      status: MessageStatus.sent,
      text: text,
      createdAt: _now,
    );
    items = [...items, stored];
    return stored;
  }

  @override
  Future<void> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async {
    if (failDecide) {
      throw Exception('The decision could not be recorded.');
    }
    decided.add((id: message.id, approved: approved));
    items = [
      for (final item in items)
        if (item.id == message.id)
          item.copyWith(
            status: approved ? MessageStatus.sent : MessageStatus.cancelled,
          )
        else
          item,
    ];
  }
}

ThreadMessage _message(
  int n, {
  MessageAuthor author = MessageAuthor.staff,
  MessageKind kind = MessageKind.note,
  MessageStatus status = MessageStatus.sent,
}) => ThreadMessage(
  id: 'msg_$n',
  author: author,
  kind: kind,
  status: status,
  text: 'Message $n',
  createdAt: _now.subtract(Duration(minutes: 10 - n)),
);

/// An exchange: the client asks, the associate answers.
List<ThreadMessage> _five() => [
  _message(
    1,
    author: MessageAuthor.client,
    kind: MessageKind.clientMessage,
    status: MessageStatus.published,
  ),
  _message(2, status: MessageStatus.read),
  _message(3, status: MessageStatus.published),
  _message(4, author: MessageAuthor.client, kind: MessageKind.clientMessage),
  _message(5, status: MessageStatus.delivered),
];

ClientThreadController _controller(
  _FakeThread thread, {
  int pageSize = 50,
}) => ClientThreadController(_nadeesha, thread, pageSize: pageSize);

void main() {
  group('ClientThreadController.load', () {
    test('opens on the newest word, with the thread reading downwards', () async {
      final controller = _controller(_FakeThread(_five()));

      await controller.load();

      expect(controller.messages.map((item) => item.id), [
        'msg_1',
        'msg_2',
        'msg_3',
        'msg_4',
        'msg_5',
      ]);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.errorMessage, isNull);
    });

    test('a thread longer than a page opens on its last one', () async {
      // History is served oldest first, so page one holds the oldest messages.
      // A thread that opens on them would open at the wrong end of the story.
      final thread = _FakeThread(_five());
      final controller = _controller(thread, pageSize: 2);

      await controller.load();

      expect(controller.messages, hasLength(1));
      expect(controller.messages.single.id, 'msg_5');
      expect(thread.requestedPages, [1, 3]);
      expect(controller.hasEarlier, isTrue);
    });

    test('a thread that fits in one page is not read twice', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);

      await controller.load();

      expect(thread.requestedPages, [1]);
      expect(controller.hasEarlier, isFalse);
    });

    test('an empty thread is empty rather than broken', () async {
      final controller = _controller(_FakeThread([]));

      await controller.load();

      expect(controller.messages, isEmpty);
      expect(controller.isEmpty, isTrue);
      expect(controller.hasEarlier, isFalse);
    });

    test('a failed read surfaces its message and settles', () async {
      final thread = _FakeThread(_five())..failFetch = true;
      final controller = _controller(thread);

      await controller.load();

      expect(controller.errorMessage, 'The thread is unavailable.');
      expect(controller.messages, isEmpty);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
    });

    test('notifies its listeners while it loads', () async {
      final controller = _controller(_FakeThread(_five()));
      var notifications = 0;
      controller.addListener(() => notifications++);

      await controller.load();

      expect(notifications, greaterThan(0));
    });
  });

  group('ClientThreadController.loadEarlier', () {
    test('prepends the page before the window, without repeating anything', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread, pageSize: 2);
      await controller.load();

      await controller.loadEarlier();

      // Page two of a five-message thread, at two a page, holds msg_3 and msg_4.
      expect(controller.messages.map((item) => item.id), [
        'msg_3',
        'msg_4',
        'msg_5',
      ]);
      expect(controller.hasEarlier, isTrue);

      await controller.loadEarlier();

      expect(controller.messages.map((item) => item.id), [
        'msg_1',
        'msg_2',
        'msg_3',
        'msg_4',
        'msg_5',
      ]);
      expect(controller.hasEarlier, isFalse);
    });

    test('does nothing once the whole thread is on screen', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.loadEarlier();

      expect(thread.requestedPages, [1]);
    });

    test('a failed page leaves what is already there', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread, pageSize: 2);
      await controller.load();

      thread.failFetch = true;
      await controller.loadEarlier();

      expect(controller.messages, hasLength(1));
      expect(controller.isLoadingEarlier, isFalse);
      expect(controller.errorMessage, 'The thread is unavailable.');
    });
  });

  group('ClientThreadController.send', () {
    test('shows the message before the API confirms it', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      final pending = controller.send('On my way.');

      expect(controller.messages, hasLength(6));
      expect(controller.messages.last.text, 'On my way.');
      expect(controller.messages.last.isSending, isTrue);
      expect(controller.messages.last.isFromBoutique, isTrue);

      await pending;

      expect(thread.sent, ['On my way.']);
      expect(controller.messages.last.id, 'stored_1');
      expect(controller.messages.last.isSending, isFalse);
      expect(controller.isSending, isFalse);
    });

    test('sending into an empty thread starts it', () async {
      final thread = _FakeThread([]);
      final controller = _controller(thread);
      await controller.load();

      await controller.send('Hello there.');

      expect(controller.messages, hasLength(1));
      expect(controller.messages.single.text, 'Hello there.');
      expect(controller.isEmpty, isFalse);
    });

    test('a refusal leaves the message on screen, marked failed', () async {
      final thread = _FakeThread(_five())..failSend = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.send('On my way.');

      expect(controller.messages, hasLength(6));
      expect(controller.messages.last.isFailed, isTrue);
      expect(controller.messages.last.text, 'On my way.');
      expect(controller.actionError, contains('could not be sent'));
    });

    test('a failed message can be sent again', () async {
      final thread = _FakeThread(_five())..failSend = true;
      final controller = _controller(thread);
      await controller.load();
      await controller.send('On my way.');
      final failed = controller.messages.last;

      thread.failSend = false;
      await controller.retry(failed);

      expect(controller.messages.last.isFailed, isFalse);
      expect(controller.messages.last.id, 'stored_1');
      expect(thread.sent, ['On my way.']);
    });

    test('retrying something that did not fail changes nothing', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.retry(controller.messages.last);

      expect(thread.sent, isEmpty);
      expect(controller.messages, hasLength(5));
    });

    test('the thread reports that it is sending while a message is in flight', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      final pending = controller.send('On my way.');
      expect(controller.isSending, isTrue);

      await pending;
      expect(controller.isSending, isFalse);
    });
  });

  group('ClientThreadController drafts', () {
    List<ThreadMessage> withDraft() => [
      _message(
        1,
        author: MessageAuthor.client,
        kind: MessageKind.clientMessage,
      ),
      _message(2, author: MessageAuthor.agent, status: MessageStatus.awaitingSignOff),
    ];

    /// The reply waiting on the associate, which the thread draws in place.
    ThreadMessage draftOf(ClientThreadController controller) =>
        controller.messages.firstWhere((item) => item.needsSignOff);

    test('a thread with nothing staged has nothing waiting', () async {
      final controller = _controller(_FakeThread(_five()));

      await controller.load();

      expect(
        controller.messages.any((item) => item.needsSignOff),
        isFalse,
      );
    });

    test('approving sends the draft', () async {
      final thread = _FakeThread(withDraft());
      final controller = _controller(thread);
      await controller.load();

      await controller.decideDraft(draftOf(controller), approved: true);

      expect(
        controller.messages.any((item) => item.needsSignOff),
        isFalse,
      );
      expect(
        controller.messages.firstWhere((item) => item.id == 'msg_2').status,
        MessageStatus.sent,
      );
      expect(thread.decided.single.approved, isTrue);
      expect(controller.actionError, isNull);
    });

    test('dismissing leaves it in the thread as a decision that was made', () async {
      final thread = _FakeThread(withDraft());
      final controller = _controller(thread);
      await controller.load();

      await controller.decideDraft(draftOf(controller), approved: false);

      expect(
        controller.messages.any((item) => item.needsSignOff),
        isFalse,
      );
      expect(
        controller.messages.firstWhere((item) => item.id == 'msg_2').status,
        MessageStatus.cancelled,
      );
      expect(thread.decided.single.approved, isFalse);
    });

    test('a refused decision puts the draft back where it was', () async {
      final thread = _FakeThread(withDraft())..failDecide = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.decideDraft(draftOf(controller), approved: true);

      expect(
        controller.messages
            .firstWhere((item) => item.id == 'msg_2')
            .needsSignOff,
        isTrue,
      );
      expect(controller.actionError, contains('could not be recorded'));
    });

    test('deciding something that is not staged changes nothing', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.decideDraft(controller.messages.last, approved: true);

      expect(thread.decided, isEmpty);
      expect(controller.messages.last.status, MessageStatus.delivered);
    });
  });

  group('ClientThreadController.actionError', () {
    test('is cleared on request, so a toast is shown once', () async {
      final thread = _FakeThread(_five())..failSend = true;
      final controller = _controller(thread);
      await controller.load();
      await controller.send('On my way.');

      expect(controller.actionError, isNotNull);

      controller.clearActionError();

      expect(controller.actionError, isNull);
    });
  });
}
