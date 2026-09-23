import 'dart:async';

import 'package:aveline_mobile/core/config/app_config.dart';
import 'package:aveline_mobile/core/network/auth_token_provider.dart';
import 'package:aveline_mobile/core/network/conversation_realtime_service.dart';
import 'package:aveline_mobile/core/notifications/realtime_connection.dart';
import 'package:aveline_mobile/core/providers/boutique_provider.dart';
import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/conversations/data/empty_thread_repository.dart';
import 'package:aveline_mobile/features/conversations/data/thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_attachment.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/attachment_opener.dart';
import 'package:aveline_mobile/features/conversations/presentation/attachment_picker.dart';
import 'package:aveline_mobile/features/conversations/presentation/screens/client_thread_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

/// A real 1x1 transparent PNG, so an image attachment has something decodable.
const List<int> _tinyPng = [
  0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D,
  0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
  0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
  0x0A, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
  0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
  0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
];

const Conversation _nadeesha = Conversation(
  id: 'cnv_nadeesha',
  kind: ConversationKind.customer,
  customerId: 'cus_204',
  customerName: 'Nadeesha Perera',
);

const Conversation _menaka = Conversation(
  id: 'cnv_menaka',
  kind: ConversationKind.customer,
  customerId: 'cus_311',
  customerName: 'Menaka Rathnayake',
);

const Conversation _nobody = Conversation(
  id: 'cnv_nobody',
  kind: ConversationKind.customer,
  customerId: 'cus_1',
  customerName: 'Chamari Silva',
);

/// The threads the screen is driven against.
///
/// These exchanges used to live in `DemoThreadRepository`, which the inbox also
/// fell back to on the live path (T0 removed it from `lib/`). The seed is now
/// unambiguously test data, and it is measured from the wall clock because a row
/// prints its age against `DateTime.now()`.
class _SeedThread implements ThreadRepository {
  _SeedThread({Map<String, List<ThreadMessage>>? seed}) : _overrides = seed ?? {};

  final Map<String, List<ThreadMessage>> _overrides;

  final Map<String, List<ThreadMessage>> _threads = {};

  /// How many messages have been sent through this instance, so the stored ids
  /// are stable within a test.
  int _sentCount = 0;

  /// The clients a `choice` option resolved to.
  final List<String> selectedCustomers = [];

  /// The attachment ids each send carried.
  final List<List<String>> sentAttachments = [];

  /// The message ids the controller told the API it had read.
  final List<String> markedRead = [];

  /// The attachments this fake stored, in order.
  final List<String> uploaded = [];

  /// Whether `uploadAttachment` refuses, so a test can drive the tray's failure state.
  bool failUpload = false;

  /// The bytes `fetchAttachmentBytes` serves.
  final List<int> _attachmentBytes = _tinyPng;

  int _attachmentCount = 0;

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    final thread = _threadOf(conversationId);
    final start = (page - 1) * pageSize;
    final slice = pageSize <= 0 || start >= thread.length
        ? <ThreadMessage>[]
        : thread.sublist(start, (start + pageSize).clamp(0, thread.length));
    return ThreadPage(
      items: List.unmodifiable(slice),
      total: thread.length,
      page: page,
      pageSize: pageSize,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) async {
    sentAttachments.add(attachmentIds);
    final stored = ThreadMessage(
      id: 'msg_sent_${++_sentCount}',
      author: MessageAuthor.staff,
      kind: MessageKind.note,
      status: MessageStatus.sent,
      text: text,
      createdAt: DateTime.now().toUtc(),
      clientMessageId: clientMessageId,
    );
    _threadOf(conversationId).add(stored);
    return stored;
  }

  @override
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async {
    final thread = _threadOf(conversationId);
    final index = thread.indexWhere((item) => item.id == message.id);
    if (index < 0) {
      return message;
    }
    thread[index] = thread[index].copyWith(
      status: approved ? MessageStatus.sent : MessageStatus.cancelled,
    );
    return thread[index];
  }

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) async {
    selectedCustomers.add(customerId);
  }

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) async {
    markedRead.add(lastReadMessageId);
  }

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) async {
    final thread = _threadOf(conversationId);
    final index = thread.indexWhere((item) => item.id == messageId);
    if (index < 0) {
      throw StateError('no such message');
    }
    thread[index] = thread[index].copyWith(
      status: MessageStatus.awaitingSignOff,
    );
    return thread[index];
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
    final id = 'att_${++_attachmentCount}';
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
  ) async => Uint8List.fromList(_attachmentBytes);

  List<ThreadMessage> _threadOf(String conversationId) =>
      _threads.putIfAbsent(
        conversationId,
        () => _overrides[conversationId] ?? _seedFor(conversationId),
      );

  ThreadMessage _seed({
    required String id,
    required MessageAuthor author,
    required String text,
    required Duration age,
    MessageStatus? status,
    String? contentHash,
  }) => ThreadMessage(
    id: id,
    author: author,
    kind: author == MessageAuthor.client
        ? MessageKind.clientMessage
        : MessageKind.note,
    text: text,
    createdAt: DateTime.now().toUtc().subtract(age),
    status: status ?? MessageStatus.published,
    contentHash: contentHash,
  );

  List<ThreadMessage> _seedFor(String conversationId) =>
      switch (conversationId) {
        'cnv_nadeesha' => [
          _seed(
            id: 'msg_nd_1',
            author: MessageAuthor.client,
            text: 'Hi! Is the wine silk saree still there in a medium?',
            age: const Duration(minutes: 55),
          ),
          _seed(
            id: 'msg_nd_2',
            author: MessageAuthor.staff,
            text: 'It is. I have put one aside for you.',
            status: MessageStatus.read,
            age: const Duration(minutes: 50),
          ),
          _seed(
            id: 'msg_nd_3',
            author: MessageAuthor.client,
            text: 'Wonderful. Could it be taken in before Friday evening?',
            age: const Duration(minutes: 30),
          ),
          _seed(
            id: 'msg_nd_4',
            author: MessageAuthor.staff,
            text:
                'She needs it by Friday. The tailor is in tomorrow, so it can be '
                'done in one visit.',
            status: MessageStatus.published,
            age: const Duration(minutes: 20),
          ),
          _seed(
            id: 'msg_nd_5',
            author: MessageAuthor.staff,
            text:
                'Yes, easily. Bring it in tomorrow and we will have it ready for '
                'Thursday.',
            status: MessageStatus.delivered,
            age: const Duration(minutes: 12),
          ),
        ],
        'cnv_menaka' => [
          _seed(
            id: 'msg_mn_1',
            author: MessageAuthor.client,
            text: 'Thank you, but 12% is more than I had hoped for.',
            age: const Duration(hours: 3),
          ),
          _seed(
            id: 'msg_mn_2',
            author: MessageAuthor.staff,
            text: 'Let me see what I can do for the wedding party.',
            status: MessageStatus.read,
            age: const Duration(hours: 2, minutes: 50),
          ),
          _seed(
            id: 'msg_mn_3',
            author: MessageAuthor.agent,
            text:
                'I can hold the 12% as a goodwill gesture on order #4821 and confirm '
                'it this afternoon.',
            status: MessageStatus.awaitingSignOff,
            contentHash: 'hash_menaka_goodwill',
            age: const Duration(hours: 2),
          ),
        ],
        _ => <ThreadMessage>[],
      };
}

