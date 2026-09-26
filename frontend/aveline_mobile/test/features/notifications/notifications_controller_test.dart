import 'package:aveline_mobile/features/notifications/data/notification_repository.dart';
import 'package:aveline_mobile/features/notifications/domain/app_notification.dart';
import 'package:aveline_mobile/features/notifications/domain/notification_page.dart';
import 'package:aveline_mobile/features/notifications/presentation/notifications_controller.dart';
import 'package:flutter_test/flutter_test.dart';

/// An inbox held in memory, with every failure the screen has to survive made
/// switchable, so the controller's optimistic paths can be driven both ways.
class _FakeInbox implements NotificationRepository {
  _FakeInbox({List<AppNotification>? items, this.reportedUnreadCount})
    : items = [...?items];

  List<AppNotification> items;

  /// Lets a test make the count disagree with the loaded page, which is exactly
  /// why the count is fetched from its own endpoint.
  int? reportedUnreadCount;

  bool failList = false;
  bool failCount = false;
  bool failMarkRead = false;
  bool failMarkAllRead = false;
  bool failDismiss = false;

  final List<String> markedRead = [];
  final List<String> dismissed = [];
  final List<int> requestedPages = [];
  int markAllReadCalls = 0;

  @override
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  }) async {
    requestedPages.add(page);
    if (failList) {
      throw Exception('The inbox is unavailable.');
    }
    final visible = unreadOnly
        ? items.where((item) => !item.isRead).toList()
        : [...items];
    final start = (page - 1) * pageSize;
    final slice = pageSize <= 0 || start >= visible.length
        ? <AppNotification>[]
        : visible.sublist(start, (start + pageSize).clamp(0, visible.length));
    return NotificationPage(
      items: slice,
      total: visible.length,
      page: page,
      pageSize: pageSize,
    );
  }

  @override
  Future<int> fetchUnreadCount() async {
    if (failCount) {
      throw Exception('The count is unavailable.');
    }
    return reportedUnreadCount ??
        items.where((item) => !item.isRead).length;
  }

  @override
  Future<void> markRead(String id) async {
    if (failMarkRead) {
      throw Exception('That notification could not be marked as read.');
    }
    markedRead.add(id);
    items = [
      for (final item in items)
        if (item.id == id) item.copyWith(isRead: true) else item,
    ];
  }

  @override
  Future<void> markAllRead() async {
    if (failMarkAllRead) {
      throw Exception('The inbox could not be marked as read.');
    }
    markAllReadCalls++;
    items = [for (final item in items) item.copyWith(isRead: true)];
  }

  @override
  Future<void> dismiss(String id) async {
    if (failDismiss) {
      throw Exception('That notification could not be deleted.');
    }
    dismissed.add(id);
    items = items.where((item) => item.id != id).toList();
  }
}

AppNotification _item(int n, {bool isRead = false}) => AppNotification(
  id: 'un_$n',
  notificationId: 'nr_$n',
  type: 'NewMessage',
  title: 'Notification $n',
  body: 'Body $n',
  isRead: isRead,
  createdAt: DateTime.utc(2026, 9, 17, 12).subtract(Duration(minutes: n)),
);

/// Ten notifications, newest first, with four unread: the odd ones up to seven.
List<AppNotification> _tenItems() => [
  for (var n = 1; n <= 10; n++) _item(n, isRead: n.isEven || n == 9),
];

/// A controller whose dismissal window never lapses on its own, so a test can
/// choose the moment the delete is committed.
NotificationsController _controller(
  _FakeInbox inbox, {
  Duration commitDelay = const Duration(hours: 1),
}) => NotificationsController(inbox, dismissCommitDelay: commitDelay);

