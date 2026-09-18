import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/notifications/data/demo_notification_repository.dart';
import 'package:aveline_mobile/features/notifications/data/notification_repository.dart';
import 'package:aveline_mobile/features/notifications/domain/notification_page.dart';
import 'package:aveline_mobile/features/notifications/presentation/notifications_controller.dart';
import 'package:aveline_mobile/features/notifications/presentation/screens/notifications_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The day the demo inbox is measured from, so its ages never move.
final DateTime _now = DateTime.utc(2026, 9, 17, 12);

/// The inbox the screen is driven against.
///
/// The demo repository rather than a stub: this test is about the screen's
/// wiring, and the seed is what gives it a kind of every sort, a mix of read and
/// unread, and a client two of the notifications share.
DemoNotificationRepository _inbox({Duration latency = Duration.zero}) =>
    DemoNotificationRepository(clock: () => _now, latency: latency);

/// Fails the first page once, then serves the real inbox.
class _FlakyInbox implements NotificationRepository {
  _FlakyInbox(this._inner);

  final DemoNotificationRepository _inner;
  int failures = 1;

  @override
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  }) async {
    if (failures > 0) {
      failures--;
      throw Exception('The inbox is unavailable.');
    }
    return _inner.fetchInbox(
      page: page,
      pageSize: pageSize,
      unreadOnly: unreadOnly,
    );
  }

  @override
  Future<int> fetchUnreadCount() => _inner.fetchUnreadCount();

  @override
  Future<void> markRead(String id) => _inner.markRead(id);

  @override
  Future<void> markAllRead() => _inner.markAllRead();

  @override
  Future<void> dismiss(String id) => _inner.dismiss(id);
}

/// An inbox that has never held anything.
class _EmptyInbox implements NotificationRepository {
  @override
  Future<NotificationPage> fetchInbox({
    int page = 1,
    int pageSize = 20,
    bool unreadOnly = false,
  }) async => NotificationPage.empty;

  @override
  Future<int> fetchUnreadCount() async => 0;

  @override
  Future<void> markRead(String id) async {}

  @override
  Future<void> markAllRead() async {}

  @override
  Future<void> dismiss(String id) async {}
}

NotificationsController _controller({
  NotificationRepository? repository,
  int pageSize = 20,
  // Long enough that the undo offer outlives every pump a test performs, so the
  // delete is still takeable back when the Undo is tapped.
  Duration commitDelay = const Duration(hours: 1),
}) {
  final controller = NotificationsController(
    repository ?? _inbox(),
    pageSize: pageSize,
    dismissCommitDelay: commitDelay,
  );
  // Disposed at the end of every test, which is also what cancels the undo
  // window a delete opens and settles the timers the test binding insists on.
  addTearDown(controller.dispose);
  return controller;
}

/// A phone-shaped viewport with reduced motion on, matching the rest of the
/// suite: the backdrop carries ambient animation that would never settle
/// otherwise.
Widget _wrap(
  NotificationsController controller, {
  String boutiqueName = 'Ceylon Atelier',
}) {
  return MaterialApp(
    theme: AppTheme.light,
    home: MediaQuery(
      data: const MediaQueryData(
        disableAnimations: true,
        size: Size(390, 844),
      ),
      child: Scaffold(
        body: NotificationsScreen(
          controller: controller,
          boutiqueName: boutiqueName,
        ),
      ),
    ),
  );
}

Finder _tile(String id) => find.byKey(Key('notification_tile_$id'));
Finder _dot(String id) => find.byKey(Key('notification_unread_dot_$id'));
Finder _body(String id) => find.byKey(Key('notification_full_body_$id'));

/// Swipes a tile to the left, which is the delete.
Future<void> _swipeLeft(WidgetTester tester, String id) async {
  await tester.drag(_tile(id), const Offset(-600, 0));
  await tester.pumpAndSettle();
}

/// Swipes a tile to the right, which is the mark-as-read.
Future<void> _swipeRight(WidgetTester tester, String id) async {
  await tester.drag(_tile(id), const Offset(600, 0));
  await tester.pumpAndSettle();
}

