// Device-level integration test for Aveline.
//
// What this exercises
// -------------------
// It launches the **real** app (`main()`) on a real device and walks the
// journey that needs no reachable API:
//
//   1. the app boots behind its opening screen;
//   2. the bootstrap hands off to the real router;
//   3. a signed-out visitor is held on the sign-in screen by the real guard;
//   4. the sign-in screen's sign-up segment opens the real sign-up form.
//
// The Aveline API is not required: none of those steps read it, so the test is
// green on a device with no backend (stated plainly below). Pointing
// `API_BASE_URL` at the composed stack is what makes the later, API-backed walk
// possible; this file is the device-run skeleton that walk extends.
//
// Running locally (a connected device or emulator)
// ------------------------------------------------
//   cd frontend/aveline_mobile
//   flutter devices                          # find the device id
//   flutter test integration_test -d <device-id>
//
// Running against the composed stack
// ----------------------------------
//   flutter test integration_test -d <device-id> \
//     --dart-define=API_BASE_URL=http://10.0.2.2:5091 \
//     --dart-define=CLERK_PUBLISHABLE_KEY=pk_test_...
//
// (`10.0.2.2` is the host as seen from the Android emulator; use the host's LAN
// address for a physical device.)
//
// Running in CI (Linux, headless, Android emulator)
// -------------------------------------------------
// A device is required: `flutter test integration_test` with no `-d` runs on the
// host and is not a device run. The CI job creates one first, then runs the same
// command against it. See the sketch in the job that owns this file:
//
//   - uses: reactivecircus/android-emulator-runner@v2
//     with:
//       api-level: 35
//       arch: x86_64
//       target: google_apis
//       script: flutter test integration_test -d emulator-5554
//
// Add `--dart-define=API_BASE_URL=...` to that script once a composed stack is
// up in CI.
//
// What a failure here means
// -------------------------
// If Clerk is unreachable the app is expected to show its own retry screen, not
// to hang or crash. The test accepts that outcome and asserts the retry surface,
// printing which branch it took; it only fails when the app never leaves the
// opening screen or a surface renders without its controls.
//
// Keep the device's screen awake. `tester.pump` waits for a frame, and a device
// whose display is off produces none, so the run stalls until the screen comes
// back on. On CI the emulator boots awake by default; for a physical device:
//
//   adb shell svc power stayon usb && adb shell input keyevent KEYCODE_WAKEUP

import 'package:aveline_mobile/features/auth/presentation/screens/auth_screen.dart';
import 'package:aveline_mobile/main.dart' as app;
import 'package:aveline_mobile/shared/widgets/aveline_loading_screen.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:integration_test/integration_test.dart';

/// Pumps frames until one of [finders] matches, or [timeout] elapses.
///
/// Real time has to pass between frames for the bootstrap's own network futures
/// to complete; a bare `pump` only advances the frame.
Future<Finder?> _waitForAny(
  WidgetTester tester,
  List<Finder> finders, {
  Duration timeout = const Duration(seconds: 45),
}) async {
  final deadline = DateTime.now().add(timeout);
  while (DateTime.now().isBefore(deadline)) {
    for (final finder in finders) {
      if (finder.evaluate().isNotEmpty) {
        return finder;
      }
    }
    await tester.pump(const Duration(milliseconds: 100));
    await Future<void>.delayed(const Duration(milliseconds: 100));
  }
  return null;
}

void main() {
  IntegrationTestWidgetsFlutterBinding.ensureInitialized();

  testWidgets('the app boots and guards a signed-out visitor to sign-in', (
    tester,
  ) async {
    await app.main();
    await tester.pump(const Duration(milliseconds: 100));

    // 1. The real app is up, behind its opening screen.
    expect(
      find.byType(AvelineLoadingScreen),
      findsOneWidget,
      reason: 'the app did not render its opening screen',
    );

    final authScreen = find.byType(AuthScreen);
    final retryButton = find.byKey(const Key('loading_retry_button'));

    // 2. Wait for the bootstrap to reach a terminal surface.
    final settled = await _waitForAny(tester, [authScreen, retryButton]);
    expect(
      settled,
      isNotNull,
      reason: 'the app never left its opening screen',
    );

    if (authScreen.evaluate().isNotEmpty) {
      // 3. The signed-out guard put the visitor on /auth, which is where the
      //    router sends anyone who is not signed in.
      expect(find.text('AVELINE ASSISTANT'), findsOneWidget);
      expect(find.text('Sign in'), findsWidgets);

      // 4. A real, deterministic journey that needs no backend: open the
      //    sign-up form the same way an associate would.
      await tester.tap(find.text('Sign up'));
      await tester.pump(const Duration(milliseconds: 600));

      expect(find.text('Create account'), findsOneWidget);
      expect(find.widgetWithText(TextFormField, 'Email'), findsOneWidget);
    } else {
      // Clerk was unreachable. The app must say so and offer a retry rather
      // than hang or crash; that is the correct no-backend outcome, and the log
      // records which branch ran.
      debugPrint(
        '[integration] Clerk unreachable: asserted the retry surface instead '
        'of the signed-out guard.',
      );
      expect(retryButton, findsOneWidget);
      expect(find.text('Try again'), findsOneWidget);
    }
  });
}
