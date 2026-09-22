import 'dart:typed_data';

import 'package:aveline_mobile/features/conversations/data/thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_attachment.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/client_thread_controller.dart';
import 'package:dio/dio.dart';
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
  bool failSelect = false;

  /// A raw error the read throws, for the status-mapping cases.
  Object? fetchError;

  /// What the response echoes as its `pageSize`. `null` echoes what was asked for; a value
  /// models the server's clamp, and `0` models a body that omitted the field.
  int? echoPageSize;

  final List<String> sent = [];
  final List<String?> sentKeys = [];
  final List<List<String>> sentAttachments = [];
  final List<String> uploaded = [];
  bool failUpload = false;
  final List<({String id, bool approved})> decided = [];
  final List<({String customerId, String? query})> selected = [];
  final List<String> markedRead = [];
  final List<String> revoked = [];
  bool failMarkRead = false;
  bool failRevoke = false;
  final List<int> requestedPages = [];
  final List<int> requestedPageSizes = [];

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    requestedPages.add(page);
    requestedPageSizes.add(pageSize);
    if (fetchError != null) {
      throw fetchError!;
    }
    if (failFetch) {
      throw Exception('The thread is unavailable.');
    }
    if (around != null) {
      // The server decides which page holds the anchor and echoes it; the fake mirrors that so
      // the controller's anchored path is exercised.
      final index = items.indexWhere((item) => item.id == around);
      if (index >= 0) {
        final anchored = (index ~/ pageSize) + 1;
        final from = (anchored - 1) * pageSize;
        return ThreadPage(
          items: List.unmodifiable(
            items.sublist(from, (from + pageSize).clamp(0, items.length)),
          ),
          total: items.length,
          page: anchored,
          pageSize: echoPageSize ?? pageSize,
        );
      }
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
      pageSize: echoPageSize ?? pageSize,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) async {
    // Recorded before the failure check, so a test can assert the key both attempts carried.
    sentKeys.add(clientMessageId);
    if (failSend) {
      throw Exception('The message could not be sent.');
    }
    sent.add(text);
    sentAttachments.add(attachmentIds);
    final stored = ThreadMessage(
      id: 'stored_${sent.length}',
      author: MessageAuthor.staff,
      status: MessageStatus.sent,
      text: text,
      createdAt: _now,
      clientMessageId: clientMessageId,
    );
    items = [...items, stored];
    return stored;
  }

  @override
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async {
    if (failDecide) {
      throw Exception('The decision could not be recorded.');
    }
    decided.add((id: message.id, approved: approved));
    final stored = message.copyWith(
      status: approved ? MessageStatus.sent : MessageStatus.cancelled,
    );
    items = [
      for (final item in items)
        if (item.id == message.id) stored else item,
    ];
    return stored;
  }

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) async {
    if (failSelect) {
      throw Exception('That client could not be linked.');
    }
    selected.add((customerId: customerId, query: query));
  }

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) async {
    if (failMarkRead) {
      throw Exception('The marker could not be written.');
    }
    markedRead.add(lastReadMessageId);
  }

  @override
  Future<ThreadAttachment> uploadAttachment(
    String conversationId, {
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) async {
    if (failUpload) {
      throw Exception('The file could not be uploaded.');
    }
    final id = 'att_${uploaded.length + 1}';
    uploaded.add(id);
    return ThreadAttachment(
      id: id,
      url: '/api/v1/orgs/org/conversations/$conversationId/attachments/$id',
      contentType: contentType,
      fileName: fileName,
      sizeBytes: bytes.length,
      width: width,
      height: height,
    );
  }

  @override
  Future<Uint8List> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  ) async => Uint8List.fromList([1, 2, 3]);

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) async {
    if (failRevoke) {
      throw Exception('That approval could not be taken back.');
    }
    revoked.add(messageId);
    final stored = items
        .firstWhere((item) => item.id == messageId)
        .copyWith(status: MessageStatus.awaitingSignOff);
    items = [
      for (final item in items)
        if (item.id == messageId) stored else item,
    ];
    return stored;
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

    test('the last page is computed from the echoed pageSize, not the requested one', () async {
      // A request of 500 against a 300-message thread is clamped to 200 by the server. The
      // second read must carry the clamped value, or the window lands on the wrong page.
      final thread = _FakeThread([
        for (var n = 1; n <= 300; n++) _message(n),
      ])..echoPageSize = 200;
      final controller = _controller(thread, pageSize: 500);

      await controller.load();

      expect(thread.requestedPages, [1, 2]);
      expect(thread.requestedPageSizes, [500, 200]);
      expect(controller.messages, hasLength(100));
      expect(controller.hasEarlier, isTrue);
    });

    test('a response without a pageSize stays a single page-one read', () async {
      final thread = _FakeThread(_five())..echoPageSize = 0;
      final controller = _controller(thread, pageSize: 2);

      await controller.load();

      expect(thread.requestedPages, [1]);
      expect(controller.messages, hasLength(2));
      expect(controller.hasEarlier, isFalse);
    });

    test('a 403 and a timeout render different messages', () async {
      final request = RequestOptions(path: '/messages');
      final forbidden = _controller(
        _FakeThread(_five())
          ..fetchError = DioException(
            requestOptions: request,
            response: Response<void>(requestOptions: request, statusCode: 403),
          ),
      );
      final timeout = _controller(
        _FakeThread(_five())
          ..fetchError = DioException(
            requestOptions: request,
            type: DioExceptionType.connectionTimeout,
          ),
      );

      await forbidden.load();
      await timeout.load();

      expect(forbidden.errorMessage, isNotNull);
      expect(timeout.errorMessage, isNotNull);
      expect(forbidden.errorMessage, isNot(timeout.errorMessage));
      // The access refusal says what it is; a timeout does not claim the caller is denied.
      expect(forbidden.errorMessage, contains('access'));
      expect(timeout.errorMessage, isNot(contains('access')));
    });

    test('a 404 says the conversation is gone rather than showing the raw exception', () async {
      final request = RequestOptions(path: '/messages');
      final controller = _controller(
        _FakeThread(_five())
          ..fetchError = DioException(
            requestOptions: request,
            response: Response<void>(requestOptions: request, statusCode: 404),
          ),
      );

      await controller.load();

      expect(controller.errorMessage, contains('no longer available'));
      expect(controller.errorMessage, isNot(contains('DioException')));
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

    test('a retry reuses the one clientMessageId the composed message carries', () async {
      // The window between the row being stored and the client giving up is wide, so a
      // retry must carry the same key: the server then answers with the stored message
      // instead of storing the sentence twice.
      final thread = _FakeThread(_five())..failSend = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.send('On my way.');
      final failed = controller.messages.last;
      expect(failed.clientMessageId, isNotNull);
      expect(
        RegExp(
          r'^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$',
        ).hasMatch(failed.clientMessageId!),
        isTrue,
        reason: 'the key must be a well-formed version-4 UUID',
      );

      thread.failSend = false;
      await controller.retry(failed);

      expect(thread.sentKeys, hasLength(2));
      expect(thread.sentKeys.first, thread.sentKeys.last);
    });

    test('two different composed messages carry two different keys', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.send('First.');
      await controller.send('Second.');

      expect(thread.sentKeys, hasLength(2));
      expect(thread.sentKeys.first, isNot(thread.sentKeys.last));
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

      // Exactly one row: the optimistic update is replaced back, not pushed aside by a
      // second copy of the draft.
      expect(controller.messages.where((item) => item.id == 'msg_2'), hasLength(1));
      expect(
        controller.messages
            .firstWhere((item) => item.id == 'msg_2')
            .needsSignOff,
        isTrue,
      );
      expect(controller.actionError, contains('could not be recorded'));
    });

    test('an approval reconciles the server\u2019s own status', () async {
      // The controller must draw what the server stored, not a guessed status, and it must
      // not leave the optimistic row behind as a second copy.
      final thread = _FakeThread(withDraft());
      final controller = _controller(thread);
      await controller.load();

      await controller.decideDraft(draftOf(controller), approved: true);

      expect(controller.messages.where((item) => item.id == 'msg_2'), hasLength(1));
      expect(
        controller.messages.firstWhere((item) => item.id == 'msg_2').status,
        MessageStatus.sent,
      );
      expect(thread.decided.single.approved, isTrue);
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

  group('ClientThreadController.selectCustomer', () {
    test('binds the thread to the client an option named, then re-reads it', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.selectCustomer('aaaa-bbbb', query: 'Nadeesha');

      expect(thread.selected.single.customerId, 'aaaa-bbbb');
      expect(thread.selected.single.query, 'Nadeesha');
      // The header's name follows from the binding, so the window is re-read rather than
      // patched with a guess.
      expect(thread.requestedPages.last, 1);
      expect(controller.actionError, isNull);
    });

    test('a refused binding says so and keeps the thread', () async {
      final thread = _FakeThread(_five())..failSelect = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.selectCustomer('aaaa-bbbb');

      expect(controller.messages, hasLength(5));
      expect(controller.actionError, contains('could not be linked'));
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

  group('ClientThreadController.load(around:)', () {
    List<ThreadMessage> twelve() => [for (var n = 1; n <= 12; n++) _message(n)];

    test('opens the page that holds the anchor, and earlier history follows', () async {
      // A notification about msg_8 opens on the page holding it, not on the newest words.
      final controller = _controller(_FakeThread(twelve()), pageSize: 5);

      await controller.load(around: 'msg_8');

      expect(controller.messages.map((item) => item.id), [
        'msg_6',
        'msg_7',
        'msg_8',
        'msg_9',
        'msg_10',
      ]);
      expect(controller.hasEarlier, isTrue);
    });

    test('an anchor on page one has no earlier history', () async {
      final controller = _controller(_FakeThread(twelve()), pageSize: 5);

      await controller.load(around: 'msg_2');

      expect(controller.messages.first.id, 'msg_1');
      expect(controller.hasEarlier, isFalse);
    });

    test('an unknown anchor falls back to the newest words', () async {
      final controller = _controller(_FakeThread(twelve()), pageSize: 5);

      await controller.load(around: 'msg_404');

      // The server served page 1 because it could not find the anchor; the window is still
      // contiguous and the associate can page forward.
      expect(controller.messages, isNotEmpty);
    });
  });

  group('ClientThreadController.receive', () {
    test('a duplicate echo is not appended', () async {
      final controller = _controller(_FakeThread(_five()));
      await controller.load();

      controller.receive(_message(3));

      expect(controller.messages, hasLength(5));
      expect(controller.messages.where((item) => item.id == 'msg_3'), hasLength(1));
    });

    test('an echo reconciles an in-flight optimistic send by its key', () async {
      final thread = _FakeThread([]);
      final controller = _controller(thread);
      await controller.load();

      // Not awaited: the send is still in flight when the hub echoes it back.
      final pending = controller.send('On my way.');
      final optimistic = controller.messages.last;
      final key = optimistic.clientMessageId!;

      controller.receive(
        ThreadMessage(
          id: 'stored_9',
          author: MessageAuthor.staff,
          status: MessageStatus.sent,
          text: 'On my way.',
          createdAt: optimistic.createdAt,
          clientMessageId: key,
        ),
      );

      expect(controller.messages, hasLength(1));
      expect(controller.messages.single.id, 'stored_9');
      expect(controller.messages.single.isSending, isFalse);

      await pending;
      expect(controller.messages, hasLength(1));
    });

    test('an echo that belongs earlier is inserted in order, not appended', () async {
      final controller = _controller(_FakeThread(_five()));
      await controller.load();

      controller.receive(
        ThreadMessage(
          id: 'msg_0',
          author: MessageAuthor.client,
          kind: MessageKind.clientMessage,
          status: MessageStatus.published,
          text: 'Earlier.',
          createdAt: _now.subtract(const Duration(minutes: 20)),
        ),
      );

      expect(controller.messages.first.id, 'msg_0');
      expect(controller.messages, hasLength(6));
    });

    test('a message newer than the window is appended', () async {
      final controller = _controller(_FakeThread(_five()));
      await controller.load();

      controller.receive(
        ThreadMessage(
          id: 'msg_99',
          author: MessageAuthor.client,
          kind: MessageKind.clientMessage,
          status: MessageStatus.published,
          text: 'Just now.',
          createdAt: _now.add(const Duration(minutes: 1)),
        ),
      );

      expect(controller.messages.last.id, 'msg_99');
    });

    test('a delivery update changes the row in place', () async {
      final controller = _controller(_FakeThread(_five()));
      await controller.load();

      controller.receive(_message(2, status: MessageStatus.delivered));

      expect(controller.messages, hasLength(5));
      expect(
        controller.messages.firstWhere((item) => item.id == 'msg_2').status,
        MessageStatus.delivered,
      );
    });

    test('a local id is never accepted from the hub', () async {
      final controller = _controller(_FakeThread(_five()));
      await controller.load();

      controller.receive(
        ThreadMessage(
          id: 'local_7',
          author: MessageAuthor.staff,
          text: 'Not from the server.',
          createdAt: _now,
        ),
      );

      expect(controller.messages, hasLength(5));
    });
  });

  group('ClientThreadController attachments', () {
    test('uploads a picked file and binds it on send', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.attach(
        bytes: Uint8List.fromList([1, 2, 3]),
        contentType: 'image/png',
        fileName: 'photo.png',
      );

      expect(thread.uploaded, ['att_1']);
      expect(controller.pendingAttachments.single.isUploaded, isTrue);
      expect(controller.isUploadingAttachments, isFalse);

      await controller.send('Here it is.');

      expect(thread.sentAttachments.single, ['att_1']);
      // The tray empties only once the message that bound the files is stored.
      expect(controller.pendingAttachments, isEmpty);
    });

    test('a failed upload blocks the send until it is retried', () async {
      final thread = _FakeThread(_five())..failUpload = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.attach(
        bytes: Uint8List.fromList([1]),
        contentType: 'image/png',
        fileName: 'photo.png',
      );

      final pending = controller.pendingAttachments.single;
      expect(pending.isFailed, isTrue);
      // A send never goes out naming bytes the server has not stored.
      await controller.send('Here it is.');
      expect(thread.sent, isEmpty);

      thread.failUpload = false;
      await controller.retryAttachment(pending.localId);
      expect(controller.pendingAttachments.single.isUploaded, isTrue);

      await controller.send('Here it is.');
      expect(thread.sentAttachments.single, ['att_1']);
    });

    test('removing a held file drops it from the next send', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();
      await controller.attach(
        bytes: Uint8List.fromList([1]),
        contentType: 'image/png',
        fileName: 'photo.png',
      );

      controller.removeAttachment(controller.pendingAttachments.single.localId);
      await controller.send('Text only.');

      expect(controller.pendingAttachments, isEmpty);
      expect(thread.sentAttachments.single, isEmpty);
    });

    test('a full tray of five uploads, and the message binds all five', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      for (var n = 1; n <= 5; n++) {
        await controller.attach(
          bytes: Uint8List.fromList([n]),
          contentType: 'image/png',
          fileName: 'photo_$n.png',
        );
      }

      expect(thread.uploaded, hasLength(5));
      expect(controller.pendingAttachments, hasLength(5));
      expect(controller.areAttachmentsReady, isTrue);

      await controller.send('All five.');

      expect(thread.sentAttachments.single, hasLength(5));
      expect(controller.pendingAttachments, isEmpty);
    });

    test('refuses a sixth file in the server\u2019s own words, before it is uploaded', () async {
      // The cap is the API's (`MediaContentTypes.cs:31`, enforced at
      // `ConversationService.cs:307-310`); the client moves the same rule to the pick path so
      // nothing that could never be bound is uploaded, and so the associate gets a specific
      // answer rather than the generic send failure.
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      for (var n = 1; n <= 5; n++) {
        await controller.attach(
          bytes: Uint8List.fromList([n]),
          contentType: 'image/png',
          fileName: 'photo_$n.png',
        );
      }
      expect(thread.uploaded, hasLength(5));

      await controller.attach(
        bytes: Uint8List.fromList([6]),
        contentType: 'image/png',
        fileName: 'photo_6.png',
      );

      // The upload call count is unchanged: the sixth file never reaches the API.
      expect(thread.uploaded, hasLength(5));
      expect(controller.pendingAttachments, hasLength(5));
      // Exactly the message the API itself would answer with.
      expect(controller.actionError, 'A message may carry at most 5 attachments.');
    });

    test('a failure names the file it could not upload', () async {
      final thread = _FakeThread(_five())..failUpload = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.attach(
        bytes: Uint8List.fromList([1]),
        contentType: 'image/png',
        fileName: 'photo.png',
      );

      expect(controller.pendingAttachments.single.isFailed, isTrue);
      expect(
        controller.pendingAttachments.single.error,
        'Could not upload photo.png.',
      );
    });
  });

  group('ClientThreadController.revokeSignOff', () {
    test('returns an approved SignOff to awaiting', () async {
      final thread = _FakeThread([
        _message(
          1,
          author: MessageAuthor.agent,
          kind: MessageKind.signOff,
          status: MessageStatus.published,
        ),
      ]);
      final controller = _controller(thread);
      await controller.load();

      await controller.revokeSignOff(controller.messages.single);

      expect(thread.revoked, ['msg_1']);
      expect(controller.messages, hasLength(1));
      expect(controller.messages.single.status, MessageStatus.awaitingSignOff);
      expect(controller.messages.single.needsSignOff, isTrue);
      expect(controller.actionError, isNull);
    });

    test('never revokes something that is not an approved SignOff', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.revokeSignOff(controller.messages.last);

      expect(thread.revoked, isEmpty);
    });

    test('a refused revocation puts nothing back and says so', () async {
      final thread = _FakeThread([
        _message(
          1,
          author: MessageAuthor.agent,
          kind: MessageKind.signOff,
          status: MessageStatus.published,
        ),
      ])..failRevoke = true;
      final controller = _controller(thread);
      await controller.load();

      await controller.revokeSignOff(controller.messages.single);

      expect(controller.messages.single.status, MessageStatus.published);
      expect(controller.actionError, contains('could not be taken back'));
    });
  });

  group('ClientThreadController read marker', () {
    test('marks the newest server message on open', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);

      await controller.load();

      expect(thread.markedRead, ['msg_5']);
    });

    test('never sends a local id as the marker', () async {
      final thread = _FakeThread([]);
      final controller = _controller(thread);
      await controller.load();

      // The optimistic row is the only message on screen while the send is in flight.
      final pending = controller.send('On my way.');
      expect(controller.messages.single.id, startsWith('local_'));
      expect(thread.markedRead, isEmpty);

      await pending;
      // Once the server answered, its own id is the marker.
      expect(thread.markedRead, ['stored_1']);
    });

    test('advances when a new server message arrives', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.receive(
        ThreadMessage(
          id: 'msg_99',
          author: MessageAuthor.client,
          kind: MessageKind.clientMessage,
          status: MessageStatus.published,
          text: 'Just now.',
          createdAt: _now.add(const Duration(minutes: 1)),
        ),
      );

      expect(thread.markedRead, ['msg_5', 'msg_99']);
    });

    test('does not repeat a write for the same position', () async {
      final thread = _FakeThread(_five());
      final controller = _controller(thread);
      await controller.load();

      await controller.receive(_message(5, status: MessageStatus.delivered));

      expect(thread.markedRead, ['msg_5']);
    });

    test('a refused marker is silent, not a toast', () async {
      final thread = _FakeThread(_five())..failMarkRead = true;
      final controller = _controller(thread);

      await controller.load();

      expect(controller.actionError, isNull);
      expect(controller.errorMessage, isNull);
      expect(thread.markedRead, isEmpty);
    });
  });
}
