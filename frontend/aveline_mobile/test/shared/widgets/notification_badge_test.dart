import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/notification_provider.dart';
import 'package:aveline_mobile/shared/widgets/notification_badge.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

Widget _buildBadgeApp({
  NotificationPayload? notification,
  int? unreadCount,
  bool? forceShow,
  NotificationProvider? provider,
}) {
  final resolved = provider ?? NotificationProvider();
  if (notification != null) {
    resolved.push(notification);
  }
  if (unreadCount != null) {
    resolved.setUnreadCount(unreadCount);
  }
  return ChangeNotifierProvider<NotificationProvider>.value(
    value: resolved,
    child: MaterialApp(
      home: Scaffold(
        body: NotificationBadge(
          forceShow: forceShow,
          child: const Icon(Icons.notifications_outlined),
        ),
      ),
    ),
  );
}

const NotificationPayload _payload = NotificationPayload(
  type: 'message',
  title: 'New message',
  body: 'You have a message',
);

void main() {
  group('NotificationBadge', () {
    testWidgets('renders child icon without dot when no notification', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(notification: null));

      expect(find.byIcon(Icons.notifications_outlined), findsOneWidget);
      // Keyed badge dot should not be visible
      expect(find.byKey(const Key('notification_badge_dot')), findsNothing);
    });

    testWidgets('renders badge dot when notification is present', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(notification: _payload));

      expect(find.byIcon(Icons.notifications_outlined), findsOneWidget);
      expect(find.byKey(const Key('notification_badge_dot')), findsOneWidget);
    });
  });

  group('NotificationBadge unread count', () {
    testWidgets('wears the count the inbox reported', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(unreadCount: 4));

      expect(find.byKey(const Key('notification_badge_count')), findsOneWidget);
      expect(find.text('4'), findsOneWidget);
      // The count replaces the dot rather than sitting beside it.
      expect(find.byKey(const Key('notification_badge_dot')), findsNothing);
    });

    testWidgets('shows nothing once the inbox is read, even though one arrived', (tester) async {
      // The flaw the count exists to fix: a dot keyed off the last notification
      // that came in stays lit for good, however much has been read since.
      await tester.pumpWidget(
        _buildBadgeApp(notification: _payload, unreadCount: 0),
      );

      expect(find.byKey(const Key('notification_badge_count')), findsNothing);
      expect(find.byKey(const Key('notification_badge_dot')), findsNothing);
    });

    testWidgets('falls back to the dot until the inbox has been counted', (tester) async {
      final provider = NotificationProvider();
      await tester.pumpWidget(
        _buildBadgeApp(notification: _payload, provider: provider),
      );

      expect(find.byKey(const Key('notification_badge_dot')), findsOneWidget);

      // And swaps to the count the moment the inbox knows its number.
      provider.setUnreadCount(2);
      await tester.pump();

      expect(find.byKey(const Key('notification_badge_dot')), findsNothing);
      expect(find.text('2'), findsOneWidget);
    });

    testWidgets('caps a long count at nine plus', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(unreadCount: 27));

      // Past a single digit the exact number stops being useful, and a wider
      // bubble would crowd the icon it hangs off.
      expect(find.text('9+'), findsOneWidget);
    });

    testWidgets('forceShow still pins the dot', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(unreadCount: 4, forceShow: true));

      expect(find.byKey(const Key('notification_badge_dot')), findsOneWidget);
      expect(find.byKey(const Key('notification_badge_count')), findsNothing);
    });

    testWidgets('forceShow false hides a badge the inbox would have shown', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(unreadCount: 4, forceShow: false));

      expect(find.byKey(const Key('notification_badge_count')), findsNothing);
      expect(find.byKey(const Key('notification_badge_dot')), findsNothing);
    });
  });
}
