import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/notification_provider.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/shared/widgets/aveline_header.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

Widget _buildHeaderTestBed({
  VoidCallback? onMenuPressed,
  VoidCallback? onSearchPressed,
  VoidCallback? onNotificationsPressed,
  VoidCallback? onProfilePressed,
  NotificationPayload? notification,
  AvelineUser? user,
}) {
  final notifProvider = NotificationProvider();
  if (notification != null) {
    notifProvider.push(notification);
  }
  final userProvider = UserProvider();
  if (user != null) {
    userProvider.setUser(user);
  }

  return MultiProvider(
    providers: [
      ChangeNotifierProvider<NotificationProvider>.value(value: notifProvider),
      ChangeNotifierProvider<UserProvider>.value(value: userProvider),
    ],
    child: MaterialApp(
      home: Scaffold(
        appBar: AvelineHeader(
          onMenuPressed: onMenuPressed,
          onSearchPressed: onSearchPressed,
          onNotificationsPressed: onNotificationsPressed,
          onProfilePressed: onProfilePressed,
        ),
        drawer: const Drawer(child: Text('Drawer Content')),
        body: const Center(child: Text('Body')),
      ),
    ),
  );
}

void main() {
  group('AvelineHeader', () {
    testWidgets('renders menu button and 3 right action buttons', (tester) async {
      await tester.pumpWidget(_buildHeaderTestBed());

      expect(find.byKey(const Key('aveline_header_menu_button')), findsOneWidget);
      expect(find.byKey(const Key('aveline_header_search_button')), findsOneWidget);
      expect(find.byKey(const Key('aveline_header_notifications_button')), findsOneWidget);
      expect(find.byKey(const Key('aveline_header_profile_button')), findsOneWidget);
    });

    testWidgets('tapping menu button calls onMenuPressed callback', (tester) async {
      var menuPressed = false;
      await tester.pumpWidget(_buildHeaderTestBed(
        onMenuPressed: () => menuPressed = true,
      ));

      await tester.tap(find.byKey(const Key('aveline_header_menu_button')));
      await tester.pump();

      expect(menuPressed, isTrue);
    });

    testWidgets('tapping search button calls onSearchPressed callback', (tester) async {
      var searchPressed = false;
      await tester.pumpWidget(_buildHeaderTestBed(
        onSearchPressed: () => searchPressed = true,
      ));

      await tester.tap(find.byKey(const Key('aveline_header_search_button')));
      await tester.pump();

      expect(searchPressed, isTrue);
    });

    testWidgets('tapping notifications button calls onNotificationsPressed callback', (tester) async {
      var notifPressed = false;
      await tester.pumpWidget(_buildHeaderTestBed(
        onNotificationsPressed: () => notifPressed = true,
      ));

      await tester.tap(find.byKey(const Key('aveline_header_notifications_button')));
      await tester.pump();

      expect(notifPressed, isTrue);
    });

    testWidgets('tapping profile button calls onProfilePressed callback', (tester) async {
      var profilePressed = false;
      await tester.pumpWidget(_buildHeaderTestBed(
        onProfilePressed: () => profilePressed = true,
      ));

      await tester.tap(find.byKey(const Key('aveline_header_profile_button')));
      await tester.pump();

      expect(profilePressed, isTrue);
    });

    testWidgets('shows badge dot on notification icon when notification exists', (tester) async {
      const payload = NotificationPayload(
        type: 'alert',
        title: 'Alert',
        body: 'Something happened',
      );

      await tester.pumpWidget(_buildHeaderTestBed(notification: payload));

      expect(find.byKey(const Key('notification_badge_dot')), findsOneWidget);
    });
  });

  group('AvelineHeader tap targets', () {
    // The conventional Material minimum tap target, stated as the floor this
    // suite refuses to go below. The header is on every screen, so a control
    // that shrinks here is a one-handed miss everywhere.
    const double minimumTapTarget = 48;

    void expectTapTarget(WidgetTester tester, String label, Key key) {
      final size = tester.getSize(find.byKey(key));
      expect(
        size.width,
        greaterThanOrEqualTo(minimumTapTarget),
        reason: '$label is ${size.width} logical pixels wide, below the '
            '$minimumTapTarget floor',
      );
      expect(
        size.height,
        greaterThanOrEqualTo(minimumTapTarget),
        reason: '$label is ${size.height} logical pixels tall, below the '
            '$minimumTapTarget floor',
      );
    }

    testWidgets('every control renders at least a 48x48 logical-pixel target', (
      tester,
    ) async {
      await tester.pumpWidget(_buildHeaderTestBed());

      expectTapTarget(
        tester,
        'the navigation-menu button',
        const Key('aveline_header_menu_button'),
      );
      expectTapTarget(
        tester,
        'the search button',
        const Key('aveline_header_search_button'),
      );
      expectTapTarget(
        tester,
        'the notifications button',
        const Key('aveline_header_notifications_button'),
      );
      expectTapTarget(
        tester,
        'the profile button',
        const Key('aveline_header_profile_button'),
      );
    });
  });
}
