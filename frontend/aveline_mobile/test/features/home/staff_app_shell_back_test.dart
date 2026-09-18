import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/home/presentation/screens/staff_app_shell.dart';
import 'package:aveline_mobile/shared/widgets/quit_confirmation_dialog.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

UserProvider _signedInProvider() {
  final provider = UserProvider();
  provider.setUser(
    AvelineUser(
      id: 'u1',
      clerkId: 'c1',
      email: 'staff@aveline.com',
      firstName: 'Staff',
      lastName: 'User',
      username: 'staffuser',
      userRole: AppRoles.staff,
      organizationRole: '',
      organizationId: 'org_1',
      hasCompletedOnboarding: true,
      accountState: AvelineAccountState.active,
      contactPreference: 'email',
      pushNotificationsEnabled: true,
      isActive: true,
      createdAt: DateTime(2025, 1, 1),
      updatedAt: DateTime(2025, 1, 1),
    ),
  );
  return provider;
}

/// Advances the tree past the current transition.
///
/// `pumpAndSettle` cannot be used on this shell: the floating [AnimatedBlossom]
/// pulses on a repeating controller, so the tree never reaches a settled state.
/// Three frames because a popped route is only removed from the overlay on the
/// frame after its exit transition completes.
Future<void> _settle(WidgetTester tester) async {
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 400));
  await tester.pump(const Duration(milliseconds: 400));
}

Future<void> _pumpShell(
  WidgetTester tester, {
  bool isAtRoot = true,
}) async {
  final navigatorKey = GlobalKey<NavigatorState>();
  final shell = StaffAppShell(
    isAtRoot: isAtRoot,
    child: const Text('Shell Content'),
  );

  await tester.pumpWidget(
    ChangeNotifierProvider<UserProvider>.value(
      value: _signedInProvider(),
      child: MaterialApp(
        navigatorKey: navigatorKey,
        home: isAtRoot
            ? shell
            : const Scaffold(body: Text('Previous Screen')),
      ),
    ),
  );
  await _settle(tester);

  // A deeper screen only exists on top of another one, so push it rather than
  // mounting it as the first route.
  if (!isAtRoot) {
    navigatorKey.currentState!.push(
      MaterialPageRoute<void>(builder: (_) => shell),
    );
    await _settle(tester);
  }
}

/// Simulates the Android system back button.
Future<void> _pressSystemBack(WidgetTester tester) async {
  await tester.binding.handlePopRoute();
  await _settle(tester);
}

/// Every method the shell sent over the `flutter/platform` channel, so a test
/// can assert whether the app actually asked the OS to close it.
final List<String> _platformCalls = <String>[];

void main() {
  setUp(() {
    _platformCalls.clear();
    TestWidgetsFlutterBinding.ensureInitialized();
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, (call) async {
      _platformCalls.add(call.method);
      return null;
    });
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(SystemChannels.platform, null);
  });

  group('StaffAppShell back handling', () {
    testWidgets('back at the root screen asks before closing the app',
        (tester) async {
      await _pumpShell(tester);

      await _pressSystemBack(tester);

      expect(find.byType(AlertDialog), findsOneWidget);
      expect(find.text('Leave Aveline?'), findsOneWidget);
      expect(find.byKey(const Key('quit_dialog_cancel')), findsOneWidget);
      expect(find.byKey(const Key('quit_dialog_confirm')), findsOneWidget);

      // Nothing has closed yet: the confirmation is still unanswered.
      expect(_platformCalls, isNot(contains('SystemNavigator.pop')));
    });

    testWidgets('cancelling the confirmation keeps the app open',
        (tester) async {
      await _pumpShell(tester);

      await _pressSystemBack(tester);
      await tester.tap(find.byKey(const Key('quit_dialog_cancel')));
      await _settle(tester);

      expect(find.byType(AlertDialog), findsNothing);
      expect(_platformCalls, isNot(contains('SystemNavigator.pop')));
      expect(find.text('Shell Content'), findsOneWidget);
    });

    testWidgets('confirming the quit closes the app', (tester) async {
      await _pumpShell(tester);

      await _pressSystemBack(tester);
      await tester.tap(find.byKey(const Key('quit_dialog_confirm')));
      await _settle(tester);

      expect(find.byType(AlertDialog), findsNothing);
      expect(_platformCalls, contains('SystemNavigator.pop'));
    });

    testWidgets('back on a deeper screen returns to the previous screen',
        (tester) async {
      await _pumpShell(tester, isAtRoot: false);
      expect(find.text('Shell Content'), findsOneWidget);

      await _pressSystemBack(tester);

      expect(find.byType(AlertDialog), findsNothing);
      expect(_platformCalls, isNot(contains('SystemNavigator.pop')));
      expect(find.text('Shell Content'), findsNothing);
      expect(find.text('Previous Screen'), findsOneWidget);
    });

    testWidgets('back closes the drawer before leaving a deeper screen',
        (tester) async {
      await _pumpShell(tester, isAtRoot: false);

      await tester.tap(find.byKey(const Key('aveline_header_menu_button')));
      await _settle(tester);
      expect(
        tester.state<ScaffoldState>(find.byType(Scaffold)).isDrawerOpen,
        isTrue,
      );

      await _pressSystemBack(tester);

      // The drawer is its own step back; the screen itself stays put.
      expect(
        tester.state<ScaffoldState>(find.byType(Scaffold)).isDrawerOpen,
        isFalse,
      );
      expect(find.text('Shell Content'), findsOneWidget);
    });
  });

  group('showQuitConfirmationDialog', () {
    testWidgets('returns true only when confirmed', (tester) async {
      bool? result;

      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) => ElevatedButton(
              onPressed: () async {
                result = await showQuitConfirmationDialog(context);
              },
              child: const Text('Ask'),
            ),
          ),
        ),
      );

      await tester.tap(find.text('Ask'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('quit_dialog_confirm')));
      await tester.pumpAndSettle();

      expect(result, isTrue);
    });

    testWidgets('barrier taps cannot dismiss the confirmation', (tester) async {
      await tester.pumpWidget(
        MaterialApp(
          home: Builder(
            builder: (context) => ElevatedButton(
              onPressed: () => showQuitConfirmationDialog(context),
              child: const Text('Ask'),
            ),
          ),
        ),
      );

      await tester.tap(find.text('Ask'));
      await tester.pumpAndSettle();

      // Tap well above the dialog, on the scrim.
      await tester.tapAt(const Offset(10, 10));
      await tester.pumpAndSettle();

      expect(find.byType(AlertDialog), findsOneWidget);
    });
  });
}
