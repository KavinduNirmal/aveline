import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/features/home/presentation/screens/staff_app_shell.dart';
import 'package:aveline_mobile/shared/widgets/animated_blossom.dart';
import 'package:aveline_mobile/shared/widgets/aveline_drawer.dart';
import 'package:aveline_mobile/shared/widgets/aveline_header.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

Widget _buildStaffShellApp({
  bool showHeader = true,
  bool showBlossom = true,
  Widget child = const Text('Staff Dashboard Content'),
}) {
  final userProvider = UserProvider();
  userProvider.setUser(
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

  return ChangeNotifierProvider<UserProvider>.value(
    value: userProvider,
    child: MaterialApp(
      home: StaffAppShell(
        showHeader: showHeader,
        showBlossom: showBlossom,
        child: child,
      ),
    ),
  );
}

void main() {
  group('StaffAppShell', () {
    testWidgets('renders child content and universal header when showHeader is true', (tester) async {
      await tester.pumpWidget(_buildStaffShellApp(showHeader: true));

      expect(find.text('Staff Dashboard Content'), findsOneWidget);
      expect(find.byType(AvelineHeader), findsOneWidget);
      expect(find.byType(AnimatedBlossom), findsOneWidget);
    });

    testWidgets('hides universal header when showHeader is false', (tester) async {
      await tester.pumpWidget(_buildStaffShellApp(showHeader: false));

      expect(find.text('Staff Dashboard Content'), findsOneWidget);
      expect(find.byType(AvelineHeader), findsNothing);
      expect(find.byType(AnimatedBlossom), findsOneWidget);
    });

    testWidgets('hides animated blossom when showBlossom is false', (tester) async {
      await tester.pumpWidget(_buildStaffShellApp(showBlossom: false));

      expect(find.byType(AnimatedBlossom), findsNothing);
    });

    testWidgets('has AvelineDrawer accessible in Scaffold', (tester) async {
      await tester.pumpWidget(_buildStaffShellApp());

      // Open drawer using the hamburger menu button
      await tester.tap(find.byKey(const Key('aveline_header_menu_button')));
      await tester.pump(const Duration(milliseconds: 350));

      expect(find.byType(AvelineDrawer), findsOneWidget);
    });

    testWidgets('widens the drawer edge-drag strip past the default 20px',
        (tester) async {
      await tester.pumpWidget(_buildStaffShellApp());

      final scaffold = tester.widget<Scaffold>(find.byType(Scaffold));

      // The wider strip is what lets a swipe near the bezel open the drawer
      // instead of being claimed by the Android back gesture. Flutter's own
      // default is 20 logical pixels.
      expect(scaffold.drawerEdgeDragWidth, StaffAppShell.drawerEdgeDragWidth);
      expect(StaffAppShell.drawerEdgeDragWidth, greaterThan(20));
    });

    testWidgets('an edge drag opens the drawer', (tester) async {
      await tester.pumpWidget(_buildStaffShellApp());

      // Start the drag inside the widened strip, just past the flutter default
      // of 20px, which the old configuration would have ignored.
      const start = Offset(24, 300);
      await tester.dragFrom(start, const Offset(260, 0));
      // The floating blossom pulses forever, so the drawer animation is stepped
      // explicitly rather than waiting for the tree to settle.
      await tester.pump();
      await tester.pump(const Duration(milliseconds: 400));

      expect(find.byType(AvelineDrawer), findsOneWidget);
      expect(
        tester.state<ScaffoldState>(find.byType(Scaffold)).isDrawerOpen,
        isTrue,
      );
    });
  });
}
