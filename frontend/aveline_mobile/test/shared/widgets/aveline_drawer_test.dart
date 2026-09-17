import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/auth/permissions.dart';
import 'package:aveline_mobile/core/navigation/screen_config.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:aveline_mobile/shared/widgets/aveline_drawer.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

AvelineUser _testUser({
  String userRole = AppRoles.staff,
  String orgRole = '',
  String firstName = 'Charlotte',
  String lastName = 'Tilbury',
}) {
  return AvelineUser(
    id: 'usr_1',
    clerkId: 'clk_1',
    email: 'charlotte@aveline.com',
    firstName: firstName,
    lastName: lastName,
    username: 'charlotte',
    userRole: userRole,
    organizationRole: orgRole,
    organizationId: 'org_1',
    hasCompletedOnboarding: true,
    accountState: AvelineAccountState.active,
    contactPreference: 'email',
    pushNotificationsEnabled: true,
    isActive: true,
    createdAt: DateTime(2025, 1, 1),
    updatedAt: DateTime(2025, 1, 1),
  );
}

final _testScreens = [
  ScreenConfig(
    id: 'home',
    label: 'Home',
    icon: Icons.home_outlined,
    activeIcon: Icons.home_rounded,
    route: '/',
    permission: null,
    builder: (_) => const SizedBox(),
  ),
  ScreenConfig(
    id: 'catalog',
    label: 'Catalog',
    icon: Icons.checkroom_outlined,
    activeIcon: Icons.checkroom_rounded,
    route: '/catalog',
    permission: Permissions.catalogView,
    builder: (_) => const SizedBox(),
  ),
  ScreenConfig(
    id: 'secret_reports',
    label: 'Reports',
    icon: Icons.analytics_outlined,
    activeIcon: Icons.analytics_rounded,
    route: '/reports',
    permission: Permissions.reportsView, // staff does NOT have reportsView
    builder: (_) => const SizedBox(),
  ),
];

Widget _buildDrawerApp({
  required AvelineUser user,
  String currentRoute = '/',
  ValueChanged<String>? onNavigate,
  VoidCallback? onSignOut,
}) {
  final provider = UserProvider();
  provider.setUser(user);

  return ChangeNotifierProvider<UserProvider>.value(
    value: provider,
    child: MaterialApp(
      home: Scaffold(
        drawer: AvelineDrawer(
          screens: _testScreens,
          currentRoute: currentRoute,
          onNavigate: onNavigate,
          onSignOut: onSignOut,
        ),
        body: Builder(
          builder: (context) => Center(
            child: ElevatedButton(
              onPressed: () => Scaffold.of(context).openDrawer(),
              child: const Text('Open Drawer'),
            ),
          ),
        ),
      ),
    ),
  );
}

void main() {
  group('AvelineDrawer', () {
    testWidgets('renders user header with name and role', (tester) async {
      await tester.pumpWidget(
        _buildDrawerApp(
          user: _testUser(firstName: 'Eleanor', lastName: 'Vane', userRole: AppRoles.staff),
        ),
      );

      // Open drawer
      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      expect(find.text('Eleanor Vane'), findsOneWidget);
      expect(find.text('staff'), findsOneWidget);
    });

    testWidgets('filters screens by permission: shows Home and Catalog, hides Reports for staff', (tester) async {
      await tester.pumpWidget(
        _buildDrawerApp(
          user: _testUser(userRole: AppRoles.staff),
        ),
      );

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      expect(find.text('Home'), findsOneWidget);
      expect(find.text('Catalog'), findsOneWidget);
      // Reports requires reports:view which staff lacks
      expect(find.text('Reports'), findsNothing);
    });

    testWidgets('shows Reports when user holds boutique_owner role', (tester) async {
      await tester.pumpWidget(
        _buildDrawerApp(
          user: _testUser(userRole: AppRoles.staff, orgRole: AppRoles.boutiqueOwner),
        ),
      );

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      expect(find.text('Reports'), findsOneWidget);
    });

    testWidgets('tapping nav item calls onNavigate with route', (tester) async {
      String? navigatedRoute;

      await tester.pumpWidget(
        _buildDrawerApp(
          user: _testUser(userRole: AppRoles.staff),
          onNavigate: (route) => navigatedRoute = route,
        ),
      );

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      await tester.tap(find.text('Catalog'));
      await tester.pumpAndSettle();

      expect(navigatedRoute, '/catalog');
    });

    testWidgets('renders sign-out item and triggers onSignOut', (tester) async {
      var signedOut = false;

      await tester.pumpWidget(
        _buildDrawerApp(
          user: _testUser(),
          onSignOut: () => signedOut = true,
        ),
      );

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      expect(find.text('Sign Out'), findsOneWidget);

      await tester.tap(find.text('Sign Out'));
      await tester.pumpAndSettle();

      expect(signedOut, isTrue);
    });

    testWidgets('stacks nav rows on the 4px rhythm', (tester) async {
      await tester.pumpWidget(_buildDrawerApp(user: _testUser()));

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      // Measure the pill surfaces themselves, not the labels, so the gap is the
      // real space between two rounded rows.
      final homeRect = tester.getRect(
        find.ancestor(
          of: find.text('Home'),
          matching: find.byType(Material),
        ).first,
      );
      final catalogRect = tester.getRect(
        find.ancestor(
          of: find.text('Catalog'),
          matching: find.byType(Material),
        ).first,
      );

      expect(catalogRect.top, greaterThanOrEqualTo(homeRect.bottom));
      expect(
        catalogRect.top - homeRect.bottom,
        moreOrLessEquals(AvelineDrawer.navItemGap, epsilon: 0.5),
        reason: 'nav rows should be separated by the 4px gap',
      );
    });

    testWidgets('selected row shows the primary tint on its pill', (tester) async {
      await tester.pumpWidget(
        _buildDrawerApp(user: _testUser(), currentRoute: '/catalog'),
      );

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      final selectedPill = tester.widget<Material>(
        find.ancestor(
          of: find.text('Catalog'),
          matching: find.byType(Material),
        ).first,
      );

      expect(selectedPill.shape, isA<StadiumBorder>());
      expect(selectedPill.color?.a, closeTo(0.10, 0.01));
    });

    testWidgets('outlines only the active row', (tester) async {
      await tester.pumpWidget(
        _buildDrawerApp(user: _testUser(), currentRoute: '/catalog'),
      );

      await tester.tap(find.text('Open Drawer'));
      await tester.pumpAndSettle();

      StadiumBorder pillShapeFor(String label) => tester
          .widget<Material>(
            find.ancestor(
              of: find.text(label),
              matching: find.byType(Material),
            ).first,
          )
          .shape! as StadiumBorder;

      final active = pillShapeFor('Catalog').side;
      final idle = pillShapeFor('Home').side;

      expect(active.color.a, greaterThan(0), reason: 'active row is outlined');
      expect(idle.color.a, 0, reason: 'idle rows carry no visible outline');
      // Same width, so revealing the outline on selection shifts nothing.
      expect(idle.width, active.width);
    });
  });
}