void main() {
  group('NotificationsController.load', () {
    test('fills the inbox and the badge count', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);

      await controller.load();

      expect(controller.items, hasLength(10));
      expect(controller.items.first.id, 'un_1');
      expect(controller.unreadCount, 4);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
      expect(controller.errorMessage, isNull);
      expect(controller.isEmpty, isFalse);
    });

    test('takes the count from its own endpoint rather than from the page', () async {
      // Page one holds four items, but the inbox holds twelve unread: a count
      // derived from the page would under-report the badge.
      final inbox = _FakeInbox(items: _tenItems(), reportedUnreadCount: 12);
      final controller = _controller(inbox);

      await controller.load();

      expect(controller.unreadCount, 12);
    });

    test('an empty inbox is empty rather than broken', () async {
      final controller = _controller(_FakeInbox());

      await controller.load();

      expect(controller.items, isEmpty);
      expect(controller.isEmpty, isTrue);
      expect(controller.unreadCount, 0);
      expect(controller.errorMessage, isNull);
    });

    test('a failed load surfaces its message and settles', () async {
      final inbox = _FakeInbox(items: _tenItems())..failList = true;
      final controller = _controller(inbox);

      await controller.load();

      expect(controller.errorMessage, 'The inbox is unavailable.');
      expect(controller.items, isEmpty);
      expect(controller.hasLoadedOnce, isTrue);
      expect(controller.isLoading, isFalse);
    });

    test('a failed count does not take the loaded inbox down with it', () async {
      final inbox = _FakeInbox(items: _tenItems())..failCount = true;
      final controller = _controller(inbox);

      await controller.load();

      expect(controller.errorMessage, isNull);
      expect(controller.items, hasLength(10));
      expect(controller.unreadCount, 4);
    });

    test('reloading clears the error it was carrying', () async {
      final inbox = _FakeInbox(items: _tenItems())..failList = true;
      final controller = _controller(inbox);
      await controller.load();

      inbox.failList = false;
      await controller.load();

      expect(controller.errorMessage, isNull);
      expect(controller.items, hasLength(10));
    });

    test('unreadOnly narrows the inbox and is remembered for the next load', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);

      await controller.load(unreadOnly: true);

      expect(controller.unreadOnly, isTrue);
      expect(controller.items.every((item) => !item.isRead), isTrue);
      expect(controller.unreadCount, 4);

      await controller.load();

      expect(controller.unreadOnly, isTrue);
      expect(controller.items, hasLength(4));
    });

    test('notifies its listeners while it loads', () async {
      final controller = _controller(_FakeInbox(items: _tenItems()));
      var notifications = 0;
      controller.addListener(() => notifications++);

      await controller.load();

      expect(notifications, greaterThan(0));
    });
  });

  group('NotificationsController.refresh', () {
    test('keeps the inbox on screen until the newer one lands', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      final pending = controller.refresh();

      // Nothing is cleared while the request is in flight: a pull that blanked
      // the list would read as a failure rather than as a refresh.
      expect(controller.items, hasLength(10));
      await pending;
      expect(controller.items, hasLength(10));
    });

    test('picks up what arrived since the inbox was opened', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      inbox.items = [_item(0), ...inbox.items];
      inbox.reportedUnreadCount = 9;
      await controller.refresh();

      expect(controller.items.first.id, 'un_0');
      expect(controller.unreadCount, 9);
    });

    test('a failed refresh leaves the inbox standing and reports it', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      inbox.failList = true;
      await controller.refresh();

      expect(controller.items, hasLength(10));
      expect(controller.actionError, 'The inbox is unavailable.');
    });

    test('loads rather than refreshing when nothing has been loaded yet', () async {
      final controller = _controller(_FakeInbox(items: _tenItems()));

      await controller.refresh();

      expect(controller.items, hasLength(10));
      expect(controller.hasLoadedOnce, isTrue);
    });
  });

  group('NotificationsController.loadMore', () {
    test('appends the next page without repeating what is on screen', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = NotificationsController(
        inbox,
        pageSize: 4,
        dismissCommitDelay: const Duration(hours: 1),
      );
      await controller.load();

      expect(controller.items, hasLength(4));
      expect(controller.hasMore, isTrue);

      await controller.loadMore();

      expect(controller.items.map((item) => item.id), [
        'un_1',
        'un_2',
        'un_3',
        'un_4',
        'un_5',
        'un_6',
        'un_7',
        'un_8',
      ]);
      expect(inbox.requestedPages, [1, 2]);
    });

    test('does nothing once the whole inbox is on screen', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = NotificationsController(
        inbox,
        pageSize: 20,
        dismissCommitDelay: const Duration(hours: 1),
      );
      await controller.load();

      expect(controller.hasMore, isFalse);
      await controller.loadMore();

      expect(inbox.requestedPages, [1]);
    });

    test('a failed page leaves what is already on screen', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = NotificationsController(
        inbox,
        pageSize: 4,
        dismissCommitDelay: const Duration(hours: 1),
      );
      await controller.load();

      inbox.failList = true;
      await controller.loadMore();

      expect(controller.items, hasLength(4));
      expect(controller.isLoadingMore, isFalse);
      expect(controller.actionError, 'The inbox is unavailable.');
    });

    test('a page that arrives after a reload is dropped', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = NotificationsController(
        inbox,
        pageSize: 4,
        dismissCommitDelay: const Duration(hours: 1),
      );
      await controller.load();

      final stale = controller.loadMore();
      await controller.load(unreadOnly: true);
      await stale;

      // The narrowing the page belonged to is gone, so its items must not be
      // grafted onto the unread inbox.
      expect(controller.items.every((item) => !item.isRead), isTrue);
      expect(controller.isLoadingMore, isFalse);
    });
  });

  group('NotificationsController.markRead', () {
    test('marks the tile and drops the badge before the API answers', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      final pending = controller.markRead(controller.items.first);

      expect(controller.items.first.isRead, isTrue);
      expect(controller.items.first.readAt, isNotNull);
      expect(controller.unreadCount, 3);

      await pending;

      expect(inbox.markedRead, ['un_1']);
      expect(controller.actionError, isNull);
    });

    test('an item already read is left alone, API call and all', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      await controller.markRead(controller.items[1]);

      expect(inbox.markedRead, isEmpty);
      expect(controller.unreadCount, 4);
    });

    test('an item the inbox no longer holds is left alone', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      await controller.markRead(_item(99));

      expect(inbox.markedRead, isEmpty);
    });

    test('a refusal puts the tile back and reports it', () async {
      final inbox = _FakeInbox(items: _tenItems())..failMarkRead = true;
      final controller = _controller(inbox);
      await controller.load();

      await controller.markRead(controller.items.first);

      expect(controller.items.first.isRead, isFalse);
      expect(controller.unreadCount, 4);
      expect(controller.actionError, contains('marked as read'));
    });

    test('under the unread narrowing the read tile leaves the list', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load(unreadOnly: true);
      final target = controller.items.first;

      await controller.markRead(target);

      expect(controller.items.map((item) => item.id), isNot(contains(target.id)));
      expect(controller.unreadCount, 3);
    });

    test('a refusal under the unread narrowing puts the tile back where it was', () async {
      final inbox = _FakeInbox(items: _tenItems())..failMarkRead = true;
      final controller = _controller(inbox);
      await controller.load(unreadOnly: true);
      final before = controller.items.map((item) => item.id).toList();

      await controller.markRead(controller.items.first);

      expect(controller.items.map((item) => item.id), before);
      expect(controller.unreadCount, 4);
    });
  });

  group('NotificationsController.markAllRead', () {
    test('empties the badge and every tile', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      await controller.markAllRead();

      expect(controller.unreadCount, 0);
      expect(controller.items.every((item) => item.isRead), isTrue);
      expect(inbox.markAllReadCalls, 1);
    });

    test('does nothing when there is nothing unread', () async {
      final inbox = _FakeInbox(items: [_item(1, isRead: true)]);
      final controller = _controller(inbox);
      await controller.load();

      await controller.markAllRead();

      expect(inbox.markAllReadCalls, 0);
    });

    test('under the unread narrowing the inbox empties', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load(unreadOnly: true);

      await controller.markAllRead();

      expect(controller.items, isEmpty);
      expect(controller.unreadCount, 0);
    });

    test('a refusal restores every tile and the badge', () async {
      final inbox = _FakeInbox(items: _tenItems())..failMarkAllRead = true;
      final controller = _controller(inbox);
      await controller.load();
      final before = controller.items.map((item) => item.isRead).toList();

      await controller.markAllRead();

      expect(controller.items.map((item) => item.isRead), before);
      expect(controller.unreadCount, 4);
      expect(controller.actionError, contains('marked as read'));
    });
  });

  group('NotificationsController.dismiss', () {
    test('takes the tile away at once but commits only after the window', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      controller.dismiss(controller.items.first);

      expect(controller.items, hasLength(9));
      expect(controller.unreadCount, 3);
      expect(controller.hasPendingDismissal, isTrue);
      expect(inbox.dismissed, isEmpty);

      await pumpEventQueue();

      // The window has not lapsed, so the API still has not been told.
      expect(inbox.dismissed, isEmpty);
    });

    test('commits the delete once the window lapses', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox, commitDelay: Duration.zero);
      await controller.load();

      controller.dismiss(controller.items.first);
      await pumpEventQueue();

      expect(inbox.dismissed, ['un_1']);
      expect(controller.hasPendingDismissal, isFalse);
      expect(controller.items, hasLength(9));
    });

    test('undo puts the tile back where it was and never tells the API', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();
      final before = controller.items.map((item) => item.id).toList();

      controller.dismiss(controller.items[2]);
      controller.undoDismiss();
      await pumpEventQueue();

      expect(controller.items.map((item) => item.id), before);
      expect(controller.unreadCount, 4);
      expect(inbox.dismissed, isEmpty);
      expect(controller.hasPendingDismissal, isFalse);
    });

    test('deleting an unread tile drops the badge, and undo restores it', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();
      final unread = controller.items.firstWhere((item) => !item.isRead);

      controller.dismiss(unread);
      expect(controller.unreadCount, 3);

      controller.undoDismiss();
      expect(controller.unreadCount, 4);
    });

    test('deleting a read tile leaves the badge alone', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();
      final read = controller.items.firstWhere((item) => item.isRead);

      controller.dismiss(read);

      expect(controller.unreadCount, 4);
    });

    test('a refused delete puts the tile back and reports it', () async {
      final inbox = _FakeInbox(items: _tenItems())..failDismiss = true;
      final controller = _controller(inbox, commitDelay: Duration.zero);
      await controller.load();
      final before = controller.items.map((item) => item.id).toList();

      controller.dismiss(controller.items.first);
      await pumpEventQueue();

      expect(controller.items.map((item) => item.id), before);
      expect(controller.unreadCount, 4);
      expect(controller.actionError, contains('deleted'));
      expect(controller.hasPendingDismissal, isFalse);
    });

    test('a second delete commits the first, so undo only covers the newest', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      controller.dismiss(controller.items.first);
      controller.dismiss(controller.items.first);
      await pumpEventQueue();

      expect(inbox.dismissed, ['un_1']);
      expect(controller.hasPendingDismissal, isTrue);
      expect(controller.items, hasLength(8));
    });

    test('undo after a second delete restores only the second', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      controller.dismiss(controller.items.first);
      controller.dismiss(controller.items.first);
      await pumpEventQueue();
      controller.undoDismiss();

      expect(controller.items.map((item) => item.id), contains('un_2'));
      expect(controller.items.map((item) => item.id), isNot(contains('un_1')));
    });

    test('undo with nothing pending does nothing', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox);
      await controller.load();

      controller.undoDismiss();

      expect(controller.items, hasLength(10));
    });

    test('disposing commits a delete the associate already made', () async {
      final inbox = _FakeInbox(items: _tenItems());
      final controller = _controller(inbox, commitDelay: Duration.zero);
      await controller.load();

      controller.dismiss(controller.items.first);
      controller.dispose();
      await pumpEventQueue();

      expect(inbox.dismissed, ['un_1']);
    });
  });

  group('NotificationsController.actionError', () {
    test('is cleared on request, so a toast is shown once', () async {
      final inbox = _FakeInbox(items: _tenItems())..failMarkRead = true;
      final controller = _controller(inbox);
      await controller.load();
      await controller.markRead(controller.items.first);

      expect(controller.actionError, isNotNull);

      controller.clearActionError();

      expect(controller.actionError, isNull);
    });

    test('is cleared when a later action succeeds', () async {
      final inbox = _FakeInbox(items: _tenItems())..failMarkRead = true;
      final controller = _controller(inbox);
      await controller.load();
      await controller.markRead(controller.items.first);

      inbox.failMarkRead = false;
      await controller.markRead(controller.items.first);

      expect(controller.actionError, isNull);
    });
  });

  group('NotificationsController.applyUnreadCount', () {
    test('takes the count that arrived with a realtime payload', () async {
      final inbox = _FakeInbox(items: _tenItems(), reportedUnreadCount: 4);
      final controller = _controller(inbox);
      await controller.load();
      expect(controller.unreadCount, 4);

      var notifications = 0;
      controller.addListener(() => notifications++);

      controller.applyUnreadCount(5);

      // The badge moves from the payload's count alone, without a list read.
      expect(controller.unreadCount, 5);
      expect(notifications, 1);
    });

    test('keeps the loaded inbox untouched, because rows are the API authority', () async {
      final inbox = _FakeInbox(items: _tenItems(), reportedUnreadCount: 4);
      final controller = _controller(inbox);
      await controller.load();

      controller.applyUnreadCount(9);

      expect(controller.items, hasLength(10));
      expect(controller.unreadCount, 9);
    });

    test('never lets the count fall below zero', () async {
      final inbox = _FakeInbox(items: _tenItems(), reportedUnreadCount: 4);
      final controller = _controller(inbox);
      await controller.load();

      controller.applyUnreadCount(-3);

      expect(controller.unreadCount, 0);
    });

    test('a repeated count notifies nobody', () async {
      final inbox = _FakeInbox(items: _tenItems(), reportedUnreadCount: 4);
      final controller = _controller(inbox);
      await controller.load();

      var notifications = 0;
      controller.addListener(() => notifications++);

      controller.applyUnreadCount(4);

      expect(controller.unreadCount, 4);
      expect(notifications, 0);
    });
  });
}