/// A short thread with very short words.
///
/// Position is what the layout tests read, and under the test font every glyph is
/// a square, so a realistic message is several times taller than it is on a
/// device and the whole thread will not fit one viewport. These four fit, which
/// lets a test compare where things actually landed.
///
/// The timestamps are measured from the wall clock rather than pinned, because a
/// day separator is compared against the local calendar day: a pinned UTC instant
/// stops reading as "Today" the moment the local date moves past it.
class _StubThread implements ThreadRepository {
  _StubThread();

  final DateTime _now = DateTime.now().toUtc();

  List<ThreadMessage> items = [];

  final List<String> sent = [];
  final List<String> markedRead = [];
  final List<String> uploaded = [];
  final List<int> _attachmentBytes = _tinyPng;
  int _attachmentCount = 0;
  bool failSend = false;

  /// Builds the four short messages against the wall clock.
  void _seedItems() {
    items = [
      ThreadMessage(
        id: 'stub_1',
        author: MessageAuthor.client,
        kind: MessageKind.clientMessage,
        status: MessageStatus.published,
        text: 'Hi',
        createdAt: _now.subtract(const Duration(minutes: 30)),
      ),
      ThreadMessage(
        id: 'stub_2',
        author: MessageAuthor.staff,
        status: MessageStatus.read,
        text: 'It is',
        createdAt: _now.subtract(const Duration(minutes: 20)),
      ),
      ThreadMessage(
        id: 'stub_3',
        author: MessageAuthor.staff,
        status: MessageStatus.published,
        text: 'A note',
        createdAt: _now.subtract(const Duration(minutes: 10)),
      ),
      ThreadMessage(
        id: 'stub_4',
        author: MessageAuthor.staff,
        status: MessageStatus.delivered,
        text: 'Ready',
        createdAt: _now.subtract(const Duration(minutes: 5)),
      ),
    ];
  }

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    if (items.isEmpty) {
      _seedItems();
    }
    final start = (page - 1) * pageSize;
    final slice = start >= items.length
        ? <ThreadMessage>[]
        : items.sublist(start, (start + pageSize).clamp(0, items.length));
    return ThreadPage(
      items: List.unmodifiable(slice),
      total: items.length,
      page: page,
      pageSize: pageSize,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) async {
    if (failSend) {
      throw Exception('The message could not be sent.');
    }
    sent.add(text);
    final stored = ThreadMessage(
      id: 'stub_sent_${sent.length}',
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
  }) async => message.copyWith(
    status: approved ? MessageStatus.sent : MessageStatus.cancelled,
  );

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) async {}

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) async => throw StateError('no approval to take back');

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) async {
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
    final id = 'att_${++_attachmentCount}';
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
  ) async => Uint8List.fromList(_attachmentBytes);
}

/// Holds the first read open until a test releases it.
///
/// A fake with a `Completer` observes the loading frame deterministically, where
/// the demo repository's latency needed a real timer and a matching `pump`.
class _DelayedThread implements ThreadRepository {
  _DelayedThread(this._inner);

  final ThreadRepository _inner;
  final Completer<void> release = Completer<void>();

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    if (!release.isCompleted) {
      await release.future;
    }
    return _inner.fetchMessages(
      conversationId,
      page: page,
      pageSize: pageSize,
      around: around,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) => _inner.sendMessage(
    conversationId,
    text,
    clientMessageId: clientMessageId,
    attachmentIds: attachmentIds,
  );

  @override
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) => _inner.decideSignOff(
    conversationId: conversationId,
    message: message,
    approved: approved,
  );

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) => _inner.selectCustomer(conversationId, customerId, query: query);

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) => _inner.revokeSignOff(conversationId, messageId, reason: reason);

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) =>
      _inner.markRead(conversationId, lastReadMessageId);

  @override
  Future<ThreadAttachment> uploadAttachment(
    String conversationId, {
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) => _inner.uploadAttachment(
    conversationId,
    bytes: bytes,
    contentType: contentType,
    fileName: fileName,
    width: width,
    height: height,
  );

  @override
  Future<Uint8List> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  ) => _inner.fetchAttachmentBytes(conversationId, attachmentId);
}

/// Fails the read until it is told to stop.
class _FailingThread implements ThreadRepository {
  _FailingThread(this._inner);

  final ThreadRepository _inner;
  bool fail = true;

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    if (fail) {
      throw Exception('The thread is unavailable.');
    }
    return _inner.fetchMessages(
      conversationId,
      page: page,
      pageSize: pageSize,
      around: around,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) => _inner.sendMessage(
    conversationId,
    text,
    clientMessageId: clientMessageId,
    attachmentIds: attachmentIds,
  );

