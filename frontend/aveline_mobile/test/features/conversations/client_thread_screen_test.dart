import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/conversations/data/demo_thread_repository.dart';
import 'package:aveline_mobile/features/conversations/data/thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/screens/client_thread_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

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

final DateTime _now = DateTime.utc(2026, 9, 18, 12);

/// The threads the screen is driven against.
///
/// The demo repository, measured from the wall clock: a row prints its age against
/// `DateTime.now()`, so a pinned seed clock would date every message to 2026 and
/// hide the day separator.
DemoThreadRepository _threads({Duration latency = Duration.zero}) =>
    DemoThreadRepository(clock: DateTime.now, latency: latency);

/// A short thread with very short words.
///
/// Position is what the layout tests read, and under the test font every glyph is
/// a square, so a realistic message is several times taller than it is on a
/// device and the whole thread will not fit one viewport. These four fit, which
/// lets a test compare where things actually landed.
class _StubThread implements ThreadRepository {
  _StubThread();

  List<ThreadMessage> items = [
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

  final List<String> sent = [];
  bool failSend = false;

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
  }) async {
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
  Future<ThreadMessage> sendMessage(String conversationId, String text) async {
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
    );
    items = [...items, stored];
    return stored;
  }

  @override
  Future<void> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async {}
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
  }) async {
    if (fail) {
      throw Exception('The thread is unavailable.');
    }
    return _inner.fetchMessages(conversationId, page: page, pageSize: pageSize);
  }

  @override
  Future<ThreadMessage> sendMessage(String conversationId, String text) =>
      _inner.sendMessage(conversationId, text);

  @override
  Future<void> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) => _inner.decideSignOff(
    conversationId: conversationId,
    message: message,
    approved: approved,
  );
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
}) {
  return MaterialApp(
    theme: AppTheme.light,
    home: Builder(
      builder: (context) => MediaQuery(
        data: MediaQuery.of(context).copyWith(disableAnimations: true),
        child: ClientThreadScreen(
          conversation: conversation,
          repository: repository ?? _threads(),
          pageSize: pageSize,
          onOpenClient: onOpenClient,
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
}) async {
  _usePhoneSurface(tester);
  await tester.pumpWidget(
    _wrap(
      conversation,
      repository: repository,
      pageSize: pageSize,
      onOpenClient: onOpenClient,
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
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(
          _nadeesha,
          repository: _threads(latency: const Duration(milliseconds: 200)),
        ),
      );
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

      // Let the read land before the test ends: a timer left pending is a failure
      // in its own right.
      await tester.pump(const Duration(milliseconds: 300));
      await tester.pumpAndSettle();
    });
  });

  group('ClientThreadScreen states', () {
    testWidgets('shows a loading state before the thread lands', (tester) async {
      _usePhoneSurface(tester);
      await tester.pumpWidget(
        _wrap(
          _nadeesha,
          repository: _threads(latency: const Duration(milliseconds: 200)),
        ),
      );
      await tester.pump();

      expect(find.byKey(const Key('thread_loading')), findsOneWidget);

      await tester.pump(const Duration(milliseconds: 300));
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

    testWidgets('a failed read offers a retry that works', (tester) async {
      final thread = _FailingThread(_threads());
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
}