/// Opens or closes a tile.
///
/// Taps the header rather than the tile: an open tile is taller, and the centre
/// of it can land on an action button rather than on the gesture that folds it
/// back up.
Future<void> _open(WidgetTester tester, String id) async {
  await tester.tap(find.byKey(Key('notification_header_$id')));
  await tester.pumpAndSettle();
}

/// Lets the undo window run out.
///
/// A test that leaves a deletion un-taken has to do this: the delete is only
/// sent to the API when the window lapses, and the test binding refuses to
/// finish while that timer is still live.
Future<void> _letUndoWindowLapse(
  WidgetTester tester,
  NotificationsController controller,
) async {
  await tester.pump(controller.dismissCommitDelay);
  await tester.pumpAndSettle();
}

void main() {
  group('NotificationsScreen', () {
    testWidgets('shows the inbox the controller holds', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      expect(
        find.byKey(const Key('notifications_title')),
        findsOneWidget,
      );
      expect(find.textContaining('Ceylon Atelier'), findsWidgets);
      expect(_tile('un_msg_nadeesha'), findsOneWidget);
      expect(_tile('un_approval_4821'), findsOneWidget);
      expect(find.byKey(const Key('notifications_unread_summary')), findsOneWidget);
      expect(find.text('4 unread'), findsOneWidget);
    });

    testWidgets('marks the unread tiles and leaves the read ones plain', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      expect(_dot('un_msg_nadeesha'), findsOneWidget);
      expect(_dot('un_approval_4821'), findsOneWidget);

      await tester.scrollUntilVisible(
        _tile('un_payment_4816'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      expect(_dot('un_payment_4816'), findsNothing);
    });

    testWidgets('states the count on the badge it hands the shell', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      // The header badge reads this, so the tab and the header cannot disagree.
      expect(controller.unreadCount, 4);
    });

    testWidgets('shows a loading state before the first page lands', (tester) async {
      final controller = _controller(
        repository: _inbox(latency: const Duration(milliseconds: 200)),
      );
      await tester.pumpWidget(_wrap(controller));
      await tester.pump();

      expect(find.byKey(const Key('notifications_loading')), findsOneWidget);

      await tester.pump(const Duration(milliseconds: 300));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('notifications_loading')), findsNothing);
      expect(_tile('un_msg_nadeesha'), findsOneWidget);
    });

    testWidgets('an inbox with nothing at all explains itself', (tester) async {
      final controller = _controller(repository: _EmptyInbox());
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('notifications_empty')), findsOneWidget);
      expect(find.text('No notifications yet'), findsOneWidget);
      expect(find.text('All caught up'), findsOneWidget);
    });

    testWidgets('the unread filter with nothing left says so', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('notifications_filter_unread')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('notifications_mark_all_read')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('notifications_empty')), findsOneWidget);
      expect(find.text('Nothing unread'), findsOneWidget);
      expect(controller.unreadCount, 0);
    });

    testWidgets('a failed load offers a retry that works', (tester) async {
      final controller = _controller(repository: _FlakyInbox(_inbox()));
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('notifications_error')), findsOneWidget);
      expect(find.byKey(const Key('notifications_retry')), findsOneWidget);
      expect(find.textContaining('unavailable'), findsOneWidget);

      await tester.tap(find.byKey(const Key('notifications_retry')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('notifications_error')), findsNothing);
      expect(_tile('un_msg_nadeesha'), findsOneWidget);
    });

    testWidgets('says so when the whole inbox has been read', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('notifications_mark_all_read')));
      await tester.pumpAndSettle();

      expect(find.text('All caught up'), findsOneWidget);
      expect(_dot('un_msg_nadeesha'), findsNothing);
      expect(
        find.byKey(const Key('notifications_mark_all_read')),
        findsNothing,
      );
      expect(controller.unreadCount, 0);
    });

    testWidgets('the unread filter narrows the inbox to what is unread', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('notifications_filter_unread')));
      await tester.pumpAndSettle();

      expect(_tile('un_msg_nadeesha'), findsOneWidget);
      expect(controller.items.every((item) => !item.isRead), isTrue);
      expect(controller.items, hasLength(4));

      await tester.tap(find.byKey(const Key('notifications_filter_all')));
      await tester.pumpAndSettle();

      expect(controller.items, hasLength(9));
    });

    testWidgets('states the end of the inbox', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await tester.scrollUntilVisible(
        find.byKey(const Key('notifications_end_of_list')),
        300,
        scrollable: find.byType(Scrollable).first,
      );

      expect(find.byKey(const Key('notifications_end_of_list')), findsOneWidget);
    });

    testWidgets('asks for the next page when the end of the page comes into view', (tester) async {
      final controller = _controller(pageSize: 4);
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      expect(controller.items, hasLength(4));

      await tester.drag(
        find.byKey(const Key('notifications_scroll')),
        const Offset(0, -700),
      );
      await tester.pumpAndSettle();

      expect(controller.items.length, greaterThan(4));
    });
  });

  group('NotificationsScreen swipe to delete', () {
    testWidgets('a swipe left takes the tile away and offers an undo', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _swipeLeft(tester, 'un_msg_nadeesha');

      expect(_tile('un_msg_nadeesha'), findsNothing);
      expect(controller.items.map((item) => item.id), isNot(contains('un_msg_nadeesha')));
      expect(find.text('Undo'), findsOneWidget);

      await _letUndoWindowLapse(tester, controller);
    });

    testWidgets('undo brings the tile back where it was', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _swipeLeft(tester, 'un_msg_nadeesha');
      await tester.tap(find.text('Undo'));
      await tester.pumpAndSettle();

      expect(_tile('un_msg_nadeesha'), findsOneWidget);
      expect(controller.items.first.id, 'un_msg_nadeesha');
      expect(controller.unreadCount, 4);
    });

    testWidgets('deleting an unread tile takes a step off the badge, and undo gives it back', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _swipeLeft(tester, 'un_msg_nadeesha');
      expect(controller.unreadCount, 3);
      expect(find.text('3 unread'), findsOneWidget);

      await tester.tap(find.text('Undo'));
      await tester.pumpAndSettle();

      expect(controller.unreadCount, 4);
    });

    testWidgets('the opened tile carries its own delete', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _open(tester, 'un_msg_nadeesha');
      await tester.tap(find.byKey(const Key('notification_action_delete_un_msg_nadeesha')));
      await tester.pumpAndSettle();

      expect(_tile('un_msg_nadeesha'), findsNothing);
      expect(find.text('Undo'), findsOneWidget);

      await _letUndoWindowLapse(tester, controller);
    });
  });

  group('NotificationsScreen swipe to mark read', () {
    testWidgets('a swipe right marks the tile read and keeps it in the inbox', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _swipeRight(tester, 'un_msg_nadeesha');

      expect(_tile('un_msg_nadeesha'), findsOneWidget);
      expect(_dot('un_msg_nadeesha'), findsNothing);
      expect(controller.unreadCount, 3);
      expect(find.text('3 unread'), findsOneWidget);
    });

    testWidgets('a swipe right on a read tile changes nothing', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();
      final before = controller.unreadCount;

      await _swipeRight(tester, 'un_payment_4816');

      expect(_tile('un_payment_4816'), findsOneWidget);
      expect(controller.unreadCount, before);
    });

    testWidgets('marking the last unread tile leaves the inbox caught up', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      for (final id in [
        'un_msg_nadeesha',
        'un_approval_4821',
        'un_match_ivory',
        'un_vip_hasini',
      ]) {
        await tester.scrollUntilVisible(
          _tile(id),
          200,
          scrollable: find.byType(Scrollable).first,
        );
        await _swipeRight(tester, id);
      }

      expect(controller.unreadCount, 0);

      // Back to the top before reading the summary: it lives in the header, and
      // the scroll that reached the last unread tile carried the header away.
      await tester.scrollUntilVisible(
        find.byKey(const Key('notifications_unread_summary')),
        -300,
        scrollable: find.byType(Scrollable).first,
      );

      expect(find.text('All caught up'), findsOneWidget);
    });
  });

  group('NotificationsScreen accordion', () {
    testWidgets('tapping a tile opens it onto the whole notification', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      expect(_body('un_msg_nadeesha'), findsNothing);

      await _open(tester, 'un_msg_nadeesha');

      expect(_body('un_msg_nadeesha'), findsOneWidget);
      expect(
        find.textContaining('whether the same cloth comes in a deeper shade'),
        findsOneWidget,
      );
    });

    testWidgets('an opened tile offers its actions', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _open(tester, 'un_msg_nadeesha');

      expect(
        find.byKey(const Key('notification_action_read_un_msg_nadeesha')),
        findsOneWidget,
      );
      expect(
        find.byKey(const Key('notification_action_delete_un_msg_nadeesha')),
        findsOneWidget,
      );
    });

    testWidgets('only one tile is open at a time', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _open(tester, 'un_msg_nadeesha');
      expect(_body('un_msg_nadeesha'), findsOneWidget);

      await _open(tester, 'un_approval_4821');

      // The accordion closes what it was showing rather than stacking panels,
      // which is what keeps a long inbox readable on a phone.
      expect(_body('un_msg_nadeesha'), findsNothing);
      expect(_body('un_approval_4821'), findsOneWidget);
    });

    testWidgets('tapping the open tile closes it again', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _open(tester, 'un_msg_nadeesha');
      await _open(tester, 'un_msg_nadeesha');

      expect(_body('un_msg_nadeesha'), findsNothing);
    });

    testWidgets('the opened tile can mark itself read', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _open(tester, 'un_msg_nadeesha');
      await tester.tap(
        find.byKey(const Key('notification_action_read_un_msg_nadeesha')),
      );
      await tester.pumpAndSettle();

      expect(controller.unreadCount, 3);
      expect(_tile('un_msg_nadeesha'), findsOneWidget);
      expect(_dot('un_msg_nadeesha'), findsNothing);
    });

    testWidgets('the opened tile offers a way into the client it is about', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await _open(tester, 'un_msg_nadeesha');

      expect(
        find.byKey(const Key('notification_action_open_un_msg_nadeesha')),
        findsOneWidget,
      );
    });

    testWidgets('a notification with no destination offers no way in', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      await tester.scrollUntilVisible(
        _tile('un_approval_4821'),
        200,
        scrollable: find.byType(Scrollable).first,
      );
      await _open(tester, 'un_approval_4821');

      expect(
        find.byKey(const Key('notification_action_open_un_approval_4821')),
        findsNothing,
      );
      expect(
        find.byKey(const Key('notification_action_read_un_approval_4821')),
        findsOneWidget,
      );
    });
  });

  group('NotificationsScreen swipe backgrounds', () {
    testWidgets('each gesture uncovers what it will do', (tester) async {
      final controller = _controller();
      await tester.pumpWidget(_wrap(controller));
      await tester.pumpAndSettle();

      // Held mid-drag rather than flicked: Dismissible only builds its
      // background while the tile is actually displaced, so the label has to be
      // read with the finger still down. The gesture moves twice because the
      // first move only wins the arena - the displacement needs a second one.
      final gesture = await tester.startGesture(
        tester.getCenter(_tile('un_msg_nadeesha')),
      );
      await gesture.moveBy(const Offset(20, 0));
      await tester.pump();
      await gesture.moveBy(const Offset(80, 0));
      await tester.pump();

      expect(
        find.byKey(const Key('notification_swipe_read_un_msg_nadeesha')),
        findsOneWidget,
      );
      expect(find.text('Mark read'), findsOneWidget);

      await gesture.moveBy(const Offset(-200, 0));
      await tester.pump();

      expect(
        find.byKey(const Key('notification_swipe_delete_un_msg_nadeesha')),
        findsOneWidget,
      );
      expect(find.text('Delete'), findsOneWidget);

      await gesture.up();
      await tester.pumpAndSettle();

      // Neither gesture ran: the drag never reached the threshold that commits.
      expect(_tile('un_msg_nadeesha'), findsOneWidget);
      expect(controller.unreadCount, 4);
    });
  });
}
