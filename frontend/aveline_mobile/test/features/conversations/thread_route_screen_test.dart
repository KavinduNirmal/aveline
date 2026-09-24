import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/conversations/data/conversation_repository.dart';
import 'package:aveline_mobile/features/conversations/data/thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/conversation.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:aveline_mobile/features/conversations/presentation/screens/thread_route_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

const Conversation _nadeesha = Conversation(
  id: 'cnv_nadeesha',
  kind: ConversationKind.customer,
  customerId: 'cus_204',
  customerName: 'Nadeesha Perera',
);

/// An inbox whose one thread can appear, be absent, or be waiting for the org id.
class _RouteInbox implements ConversationRepository {
  _RouteInbox({this.conversation, this.waitingForOrg = false});

  Conversation? conversation;
  bool waitingForOrg;

  @override
  Future<ConversationPage> fetchConversations({int page = 1}) async =>
      ConversationPage.empty;

  @override
  Future<Conversation?> fetchConversation(String id) async {
    if (waitingForOrg) {
      throw const OrgContextUnavailable();
    }
    return conversation;
  }
}

/// A thread that records how it was asked to open.
class _RouteThread implements ThreadRepository {
  final List<String?> requestedAround = [];

  @override
  Future<ThreadPage> fetchMessages(
    String conversationId, {
    int page = 1,
    int pageSize = 50,
    String? around,
  }) async {
    requestedAround.add(around);
    return ThreadPage(
      items: [
        ThreadMessage(
          id: 'msg_1',
          author: MessageAuthor.staff,
          status: MessageStatus.sent,
          text: 'Here it is.',
          createdAt: DateTime.now().toUtc().subtract(const Duration(minutes: 5)),
        ),
      ],
      total: 1,
      page: 1,
      pageSize: pageSize,
    );
  }

  @override
  Future<ThreadMessage> sendMessage(
    String conversationId,
    String text, {
    String? clientMessageId,
    List<String> attachmentIds = const [],
  }) async => throw StateError('not used');

  @override
  Future<ThreadMessage> decideSignOff({
    required String conversationId,
    required ThreadMessage message,
    required bool approved,
  }) async => throw StateError('not used');

  @override
  Future<void> selectCustomer(
    String conversationId,
    String customerId, {
    String? query,
  }) async {}

  @override
  Future<void> markRead(String conversationId, String lastReadMessageId) async {}

  @override
  Future<ThreadMessage> revokeSignOff(
    String conversationId,
    String messageId, {
    String? reason,
  }) async => throw StateError('not used');

  @override
  Future<Never> uploadAttachment(
    String conversationId, {
    required dynamic bytes,
    required String contentType,
    required String fileName,
    int? width,
    int? height,
  }) async => throw StateError('not used');

  @override
  Future<Never> fetchAttachmentBytes(
    String conversationId,
    String attachmentId,
  ) async => throw StateError('not used');

  @override
  Future<Never> deliver(
    String conversationId,
    String text, {
    String? clientMessageId,
  }) async => throw StateError('not used');

  @override
  Future<void> regenerate(String conversationId, String messageId) async =>
      throw StateError('not used');
}

Widget _wrap(
  ConversationRepository conversations,
  ThreadRepository thread, {
  String conversationId = 'cnv_nadeesha',
  String? messageId,
}) => MaterialApp(
  theme: AppTheme.light,
  // Reduced motion, matching the rest of the suite: a progress indicator animates forever, so
  // `pumpAndSettle` would never settle while one is on screen.
  home: MediaQuery(
    data: const MediaQueryData(
      disableAnimations: true,
      size: Size(390, 844),
    ),
    child: ThreadRouteScreen(
      conversationId: conversationId,
      messageId: messageId,
      conversationRepository: conversations,
      threadRepository: thread,
    ),
  ),
);

void main() {
  group('ThreadRouteScreen', () {
    testWidgets('opens the thread once the conversation resolves', (tester) async {
      final thread = _RouteThread();
      await tester.pumpWidget(_wrap(_RouteInbox(conversation: _nadeesha), thread));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_client_name')), findsOneWidget);
      expect(find.text('Nadeesha Perera'), findsOneWidget);
      expect(thread.requestedAround, [null]);
    });

    testWidgets('anchors the thread to the message a notification named', (tester) async {
      final thread = _RouteThread();
      await tester.pumpWidget(
        _wrap(
          _RouteInbox(conversation: _nadeesha),
          thread,
          messageId: 'msg_deep',
        ),
      );
      await tester.pumpAndSettle();

      expect(thread.requestedAround, ['msg_deep']);
    });

    testWidgets('a thread that is gone says so, and the retry works', (tester) async {
      final inbox = _RouteInbox();
      await tester.pumpWidget(_wrap(inbox, _RouteThread()));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_route_not_found')), findsOneWidget);

      inbox.conversation = _nadeesha;
      await tester.tap(find.byKey(const Key('thread_route_retry')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_client_name')), findsOneWidget);
    });

    testWidgets('a missing organization id is a "not yet", not a refusal', (tester) async {
      final inbox = _RouteInbox(waitingForOrg: true);
      await tester.pumpWidget(_wrap(inbox, _RouteThread()));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_route_waiting')), findsOneWidget);
      expect(find.byKey(const Key('thread_route_not_found')), findsNothing);

      inbox.waitingForOrg = false;
      inbox.conversation = _nadeesha;
      await tester.tap(find.byKey(const Key('thread_route_retry')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('thread_client_name')), findsOneWidget);
    });
  });
}
