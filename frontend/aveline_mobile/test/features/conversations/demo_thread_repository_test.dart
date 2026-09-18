import 'package:aveline_mobile/features/conversations/data/demo_thread_repository.dart';
import 'package:aveline_mobile/features/conversations/domain/thread_message.dart';
import 'package:flutter_test/flutter_test.dart';

final DateTime _now = DateTime.utc(2026, 9, 18, 12);

DemoThreadRepository _threads() =>
    DemoThreadRepository(clock: () => _now, latency: Duration.zero);

void main() {
  group('DemoThreadRepository.fetchMessages', () {
    test('serves a thread oldest first, so it reads downwards', () async {
      final page = await _threads().fetchMessages('cnv_nadeesha');

      expect(page.items, isNotEmpty);
      final times = page.items.map((item) => item.createdAt).toList();
      expect(times, [...times]..sort());
    });

    test('holds both sides of the conversation', () async {
      final page = await _threads().fetchMessages('cnv_nadeesha');

      expect(page.items.any((item) => item.isFromClient), isTrue);
      expect(page.items.any((item) => item.isFromBoutique), isTrue);
    });

    test('holds a note the client never saw', () async {
      // The distinction the thread has to draw: an internal note and a message
      // that went out look nothing alike on the wire (Published vs Sent).
      final page = await _threads().fetchMessages('cnv_nadeesha');

      expect(page.items.any((item) => item.isInternalNote), isTrue);
    });

    test('holds a reply staged for a signature, with its hash', () async {
      final page = await _threads().fetchMessages('cnv_menaka');

      final draft = page.items.firstWhere(
        (item) => item.needsSignOff,
        orElse: () => throw StateError('no draft'),
      );
      expect(draft.contentHash, isNotNull);
      expect(draft.contentHash, isNotEmpty);
    });

    test('leaves some sent messages with their delivery ticks', () async {
      final page = await _threads().fetchMessages('cnv_chathurika');

      final sent = page.items.where(
        (item) => item.isFromBoutique && !item.isInternalNote,
      );
      expect(sent, isNotEmpty);
      expect(sent.any((item) => item.isDelivered), isTrue);
    });

    test('a conversation it has nothing for is an empty thread', () async {
      final page = await _threads().fetchMessages('cnv_nobody');

      expect(page.items, isEmpty);
      expect(page.total, 0);
      expect(page.page, 1);
    });

    test('files a long thread over more than one page', () async {
      final first = await _threads().fetchMessages('cnv_nadeesha', pageSize: 2);

      expect(first.items, hasLength(2));
      expect(first.total, greaterThan(2));
      expect(first.page, 1);
    });

    test('a later page continues where the one before it stopped', () async {
      final threads = _threads();

      final first = await threads.fetchMessages('cnv_nadeesha', pageSize: 2);
      final second = await threads.fetchMessages(
        'cnv_nadeesha',
        page: 2,
        pageSize: 2,
      );

      expect(
        second.items.map((item) => item.id),
        isNot(contains(first.items.first.id)),
      );
      expect(second.page, 2);
    });

    test('a page past the end is empty rather than an error', () async {
      final page = await _threads().fetchMessages('cnv_nadeesha', page: 99);

      expect(page.items, isEmpty);
    });

    test('measures its ages from the injected clock', () async {
      final page = await _threads().fetchMessages('cnv_nadeesha');

      expect(page.items.last.createdAt.isBefore(_now), isTrue);
    });

    test('hands back a list a caller cannot mutate under the next one', () async {
      final page = await _threads().fetchMessages('cnv_nadeesha');

      expect(
        () => page.items.add(
          ThreadMessage(
            id: 'x',
            author: MessageAuthor.staff,
            text: 'x',
            createdAt: _now,
          ),
        ),
        throwsUnsupportedError,
      );
    });
  });

  group('DemoThreadRepository.sendMessage', () {
    test('stores what was sent and hands it back confirmed', () async {
      final threads = _threads();
      final before = await threads.fetchMessages('cnv_nadeesha');

      final sent = await threads.sendMessage('cnv_nadeesha', 'On my way.');

      expect(sent.text, 'On my way.');
      expect(sent.author, MessageAuthor.staff);
      expect(sent.deliveryStatus, isNull);
      expect(sent.isFromBoutique, isTrue);

      final after = await threads.fetchMessages('cnv_nadeesha');
      expect(after.total, before.total + 1);
      expect(after.items.last.text, 'On my way.');
    });

    test('sending into a thread it has nothing for starts one', () async {
      final threads = _threads();

      await threads.sendMessage('cnv_nobody', 'Hello there.');

      final page = await threads.fetchMessages('cnv_nobody');
      expect(page.items, hasLength(1));
      expect(page.items.single.text, 'Hello there.');
    });
  });

  group('DemoThreadRepository.decideSignOff', () {
    ThreadMessage draftOf(List<ThreadMessage> items) =>
        items.firstWhere((item) => item.needsSignOff);

    test('approving a draft sends it', () async {
      final threads = _threads();
      final page = await threads.fetchMessages('cnv_menaka');
      final draft = draftOf(page.items);

      await threads.decideSignOff(
        conversationId: 'cnv_menaka',
        message: draft,
        approved: true,
      );

      final after = await threads.fetchMessages('cnv_menaka');
      final decided = after.items.firstWhere((item) => item.id == draft.id);
      expect(decided.status, MessageStatus.sent);
      expect(decided.needsSignOff, isFalse);
    });

    test('dismissing a draft takes it out of the associate\u2019s hands', () async {
      final threads = _threads();
      final page = await threads.fetchMessages('cnv_menaka');
      final draft = draftOf(page.items);

      await threads.decideSignOff(
        conversationId: 'cnv_menaka',
        message: draft,
        approved: false,
      );

      final after = await threads.fetchMessages('cnv_menaka');
      final decided = after.items.firstWhere((item) => item.id == draft.id);
      expect(decided.status, MessageStatus.cancelled);
      expect(decided.needsSignOff, isFalse);
    });

    test('leaves every other message alone', () async {
      final threads = _threads();
      final before = await threads.fetchMessages('cnv_menaka');
      final draft = draftOf(before.items);

      await threads.decideSignOff(
        conversationId: 'cnv_menaka',
        message: draft,
        approved: true,
      );

      final after = await threads.fetchMessages('cnv_menaka');
      expect(after.total, before.total);
      expect(
        after.items.where((item) => item.id != draft.id).map((item) => item.status),
        before.items.where((item) => item.id != draft.id).map((item) => item.status),
      );
    });
  });
}