  @override
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) => _inner.decideSignOff(
    conversationId: conversationId,
    message: message,
    approved: approved,
  );

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) => _inner.selectCustomer(conversationId, customerId, query: query);

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) => _inner.revokeSignOff(conversationId, messageId, reason: reason);

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) =>
      _inner.markRead(conversationId, lastReadMessageId);

  @override
  Future<ThreadAttachment> uploadAttachment(
    String conversationId, {
    required Uint8List bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) => _inner.uploadAttachment(
    conversationId,
    bytes: bytes,
    contentType: contentType,
    fileName: fileName,
    width: width,
    height: height,
  );

  @override
  Future<Uint8List> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  ) => _inner.fetchAttachmentBytes(conversationId, attachmentId);
}

/// A thread whose id is a UUID, so the realtime service accepts it.
const Conversation _connected = Conversation(
  id: '11111111-1111-4111-8111-111111111111',
  kind: ConversationKind.customer,
  customerId: 'cus_204',
  customerName: 'Nadeesha Perera',
);

/// The auth port the realtime connect reads its token from.
class _TokenProvider implements AuthTokenProvider {
  @override
  Future<String?> getToken() async => 'token';

  @override
  Future<String?> refreshToken() async => 'token';

  @override
  Future<void> signOut() async {}
}

/// A hub connection that records start/stop and lets a test emit hub events.
class _FakeConnection implements RealtimeConnection {
  final handlers = <String, void Function(List<Object?>?)>{};
  bool started = false;
  bool stopped = false;

  @override
  void on(String method, void Function(List<Object?>? arguments) handler) {
    handlers[method] = handler;
  }

  @override
  Future<void> invoke(String method, List<Object?> arguments) async {}

  @override
  Future<void> start() async {
    started = true;
  }

  @override
  Future<void> stop() async {
    stopped = true;
  }

  @override
  void onReconnected(void Function() handler) {}

  @override
  void onClosed(void Function(Object? error) handler) {}

  void emit(String method, List<Object?>? args) => handlers[method]?.call(args);
}

/// A phone-shaped viewport, so the thread lays out the way it does on the devices
/// this app ships to.
void _usePhoneSurface(WidgetTester tester) {
  tester.view.physicalSize = const Size(1170, 2532);
  tester.view.devicePixelRatio = 3.0;
  addTearDown(tester.view.reset);
}

Widget _wrap(
  Conversation conversation, {
  ThreadRepository? repository,
  int pageSize = 50,
  VoidCallback? onOpenClient,
  ConversationRealtimeService? realtimeService,
  String? boutiqueRole,
  AttachmentPicker? attachmentPicker,
  AttachmentOpener? attachmentOpener,
}) {
  return MaterialApp(
    theme: AppTheme.light,
    home: MultiProvider(
      providers: [
        Provider<AppConfig>.value(
          value: const AppConfig(
            clerkPublishableKey: 'pk_test',
            apiBaseUrl: 'https://api.test',
            jwtTemplateName: 'jwt-aveline-v1',
          ),
        ),
        Provider<AuthTokenProvider>.value(value: _TokenProvider()),
        // The membership row the approval gate reads. No role means no permission, which is
        // what a screen with no provider above it sees.
        ChangeNotifierProvider<BoutiqueProvider>.value(
          value: BoutiqueProvider()
            ..setMembership(boutiqueRole: boutiqueRole),
        ),
      ],
      child: Builder(
        builder: (context) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: ClientThreadScreen(
            conversation: conversation,
            repository: repository ?? _SeedThread(),
            pageSize: pageSize,
            onOpenClient: onOpenClient,
            realtimeService: realtimeService,
            attachmentPicker: attachmentPicker,
            attachmentOpener: attachmentOpener,
          ),
        ),
      ),
    ),
  );
}

Finder _bubble(String id) => find.byKey(Key('thread_message_$id'));
Finder _tick(String state, String id) =>
    find.byKey(Key('thread_tick_${state}_$id'));

Future<void> _open(
  WidgetTester tester, {
  Conversation conversation = _nadeesha,
  ThreadRepository? repository,
  int pageSize = 50,
  VoidCallback? onOpenClient,
  ConversationRealtimeService? realtimeService,
  String? boutiqueRole,
  AttachmentPicker? attachmentPicker,
  AttachmentOpener? attachmentOpener,
}) async {
  _usePhoneSurface(tester);
  await tester.pumpWidget(
    _wrap(
      conversation,
      repository: repository,
      pageSize: pageSize,
      onOpenClient: onOpenClient,
      realtimeService: realtimeService,
      boutiqueRole: boutiqueRole,
      attachmentPicker: attachmentPicker,
      attachmentOpener: attachmentOpener,
    ),
  );
  await tester.pumpAndSettle();
}

