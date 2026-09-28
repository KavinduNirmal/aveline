import 'package:aveline_mobile/core/theme/app_theme.dart';
import 'package:aveline_mobile/features/notifications/domain/app_notification.dart';
import 'package:aveline_mobile/features/notifications/domain/notification_kind.dart';
import 'package:aveline_mobile/features/notifications/presentation/widgets/notification_kind_visuals.dart';
import 'package:aveline_mobile/features/notifications/presentation/widgets/notification_tile.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The two kinds that shipped without visuals must wear their own mark.
///
/// Before this slice `IntegrationExpired` (live) and `SystemAlert` fell to the
/// grey `_fallback`, so a real integration expiry read like nothing at all.
void main() {
  AppNotification notification(String type) => AppNotification(
    id: 'n-1',
    notificationId: 'nr-1',
    type: type,
    title: 'A title',
    body: 'A body',
    createdAt: DateTime.utc(2026, 9, 17, 12),
  );

  Widget wrap(AppNotification item) => MaterialApp(
    theme: AppTheme.light,
    home: MediaQuery(
      data: const MediaQueryData(
        disableAnimations: true,
        size: Size(390, 844),
      ),
      child: Scaffold(
        body: NotificationTile(
          notification: item,
          expanded: false,
          onToggle: () {},
          onMarkRead: () async {},
          onDelete: () {},
        ),
      ),
    ),
  );

  /// The enclosure's icon: the only `Icon` wearing the kind's own tint.
  Finder markFor(NotificationKindVisuals visuals) => find.byWidgetPredicate(
    (widget) =>
        widget is Icon &&
        widget.icon == visuals.icon &&
        widget.color == visuals.tint,
  );

  group('NotificationTile visuals', () {
    testWidgets('an IntegrationExpired tile wears the link-off mark', (
      tester,
    ) async {
      await tester.pumpWidget(wrap(notification('IntegrationExpired')));
      await tester.pumpAndSettle();

      expect(
        markFor(
          const NotificationKindVisuals(
            icon: Icons.link_off_rounded,
            tint: Color(0xFF8A4B3C),
          ),
        ),
        findsOneWidget,
      );
    });

    testWidgets('a SystemAlert tile wears the problem mark', (tester) async {
      await tester.pumpWidget(wrap(notification('SystemAlert')));
      await tester.pumpAndSettle();

      expect(
        markFor(
          const NotificationKindVisuals(
            icon: Icons.report_problem_outlined,
            tint: Color(0xFF9E3B33),
          ),
        ),
        findsOneWidget,
      );
    });

    testWidgets('neither new kind falls back to the neutral mark', (
      tester,
    ) async {
      final fallback = NotificationKindVisuals.of(NotificationKind.unknown);

      for (final type in ['IntegrationExpired', 'SystemAlert']) {
        final visuals = NotificationKindVisuals.of(
          NotificationKind.fromType(type),
        );
        expect(visuals.icon, isNot(fallback.icon), reason: type);
        expect(visuals.tint, isNot(fallback.tint), reason: type);
      }
    });

    testWidgets('a NewMessage tile wears the wine chat mark', (tester) async {
      // The whole point of carrying `type` in the push payload: a message that
      // reached the app as a push renders as itself, not as the grey fallback.
      await tester.pumpWidget(wrap(notification('NewMessage')));
      await tester.pumpAndSettle();

      expect(
        markFor(
          const NotificationKindVisuals(
            icon: Icons.chat_bubble_outline_rounded,
            tint: Color(0xFF8B2E42),
          ),
        ),
        findsOneWidget,
      );
    });
  });
}
