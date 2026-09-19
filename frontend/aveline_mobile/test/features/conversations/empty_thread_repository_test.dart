import 'package:aveline_mobile/features/conversations/data/empty_thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:flutter_test/flutter_test.dart';

/// The stand-in the inbox falls back to when no thread source was injected.
///
/// It exists so that no production path can reach the demo seed: the drawer
/// builds `const ConversationsScreen()` with no way to inject anything, so the
/// fallback is what a real associate would see if the wiring regressed.
void main() {
  group('EmptyThreadRepository', () {
    test('serves an empty page rather than an invented thread', () async {
      const repository = EmptyThreadRepository();

      final page = await repository.fetchMessages('cnv_any');

      expect(page.items, isEmpty);
      expect(page.total, 0);
      expect(page.pageSize, 0);
    });

    test('accepts the paging arguments the contract names', () async {
      const repository = EmptyThreadRepository();

      final page = await repository.fetchMessages(
        'cnv_any',
        page: 3,
        pageSize: 20,
      );

      expect(page.items, isEmpty);
      expect(page.page, 3);
    });

    test('refuses a send with something the caller can read', () async {
      const repository = EmptyThreadRepository();

      await expectLater(
        repository.sendMessage('cnv_any', 'Hello.'),
        throwsA(
          isA<StateError>().having(
            (error) => error.message,
            'message',
            contains('no thread source'),
          ),
        ),
      );
    });

    test('refuses a sign-off decision with something the caller can read', () async {
      const repository = EmptyThreadRepository();

      await expectLater(
        repository.decideSignOff(
          conversationId: 'cnv_any',
          message: ThreadMessage(
            id: 'msg_1',
            author: MessageAuthor.agent,
            text: 'Shall I?',
            createdAt: DateTime.utc(2026, 9, 18),
          ),
          approved: true,
        ),
        throwsA(isA<StateError>()),
      );
    });
  });
}
