import 'package:aveline_mobile/core/navigation/screen_config.dart';
import 'package:aveline_mobile/core/navigation/staff_screens.dart';
import 'package:aveline_mobile/core/router/route_guards.dart';
import 'package:flutter_test/flutter_test.dart';

Map<String, ScreenConfig> _byId() => {
  for (final screen in staffScreens()) screen.id: screen,
};

void main() {
  group('staffScreens', () {
    test('lists every destination the side panel shows, in order', () {
      expect(
        staffScreens().map((screen) => screen.id),
        ['home', 'customers', 'catalog', 'conversations', 'notifications', 'settings'],
      );
    });

    test('routes each row at the destination the router serves', () {
      final screens = _byId();

      expect(screens['home']!.route, AppRoutes.home);
      expect(screens['customers']!.route, AppRoutes.customers);
      expect(screens['catalog']!.route, AppRoutes.catalog);
      expect(screens['conversations']!.route, AppRoutes.conversations);
      expect(screens['notifications']!.route, AppRoutes.notifications);
      expect(screens['settings']!.route, AppRoutes.settings);
    });

    test('calls the inbox the same word the screen titles itself', () {
      // The panel used to read `Conversations` while the screen it opened read
      // `Messages`, which left the associate wondering whether they matched.
      expect(_byId()['conversations']!.label, 'Messages');
      // The route keeps its name: only the label was ever wrong.
      expect(_byId()['conversations']!.route, '/conversations');
    });

    test('leaves the personal destinations ungated', () {
      // These hold the associate's own account and inbox rather than the shop's
      // settings, so every role has to reach them: only the shop-wide block
      // inside Settings is permission-gated.
      expect(_byId()['notifications']!.permission, isNull);
      expect(_byId()['settings']!.permission, isNull);
      expect(_byId()['home']!.permission, isNull);
    });

    test('gives every row an icon and a filled variant of it', () {
      for (final screen in staffScreens()) {
        expect(
          screen.activeIcon,
          isNotNull,
          reason: '${screen.id} has no active icon to mark it selected with',
        );
        expect(
          screen.icon,
          isNot(screen.activeIcon),
          reason: '${screen.id} would not change when it became the active row',
        );
      }
    });
  });
}
