import 'package:aveline_mobile/core/notifications/notification_payload.dart';
import 'package:aveline_mobile/core/notifications/notification_provider.dart';
import 'package:aveline_mobile/shared/widgets/notification_badge.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

Widget _buildBadgeApp({NotificationPayload? notification}) {
  final provider = NotificationProvider();
  if (notification != null) {
    provider.push(notification);
  }
  return ChangeNotifierProvider<NotificationProvider>.value(
    value: provider,
    child: const MaterialApp(
      home: Scaffold(
        body: NotificationBadge(
          child: Icon(Icons.notifications_outlined),
        ),
      ),
    ),
  );
}

void main() {
  group('NotificationBadge', () {
    testWidgets('renders child icon without dot when no notification', (tester) async {
      await tester.pumpWidget(_buildBadgeApp(notification: null));

      expect(find.byIcon(Icons.notifications_outlined), findsOneWidget);
      // Keyed badge dot should not be visible
      expect(find.byKey(const Key('notification_badge_dot')), findsNothing);
    });

    testWidgets('renders badge dot when notification is present', (tester) async {
      const payload = NotificationPayload(
        type: 'message',
        title: 'New message',
        body: 'You have a message',
      );

      await tester.pumpWidget(_buildBadgeApp(notification: payload));

      expect(find.byIcon(Icons.notifications_outlined), findsOneWidget);
      expect(find.byKey(const Key('notification_badge_dot')), findsOneWidget);
    });
  });
}
