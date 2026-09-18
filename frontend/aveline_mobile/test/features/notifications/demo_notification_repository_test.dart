import 'package:aveline_mobile/features/notifications/data/demo_notification_repository.dart';
import 'package:aveline_mobile/features/notifications/domain/notification_kind.dart';
import 'package:flutter_test/flutter_test.dart';

/// The clock the demo inbox is measured from, so "20 minutes ago" means the same
/// thing on every run.
final DateTime _now = DateTime.utc(2026, 9, 17, 12);

DemoNotificationRepository _inbox() =>
    DemoNotificationRepository(clock: () => _now, latency: Duration.zero);

void main() {
  group('DemoNotificationRepository.fetchInbox', () {
    test('serves the newest first', () async {
      final page = await _inbox().fetchInbox();

      final times = page.items.map((item) => item.createdAt).toList();
      final sorted = [...times]..sort((a, b) => b.compareTo(a));
      expect(times, sorted);
    });

    test('covers every kind the gateway dispatches, so the tab can be designed', () async {
      final page = await _inbox().fetchInbox();

      final kinds = page.items.map((item) => item.kind).toSet();
      for (final kind in NotificationKind.values) {
        if (kind == NotificationKind.unknown) {
          continue;
        }
        expect(kinds, contains(kind), reason: '$kind is missing from the demo inbox');
      }
    });

    test('mixes read and unread, so both tile states are visible', () async {
      final page = await _inbox().fetchInbox();

      expect(page.items.any((item) => item.isRead), isTrue);
      expect(page.items.any((item) => !item.isRead), isTrue);
    });

    test('reports the whole inbox as its total, not just the page', () async {
      final page = await _inbox().fetchInbox(pageSize: 3);

      expect(page.items, hasLength(3));
      expect(page.total, greaterThan(3));
      expect(page.hasMore, isTrue);
    });

    test('a later page continues where the first stopped', () async {
      final inbox = _inbox();

      final first = await inbox.fetchInbox(page: 1, pageSize: 3);
      final second = await inbox.fetchInbox(page: 2, pageSize: 3);

      expect(
        second.items.map((item) => item.id),
        isNot(contains(first.items.first.id)),
      );
      expect(second.page, 2);
    });

    test('a page past the end is empty rather than an error', () async {
      final page = await _inbox().fetchInbox(page: 99, pageSize: 20);

      expect(page.items, isEmpty);
      expect(page.hasMore, isFalse);
    });

    test('unreadOnly narrows to what is unread and totals only that', () async {
      final all = await _inbox().fetchInbox();
      final unread = await _inbox().fetchInbox(unreadOnly: true);

      expect(unread.items, isNotEmpty);
      expect(unread.items.every((item) => !item.isRead), isTrue);
      expect(unread.total, unread.items.length);
      expect(unread.total, lessThan(all.total));
    });

    test('a page of nothing is what a zero page size asks for', () async {
      final page = await _inbox().fetchInbox(pageSize: 0);

      expect(page.items, isEmpty);
    });
  });

  group('DemoNotificationRepository.fetchUnreadCount', () {
    test('counts what the full inbox has not read', () async {
      final inbox = _inbox();

      final count = await inbox.fetchUnreadCount();
      final page = await inbox.fetchInbox();

      expect(count, page.items.where((item) => !item.isRead).length);
      expect(count, greaterThan(0));
    });
  });

  group('DemoNotificationRepository.markRead', () {
    test('flips one item and its count', () async {
      final inbox = _inbox();
      final before = await inbox.fetchUnreadCount();
      final target = (await inbox.fetchInbox(unreadOnly: true)).items.first;

      await inbox.markRead(target.id);

      expect(await inbox.fetchUnreadCount(), before - 1);
      final all = await inbox.fetchInbox();
      expect(all.items.firstWhere((item) => item.id == target.id).isRead, isTrue);
    });

    test('is idempotent, so a second swipe at an unread item is harmless', () async {
      final inbox = _inbox();
      final target = (await inbox.fetchInbox(unreadOnly: true)).items.first;

      await inbox.markRead(target.id);
      final once = await inbox.fetchUnreadCount();
      await inbox.markRead(target.id);

      expect(await inbox.fetchUnreadCount(), once);
    });

    test('ignores an id the inbox does not hold', () async {
      final inbox = _inbox();

      await inbox.markRead('un_does_not_exist');

      expect(await inbox.fetchUnreadCount(), greaterThan(0));
    });
  });

  group('DemoNotificationRepository.markAllRead', () {
    test('empties the unread count', () async {
      final inbox = _inbox();

      await inbox.markAllRead();

      expect(await inbox.fetchUnreadCount(), 0);
      expect((await inbox.fetchInbox(unreadOnly: true)).items, isEmpty);
      expect((await inbox.fetchInbox()).items.every((item) => item.isRead), isTrue);
    });

    test('keeps every item in the inbox', () async {
      final inbox = _inbox();
      final before = (await inbox.fetchInbox()).items.length;

      await inbox.markAllRead();

      expect((await inbox.fetchInbox()).items, hasLength(before));
    });
  });

  group('DemoNotificationRepository.dismiss', () {
    test('removes the item from the inbox and its total', () async {
      final inbox = _inbox();
      final before = await inbox.fetchInbox();
      final target = before.items.first;

      await inbox.dismiss(target.id);

      final after = await inbox.fetchInbox();
      expect(after.items.map((item) => item.id), isNot(contains(target.id)));
      expect(after.total, before.total - 1);
    });

    test('drops an unread item from the count too', () async {
      final inbox = _inbox();
      final unreadBefore = await inbox.fetchUnreadCount();
      final target = (await inbox.fetchInbox(unreadOnly: true)).items.first;

      await inbox.dismiss(target.id);

      expect(await inbox.fetchUnreadCount(), unreadBefore - 1);
    });

    test('leaves the count alone when the item was already read', () async {
      final inbox = _inbox();
      final all = await inbox.fetchInbox();
      final read = all.items.firstWhere((item) => item.isRead);
      final unreadBefore = await inbox.fetchUnreadCount();

      await inbox.dismiss(read.id);

      expect(await inbox.fetchUnreadCount(), unreadBefore);
    });

    test('is idempotent, so a retry cannot remove a second item', () async {
      final inbox = _inbox();
      final target = (await inbox.fetchInbox()).items.first;

      await inbox.dismiss(target.id);
      final afterFirst = (await inbox.fetchInbox()).total;
      await inbox.dismiss(target.id);

      expect((await inbox.fetchInbox()).total, afterFirst);
    });
  });

  group('DemoNotificationRepository', () {
    test('hands each caller its own inbox, so one tab cannot empty another', () async {
      final first = _inbox();
      final second = _inbox();

      await first.markAllRead();

      expect(await second.fetchUnreadCount(), greaterThan(0));
    });

    test('measures its ages from the injected clock', () async {
      final page = await DemoNotificationRepository(
        clock: () => _now,
        latency: Duration.zero,
      ).fetchInbox();

      expect(page.items.first.createdAt.isBefore(_now), isTrue);
      expect(page.items.last.createdAt.isBefore(_now), isTrue);
    });
  });
}
