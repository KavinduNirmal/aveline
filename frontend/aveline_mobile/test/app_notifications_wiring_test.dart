import 'dart:io';

import 'package:flutter_test/flutter_test.dart';

/// The regression guard the notifications plan says is missing: nothing asserted
/// which repository the shell selects, so the demo inbox could quietly return to
/// the live path and every test would still pass (they all inject their own
/// repository).
///
/// The shell cannot be driven to `/notifications` with a signed-in session in a
/// widget test without a Clerk bootstrap, a live Dio and Firebase, which is why
/// this is a source-level assertion over the wiring block rather than a widget
/// test (the plan's S0 gate default).
void main() {
  group('app notification wiring', () {
    final source = File('lib/app.dart').readAsStringSync();

    test('the shell selects the API notification repository', () {
      expect(
        source,
        contains('ApiNotificationRepository(_dio)'),
        reason: 'A signed-in user must read the live inbox, not a demo seed.',
      );
    });

    test('the shell imports the API repository', () {
      expect(
        source,
        contains(
          "import 'features/notifications/data/api_notification_repository.dart';",
        ),
      );
    });

    test('no demo notification repository is constructed on the live path', () {
      expect(
        source,
        isNot(contains('DemoNotificationRepository')),
        reason:
            'The demo inbox belongs to tests and previews; the live path must '
            'not select it.',
      );
    });
  });
}
