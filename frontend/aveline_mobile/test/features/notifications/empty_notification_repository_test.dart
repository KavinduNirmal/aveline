import 'package:aveline_mobile/features/notifications/data/empty_notification_repository.dart';
import 'package:flutter_test/flutter_test.dart';

/// The provider-less fallback is an honest empty inbox, not fiction.
///
/// The screen builds this repository when nothing has been injected, so it has
/// to answer a read with an empty page and refuse a write with a message that
/// says why - the rule `EmptyThreadRepository` established.
void main() {
  group('EmptyNotificationRepository', () {
    const repository = EmptyNotificationRepository();

    test('serves an empty page, echoing the paging it was asked for', () async {
      final page = await repository.fetchInbox(page: 3, pageSize: 20);

      expect(page.items, isEmpty);
      expect(page.total, 0);
      expect(page.hasMore, isFalse);
    });

    test('serves a zero unread count', () async {
      expect(await repository.fetchUnreadCount(), 0);
    });

    test('refuses a mark-read with a readable error', () async {
      await expectLater(repository.markRead('n-1'), throwsStateError);
    });

    test('refuses a mark-all-read with a readable error', () async {
      await expectLater(repository.markAllRead(), throwsStateError);
    });

    test('refuses a dismiss with a readable error', () async {
      await expectLater(repository.dismiss('n-1'), throwsStateError);
    });
  });
}