void main() {
  group('ClientThreadScreen header', () {
    testWidgets('names the client and the way back to the inbox', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(find.byKey(const Key('thread_client_name')), findsOneWidget);
      expect(
        tester.widget<Text>(find.byKey(const Key('thread_client_name'))).data,
        'Nadeesha Perera',
      );
      expect(find.byType(BackButton), findsOneWidget);
    });

    testWidgets('tapping the client opens the client book', (tester) async {
      var opened = false;
      await _open(
        tester,
        repository: _StubThread(),
        onOpenClient: () => opened = true,
      );

      await tester.tap(find.byKey(const Key('thread_client_name')));
      await tester.pumpAndSettle();

      expect(opened, isTrue);
    });
  });

  group('ClientThreadScreen thread', () {
    testWidgets('reads from the oldest word downwards', (tester) async {
      await _open(tester, repository: _StubThread());

      final first = tester.getTopLeft(_bubble('stub_1')).dy;
      final next = tester.getTopLeft(_bubble('stub_2')).dy;
      final last = tester.getTopLeft(_bubble('stub_4')).dy;

      expect(first, lessThan(next));
      expect(next, lessThan(last));
    });

    testWidgets('opens at the newest word rather than at the top', (tester) async {
      await _open(tester);

      // The list is reversed, so the newest message sits at the foot of the
      // screen: the first thing an associate sees is what was said last.
      final newest = tester.getBottomLeft(_bubble('msg_nd_5')).dy;
      final listBottom = tester
          .getBottomLeft(find.byKey(const Key('thread_scroll')))
          .dy;

      expect(newest, lessThan(listBottom));
      expect(
        _bubble('msg_nd_1'),
        findsNothing,
        reason: 'the oldest message should be above the window',
      );
    });

    testWidgets('puts the client on the left and the boutique on the right', (tester) async {
      await _open(tester, repository: _StubThread());

      // The side is who spoke; the treatment is where it went.
      final theirs = tester.getTopLeft(_bubble('stub_1')).dx;
      final ours = tester.getTopLeft(_bubble('stub_2')).dx;

      expect(theirs, lessThan(ours));
      expect(theirs, lessThan(100));
    });

    testWidgets('puts an agent reply on the counterparty side, credited to its persona', (
      tester,
    ) async {
      await _open(
        tester,
        repository: _SeedThread(
          seed: {
            _nadeesha.id: [
              ThreadMessage(
                id: 'msg_agent_side',
                author: MessageAuthor.agent,
                agentKey: 'ava',
                kind: MessageKind.note,
                status: MessageStatus.published,
                text: 'She last bought evening wear.',
                createdAt: DateTime.now().toUtc(),
              ),
            ],
          },
        ),
      );

      // The wire's author kinds are `User`, `Agent` and `System`: an agent is the
      // counterparty rather than the associate, so its bubble sits on the left the
      // way the client's does. It is also the thread's reply rather than a note to
      // the record, so it must not wear the label a staff note wears.
      expect(tester.getTopLeft(_bubble('msg_agent_side')).dx, lessThan(100));
      expect(find.text('Ava'), findsOneWidget);
      expect(find.textContaining('NOT SENT'), findsNothing);
    });

    testWidgets('marks the note the client never saw', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(find.byKey(const Key('thread_note_stub_3')), findsOneWidget);
      expect(find.textContaining('NOT SENT'), findsOneWidget);
    });

    testWidgets('ticks a message the client has read', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(_tick('read', 'stub_2'), findsOneWidget);
    });

    testWidgets('ticks a message that has arrived but not been opened', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(_tick('delivered', 'stub_4'), findsOneWidget);
      expect(_tick('read', 'stub_4'), findsNothing);
    });

    testWidgets('draws no tick on a note, which went nowhere', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(
        find.descendant(
          of: _bubble('stub_3'),
          matching: find.byIcon(Icons.done_all_rounded),
        ),
        findsNothing,
      );
    });

    testWidgets('opens a day with the day it was', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(find.byKey(const Key('thread_day_separator')), findsOneWidget);
      expect(find.text('Today'), findsOneWidget);
    });
  });

  group('ClientThreadScreen drafts', () {
    testWidgets('offers a staged reply for a decision', (tester) async {
      await _open(tester, conversation: _menaka);

      expect(find.byKey(const Key('thread_draft_msg_mn_3')), findsOneWidget);
      expect(find.textContaining('AWAITING APPROVAL'), findsOneWidget);
      expect(
        find.byKey(const Key('thread_draft_approve_msg_mn_3')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('thread_draft_dismiss_msg_mn_3')),
        findsOneWidget,
      );
    });

    testWidgets('approving releases the reply', (tester) async {
      await _open(tester, conversation: _menaka);

      await tester.tap(find.byKey(const Key('thread_draft_approve_msg_mn_3')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_draft_msg_mn_3')), findsNothing);
      expect(_bubble('msg_mn_3'), findsOneWidget);
      expect(find.textContaining('AWAITING APPROVAL'), findsNothing);
    });

    testWidgets('dismissing keeps it as a decision that was made', (tester) async {
      await _open(tester, conversation: _menaka);

      await tester.tap(find.byKey(const Key('thread_draft_dismiss_msg_mn_3')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_draft_msg_mn_3')), findsNothing);
      expect(find.byKey(const Key('thread_dismissed_msg_mn_3')), findsOneWidget);
      expect(find.textContaining('DISMISSED'), findsOneWidget);
    });
  });

  group('ClientThreadScreen composer', () {
    testWidgets('sends what the associate typed', (tester) async {
      final thread = _StubThread();
      await _open(tester, repository: thread);

      await tester.enterText(
        find.byKey(const Key('thread_composer_field')),
        'See you at ten.',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_send')));
      await tester.pumpAndSettle();

      expect(find.text('See you at ten.'), findsOneWidget);
      expect(thread.sent, ['See you at ten.']);
      expect(
        tester
            .widget<TextField>(find.byKey(const Key('thread_composer_field')))
            .controller!
            .text,
        isEmpty,
      );
    });

    testWidgets('offers to try again when sending is refused', (tester) async {
      final thread = _StubThread()..failSend = true;
      await _open(tester, repository: thread);

      await tester.enterText(
        find.byKey(const Key('thread_composer_field')),
        'On my way.',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_send')));
      await tester.pumpAndSettle();

      expect(find.textContaining('Failed to send'), findsOneWidget);
      expect(find.byKey(const Key('thread_retry_local_1')), findsOneWidget);

      thread.failSend = false;
      await tester.tap(find.byKey(const Key('thread_retry_local_1')));
      await tester.pumpAndSettle();

      expect(find.textContaining('Failed to send'), findsNothing);
      expect(find.text('On my way.'), findsOneWidget);
    });

    testWidgets('will not send into a thread it has not read yet', (tester) async {
      final thread = _DelayedThread(_SeedThread());
      _usePhoneSurface(tester);
      await tester.pumpWidget(_wrap(_nadeesha, repository: thread));
      await tester.pump();

      await tester.enterText(
        find.byKey(const Key('thread_composer_field')),
        'Too soon.',
      );
      await tester.pump();

      expect(
        tester.widget<IconButton>(find.byKey(const Key('thread_send'))).onPressed,
        isNull,
      );

      thread.release.complete();
      await tester.pumpAndSettle();
    });
  });

  group('ClientThreadScreen states', () {
    testWidgets('shows a loading state before the thread lands', (tester) async {
      final thread = _DelayedThread(_SeedThread());
      _usePhoneSurface(tester);
      await tester.pumpWidget(_wrap(_nadeesha, repository: thread));
      await tester.pump();

      expect(find.byKey(const Key('thread_loading')), findsOneWidget);

      thread.release.complete();
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_loading')), findsNothing);
      // The newest message, which is the end of the thread the screen opens on.
      expect(_bubble('msg_nd_5'), findsOneWidget);
    });

    testWidgets('a thread nobody has spoken in says so', (tester) async {
      await _open(tester, conversation: _nobody);

      expect(find.byKey(const Key('thread_empty')), findsOneWidget);
      expect(find.text('No messages yet'), findsOneWidget);
    });

    testWidgets(
      'the empty fallback renders the honest empty state, not a demo seed',
      (tester) async {
        // The registry builds `const ConversationsScreen()` with nothing
        // injected, so this is what a production path reaches if the wiring
        // regresses. It must never be eight invented exchanges.
        await _open(tester, repository: const EmptyThreadRepository());

        expect(find.byKey(const Key('thread_empty')), findsOneWidget);
        expect(_bubble('msg_nd_1'), findsNothing);
        expect(_bubble('msg_nd_5'), findsNothing);
      },
    );

    testWidgets('a failed read offers a retry that works', (tester) async {
      final thread = _FailingThread(_SeedThread());
      await _open(tester, repository: thread);

      expect(find.byKey(const Key('thread_error')), findsOneWidget);
      expect(find.textContaining('unavailable'), findsOneWidget);

      thread.fail = false;
      await tester.tap(find.byKey(const Key('thread_retry_load')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_error')), findsNothing);
      expect(_bubble('msg_nd_5'), findsOneWidget);
    });

    testWidgets('offers the history above the window', (tester) async {
      await _open(tester, pageSize: 2);

      expect(find.byKey(const Key('thread_load_earlier')), findsOneWidget);

      await tester.tap(find.byKey(const Key('thread_load_earlier')));
      await tester.pumpAndSettle();

      // Page two of a five-message thread at two a page.
      expect(_bubble('msg_nd_4'), findsOneWidget);
    });

    testWidgets('a thread that fits in one page offers no history', (tester) async {
      await _open(tester, repository: _StubThread());

      expect(find.byKey(const Key('thread_load_earlier')), findsNothing);
    });
  });

  group('ClientThreadScreen briefing blocks', () {
    DateTime age([int minutes = 5]) =>
        DateTime.now().toUtc().subtract(Duration(minutes: minutes));

    _SeedThread seeded(ThreadMessage message) =>
        _SeedThread(seed: {_nadeesha.id: [message]});

    testWidgets('a client_message shows the client\u2019s words and channel handle', (tester) async {
      // The block's own field is `text` and `from` is the handle: a thread that read only
      // the first `text` block drew this as an empty bubble.
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_c1',
            author: MessageAuthor.client,
            kind: MessageKind.clientMessage,
            text: 'Is it ready?',
            createdAt: age(),
            clientMessageFrom: 'whatsapp:+94771234567',
            blocks: const [
              ThreadBlock('client_message', {
                'type': 'client_message',
                'from': 'whatsapp:+94771234567',
                'text': 'Is it ready?',
              }),
            ],
          ),
        ),
      );

      expect(find.text('Is it ready?'), findsOneWidget);
      expect(
        find.byKey(const Key('thread_client_handle_msg_c1')),
        findsOneWidget,
      );
      expect(find.text('whatsapp:+94771234567'), findsOneWidget);
    });

    testWidgets('a piece-only message is never an empty bubble', (tester) async {
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_p1',
            author: MessageAuthor.agent,
            kind: MessageKind.piece,
            text: '',
            createdAt: age(),
            blocks: const [
              ThreadBlock('piece', {'type': 'piece', 'name': 'Silk Slip Dress', 'price': 24000}),
            ],
          ),
        ),
      );

      expect(
        find.byKey(const Key('thread_block_msg_p1_piece')),
        findsOneWidget,
      );
      expect(find.textContaining('Silk Slip Dress'), findsOneWidget);
    });

    testWidgets('a courier-only message is never an empty bubble', (tester) async {
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_cr1',
            author: MessageAuthor.agent,
            kind: MessageKind.courier,
            text: '',
            createdAt: age(),
            blocks: const [
              ThreadBlock('courier', {'type': 'courier', 'carrier': 'Pronto', 'status': 'in transit'}),
            ],
          ),
        ),
      );

      expect(
        find.byKey(const Key('thread_block_msg_cr1_courier')),
        findsOneWidget,
      );
      expect(find.textContaining('Pronto'), findsOneWidget);
    });

    testWidgets('an unknown block type still says something', (tester) async {
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_u1',
            author: MessageAuthor.agent,
            kind: MessageKind.unknown,
            text: '',
            createdAt: age(),
            blocks: const [
              ThreadBlock('hologram', {'type': 'hologram'}),
            ],
          ),
        ),
      );

      expect(
        find.byKey(const Key('thread_block_msg_u1_hologram')),
        findsOneWidget,
      );
      expect(find.text('Update'), findsOneWidget);
    });

    testWidgets('a suggestion shows the draft and copies it', (tester) async {
      final calls = <MethodCall>[];
      final messenger = tester.binding.defaultBinaryMessenger;
      messenger.setMockMethodCallHandler(SystemChannels.platform, (call) async {
        calls.add(call);
        return null;
      });
      addTearDown(
        () => messenger.setMockMethodCallHandler(SystemChannels.platform, null),
      );

      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_s1',
            author: MessageAuthor.agent,
            kind: MessageKind.suggestion,
            agentKey: 'ava',
            text: 'Draft body',
            createdAt: age(),
            blocks: const [
              ThreadBlock('suggestion', {'type': 'suggestion', 'text': 'Draft body'}),
            ],
          ),
        ),
      );

      // A suggestion-only message must not be reported as plain text; it is a card.
      expect(find.byKey(const Key('thread_suggestion_msg_s1')), findsOneWidget);

      await tester.tap(
        find.byKey(const Key('thread_suggestion_copy_msg_s1')),
      );
      await tester.pumpAndSettle();

      final copied = calls.firstWhere(
        (call) => call.method == 'Clipboard.setData',
      );
      expect((copied.arguments as Map)['text'], 'Draft body');
    });

    testWidgets('a choice resolves the client through select-customer', (tester) async {
      final thread = seeded(
        ThreadMessage(
          id: 'msg_ch1',
          author: MessageAuthor.agent,
          kind: MessageKind.note,
          text: '',
          createdAt: age(),
          blocks: const [
            ThreadBlock('choice', {
              'type': 'choice',
              'prompt': 'Which one did you mean?',
              'options': [
                {'customerId': 'cus_1', 'fullName': 'Nadeesha Perera', 'status': 'active'},
                {'customerId': 'cus_2', 'fullName': 'Nadeesha Silva', 'status': 'new'},
              ],
            }),
          ],
        ),
      );

      await _open(tester, repository: thread);

      expect(find.byKey(const Key('thread_choice_msg_ch1')), findsOneWidget);
      expect(find.text('Which one did you mean?'), findsOneWidget);

      await tester.tap(
        find.byKey(const Key('thread_choice_msg_ch1_cus_1')),
      );
      await tester.pumpAndSettle();

      expect(thread.selectedCustomers, ['cus_1']);
    });

    testWidgets('an approved SignOff reads APPROVED rather than a note', (tester) async {
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_so1',
            author: MessageAuthor.agent,
            kind: MessageKind.signOff,
            status: MessageStatus.published,
            text: '',
            contentHash: 'hash1',
            createdAt: age(),
            blocks: const [
              ThreadBlock('sign_off', {'type': 'sign_off', 'amount': 48000, 'reason': 'above the discretionary limit'}),
            ],
          ),
        ),
      );

      expect(find.byKey(const Key('thread_approved_msg_so1')), findsOneWidget);
      expect(find.textContaining('APPROVED'), findsOneWidget);
      expect(find.textContaining('NOT SENT'), findsNothing);
      // The sign-off's own words stand in for the text the message does not have.
      expect(find.text('above the discretionary limit'), findsOneWidget);
    });

    testWidgets('a dismissed SignOff reads DISMISSED rather than a note', (tester) async {
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_so2',
            author: MessageAuthor.agent,
            kind: MessageKind.signOff,
            status: MessageStatus.cancelled,
            text: 'Shall I confirm the fitting?',
            createdAt: age(),
          ),
        ),
      );

      expect(find.byKey(const Key('thread_dismissed_msg_so2')), findsOneWidget);
      expect(find.textContaining('DISMISSED'), findsOneWidget);
      expect(find.textContaining('NOT SENT'), findsNothing);
    });

    testWidgets('a staff note is still labelled NOT SENT', (tester) async {
      // D2 = A pins this: the composer records what the boutique decided and reaches no
      // customer channel, so `NOTE · NOT SENT` is the honest label.
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_n1',
            author: MessageAuthor.staff,
            kind: MessageKind.note,
            status: MessageStatus.published,
            text: 'A note to the record.',
            createdAt: age(),
          ),
        ),
      );

      expect(find.byKey(const Key('thread_note_msg_n1')), findsOneWidget);
      expect(find.text('NOTE · NOT SENT'), findsOneWidget);
    });

    testWidgets('a reply with an in-window parent draws a quoted line', (tester) async {
      final parent = ThreadMessage(
        id: 'msg_parent',
        author: MessageAuthor.client,
        kind: MessageKind.clientMessage,
        text: 'Is it ready?',
        createdAt: age(10),
        clientMessageFrom: 'whatsapp:+94771234567',
      );
      await _open(
        tester,
        repository: _SeedThread(
          seed: {
            _nadeesha.id: [
              parent,
              ThreadMessage(
                id: 'msg_reply',
                author: MessageAuthor.staff,
                kind: MessageKind.note,
                status: MessageStatus.sent,
                text: 'Almost.',
                replyToMessageId: 'msg_parent',
                createdAt: age(5),
              ),
            ],
          },
        ),
      );

      expect(find.byKey(const Key('thread_quote_msg_reply')), findsOneWidget);
      expect(find.textContaining('Is it ready?'), findsWidgets);
    });

    testWidgets('a reply whose parent is outside the window omits the quote', (tester) async {
      await _open(
        tester,
        repository: seeded(
          ThreadMessage(
            id: 'msg_orphan',
            author: MessageAuthor.staff,
            kind: MessageKind.note,
            status: MessageStatus.sent,
            text: 'As discussed.',
            replyToMessageId: 'msg_not_in_window',
            createdAt: age(),
          ),
        ),
      );

      expect(find.byKey(const Key('thread_quote_msg_orphan')), findsNothing);
      expect(find.text('As discussed.'), findsOneWidget);
    });
  });

  group('ClientThreadScreen sign-off oversight', () {
    DateTime age([int minutes = 5]) =>
        DateTime.now().toUtc().subtract(Duration(minutes: minutes));

    ThreadMessage approvedSignOff(String id) => ThreadMessage(
      id: id,
      author: MessageAuthor.agent,
      kind: MessageKind.signOff,
      status: MessageStatus.published,
      text: '',
      contentHash: 'hash1',
      createdAt: age(),
      blocks: const [
        ThreadBlock('sign_off', {'type': 'sign_off', 'amount': 48000}),
      ],
    );

    _SeedThread seeded(String id) =>
        _SeedThread(seed: {_nadeesha.id: [approvedSignOff(id)]});

    testWidgets('offers Revoke only to a caller who may approve', (tester) async {
      // An ordinary staff membership holds conversations:view but not approvals:approve, so
      // the action is not drawn for them. The API refuses them anyway; this is only about not
      // offering a decision they cannot make.
      await _open(tester, repository: seeded('msg_so3'));
      expect(find.byKey(const Key('thread_approved_msg_so3')), findsOneWidget);
      expect(find.byKey(const Key('thread_revoke_msg_so3')), findsNothing);

      await _open(
        tester,
        repository: seeded('msg_so3'),
        boutiqueRole: 'org:boutique_staff',
      );
      expect(find.byKey(const Key('thread_revoke_msg_so3')), findsNothing);
    });

    testWidgets('a supervisor can take an approval back', (tester) async {
      await _open(
        tester,
        repository: seeded('msg_so4'),
        boutiqueRole: 'org:boutique_supervisor',
      );

      expect(find.byKey(const Key('thread_revoke_msg_so4')), findsOneWidget);

      await tester.tap(find.byKey(const Key('thread_revoke_msg_so4')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_approved_msg_so4')), findsNothing);
      expect(find.byKey(const Key('thread_draft_msg_so4')), findsOneWidget);
      expect(find.textContaining('AWAITING APPROVAL'), findsOneWidget);
    });
  });

  group('ClientThreadScreen attachments', () {
    DateTime age([int minutes = 5]) =>
        DateTime.now().toUtc().subtract(Duration(minutes: minutes));

    ThreadBlock attachmentBlock({
      required String id,
      String contentType = 'image/png',
      String fileName = 'photo.png',
      int sizeBytes = 2048,
    }) => ThreadBlock('attachment', {
      'type': 'attachment',
      'attachmentId': id,
      'url': '/api/v1/orgs/org/conversations/cnv/attachments/$id',
      'contentType': contentType,
      'fileName': fileName,
      'sizeBytes': sizeBytes,
    });

    testWidgets('an image renders a thumbnail that opens full screen', (tester) async {
      await _open(
        tester,
        repository: _SeedThread(seed: {
          _nadeesha.id: [
            ThreadMessage(
              id: 'msg_img',
              author: MessageAuthor.staff,
              status: MessageStatus.sent,
              text: 'Here it is.',
              createdAt: age(),
              blocks: [attachmentBlock(id: 'att_1')],
            ),
          ],
        }),
      );

      final thumbnail = find.byKey(const Key('thread_attachment_msg_img_att_1'));
      expect(thumbnail, findsOneWidget);

      await tester.tap(thumbnail);
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_attachment_viewer')), findsOneWidget);
      expect(find.byType(InteractiveViewer), findsOneWidget);
    });

    testWidgets('a document renders a chip with its name and size', (tester) async {
      await _open(
        tester,
        repository: _SeedThread(seed: {
          _nadeesha.id: [
            ThreadMessage(
              id: 'msg_pdf',
              author: MessageAuthor.staff,
              status: MessageStatus.sent,
              text: 'The invoice.',
              createdAt: age(),
              blocks: [
                attachmentBlock(
                  id: 'att_2',
                  contentType: 'application/pdf',
                  fileName: 'invoice.pdf',
                  sizeBytes: 2048,
                ),
              ],
            ),
          ],
        }),
      );

      expect(
        find.byKey(const Key('thread_attachment_msg_pdf_att_2')),
        findsOneWidget,
      );
      expect(find.text('invoice.pdf'), findsOneWidget);
      expect(find.text('2 KB'), findsOneWidget);
    });

    testWidgets('a document opens through the platform viewer', (tester) async {
      // A PDF cannot be previewed in-process, so the chip hands the bytes to the platform.
      final opened = <({int length, String name})>[];

      await _open(
        tester,
        repository: _SeedThread(seed: {
          _nadeesha.id: [
            ThreadMessage(
              id: 'msg_doc',
              author: MessageAuthor.staff,
              status: MessageStatus.sent,
              text: 'The invoice.',
              createdAt: age(),
              blocks: [
                attachmentBlock(
                  id: 'att_doc',
                  contentType: 'application/pdf',
                  fileName: 'invoice.pdf',
                  sizeBytes: 2048,
                ),
              ],
            ),
          ],
        }),
        attachmentOpener: (bytes, fileName) async {
          opened.add((length: bytes.length, name: fileName));
        },
      );

      await tester.tap(find.byKey(const Key('thread_attachment_msg_doc_att_doc')));
      await tester.pumpAndSettle();

      expect(opened.single.name, 'invoice.pdf');
      expect(opened.single.length, _tinyPng.length);
    });

    testWidgets('the paperclip picks from the gallery and the tray sends it', (tester) async {
      final thread = _SeedThread(seed: {_nadeesha.id: []});
      var picked = 0;

      await _open(
        tester,
        repository: thread,
        attachmentPicker: (source) async {
          picked++;
          expect(source, AttachmentSource.gallery);
          return [
            PickedAttachment(
              bytes: Uint8List.fromList(_tinyPng),
              contentType: 'image/png',
              fileName: 'photo.png',
            ),
          ];
        },
      );

      await tester.tap(find.byKey(const Key('thread_attach')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('thread_attach_gallery')), findsOneWidget);

      await tester.tap(find.byKey(const Key('thread_attach_gallery')));
      await tester.pumpAndSettle();

      expect(picked, 1);
      expect(thread.uploaded, ['att_1']);
      expect(find.byKey(const Key('thread_attachment_tray')), findsOneWidget);
      expect(find.byKey(const Key('thread_pending_local_att_1')), findsOneWidget);

      await tester.enterText(
        find.byKey(const Key('thread_composer_field')),
        'Here it is.',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_send')));
      await tester.pumpAndSettle();

      expect(thread.sentAttachments.single, ['att_1']);
      expect(find.byKey(const Key('thread_attachment_tray')), findsNothing);
    });

    testWidgets('a held file can be removed before sending', (tester) async {
      final thread = _SeedThread(seed: {_nadeesha.id: []});

      await _open(
        tester,
        repository: thread,
        attachmentPicker: (source) async => [
          PickedAttachment(
            bytes: Uint8List.fromList(_tinyPng),
            contentType: 'image/png',
            fileName: 'photo.png',
          ),
        ],
      );

      await tester.tap(find.byKey(const Key('thread_attach')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_attach_gallery')));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('thread_remove_local_att_1')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_attachment_tray')), findsNothing);

      await tester.enterText(
        find.byKey(const Key('thread_composer_field')),
        'Text only.',
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_send')));
      await tester.pumpAndSettle();

      expect(thread.sentAttachments.single, isEmpty);
    });

    testWidgets('a picker that fails says so on screen, not only in the log', (
      tester,
    ) async {
      await _open(
        tester,
        repository: _SeedThread(seed: {_nadeesha.id: []}),
        attachmentPicker: (source) async =>
            throw StateError('the photo library refused'),
      );

      await tester.tap(find.byKey(const Key('thread_attach')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_attach_gallery')));
      await tester.pumpAndSettle();

      // The exception used to reach `debugPrint` alone; the associate now sees an outcome.
      expect(find.text('That file could not be attached.'), findsOneWidget);
    });

    testWidgets('a sixth file is refused in the API\u2019s words and never uploaded', (
      tester,
    ) async {
      final thread = _SeedThread(seed: {_nadeesha.id: []});
      var picks = 0;

      List<PickedAttachment> batch(int count) => [
        for (var n = 1; n <= count; n++)
          PickedAttachment(
            bytes: Uint8List.fromList(_tinyPng),
            contentType: 'image/png',
            fileName: 'photo_$n.png',
          ),
      ];

      await _open(
        tester,
        repository: thread,
        attachmentPicker: (source) async => batch(++picks == 1 ? 5 : 1),
      );

      Future<void> pickAgain() async {
        await tester.tap(find.byKey(const Key('thread_attach')));
        await tester.pumpAndSettle();
        await tester.tap(find.byKey(const Key('thread_attach_gallery')));
        await tester.pumpAndSettle();
      }

      // Five fit under the cap and are stored.
      await pickAgain();
      expect(thread.uploaded, hasLength(5));

      // The sixth is refused before a byte is uploaded, with the server's own wording.
      await pickAgain();
      expect(thread.uploaded, hasLength(5));
      expect(
        find.text('A message may carry at most 5 attachments.'),
        findsOneWidget,
      );
    });

    testWidgets('a failed upload is readable in the tray and can be retried', (
      tester,
    ) async {
      final thread = _SeedThread(seed: {_nadeesha.id: []})..failUpload = true;

      await _open(
        tester,
        repository: thread,
        attachmentPicker: (source) async => [
          PickedAttachment(
            bytes: Uint8List.fromList(_tinyPng),
            contentType: 'image/png',
            fileName: 'photo.png',
          ),
        ],
      );

      await tester.tap(find.byKey(const Key('thread_attach')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('thread_attach_gallery')));
      await tester.pumpAndSettle();

      // The red border and retry icon stay; the reason is now readable too.
      expect(find.byKey(const Key('thread_attachment_error')), findsOneWidget);
      expect(find.text('Could not upload photo.png.'), findsOneWidget);

      thread.failUpload = false;
      await tester.tap(
        find.byKey(const Key('thread_retry_upload_local_att_1')),
      );
      await tester.pumpAndSettle();

      expect(thread.uploaded, ['att_1']);
      expect(find.byKey(const Key('thread_attachment_error')), findsNothing);
    });
  });

  group('ClientThreadScreen realtime', () {
    ({_FakeConnection connection, ConversationRealtimeService service}) hub() {
      final connection = _FakeConnection();
      final service = ConversationRealtimeService(
        ({required url, required accessTokenFactory}) => connection,
      );
      return (connection: connection, service: service);
    }

    testWidgets('subscribes on open and leaves on dispose', (tester) async {
      final fake = hub();

      await _open(
        tester,
        conversation: _connected,
        repository: _StubThread(),
        realtimeService: fake.service,
      );

      expect(fake.connection.started, isTrue);
      expect(fake.connection.handlers.containsKey('ReceiveMessage'), isTrue);

      await tester.pumpWidget(const SizedBox());
      await tester.pumpAndSettle();

      expect(fake.connection.stopped, isTrue);
    });

    testWidgets('a message that arrives while the thread is open appears', (tester) async {
      final fake = hub();
      await _open(
        tester,
        conversation: _connected,
        repository: _StubThread(),
        realtimeService: fake.service,
      );

      fake.connection.emit('ReceiveMessage', [
        {
          'id': '99999999-9999-4999-8999-999999999999',
          'authorKind': 'System',
          'kind': 'ClientMessage',
          'status': 'Published',
          'contentBlocks': [
            {
              'type': 'client_message',
              'from': 'whatsapp:+94770000000',
              'text': 'Any news?',
            },
          ],
          'createdAt': '2099-01-01T00:00:00Z',
        },
      ]);
      await tester.pumpAndSettle();

      expect(
        _bubble('99999999-9999-4999-8999-999999999999'),
        findsOneWidget,
      );
      expect(find.text('Any news?'), findsOneWidget);
    });

    testWidgets('says who is working, and only while the payload supports it', (tester) async {
      final fake = hub();
      await _open(
        tester,
        conversation: _connected,
        repository: _StubThread(),
        realtimeService: fake.service,
      );

      expect(find.byKey(const Key('thread_agent_activity')), findsNothing);

      fake.connection.emit('ReceiveAgentState', [
        {'conversationId': _connected.id, 'state': 'searching', 'agentKey': 'ava'},
      ]);
      await tester.pump();

      expect(find.byKey(const Key('thread_agent_activity')), findsOneWidget);
      expect(find.textContaining('Ava is searching'), findsOneWidget);

      fake.connection.emit('ReceiveAgentState', [
        {'conversationId': _connected.id, 'state': 'tool_call', 'agentKey': 'elle'},
      ]);
      await tester.pump();
      expect(find.textContaining('Elle is using a tool'), findsOneWidget);

      // A terminal state settles rather than hanging a stale card.
      fake.connection.emit('ReceiveAgentState', [
        {'conversationId': _connected.id, 'state': 'idle', 'agentKey': 'ava'},
      ]);
      await tester.pump();
      expect(find.byKey(const Key('thread_agent_activity')), findsNothing);

      // A malformed payload is dropped by the service, so it does not read as idle either.
      fake.connection.emit('ReceiveAgentState', [
        {'conversationId': _connected.id},
      ]);
      await tester.pump();
      expect(find.byKey(const Key('thread_agent_activity')), findsNothing);
    });
  });
}
