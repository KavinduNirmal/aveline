import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/auth/permission_guard.dart';
import 'package:aveline_mobile/core/auth/permissions.dart';
import 'package:aveline_mobile/core/providers/user_provider.dart';
import 'package:aveline_mobile/features/auth/domain/aveline_user.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:provider/provider.dart';

AvelineUser _testUser({String userRole = '', String orgRole = ''}) {
  return AvelineUser(
    id: 'user_1',
    clerkId: 'clk_1',
    email: 'test@example.com',
    firstName: 'Jane',
    lastName: 'Doe',
    username: 'janedoe',
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

Widget _wrapWithUser(Widget child, {AvelineUser? user}) {
  final provider = UserProvider();
  if (user != null) {
    provider.setUser(user);
  }
  return ChangeNotifierProvider<UserProvider>.value(
    value: provider,
    child: MaterialApp(home: Scaffold(body: child)),
  );
}

void main() {
  group('PermissionGuard', () {
    testWidgets('renders child when permission is null', (tester) async {
      await tester.pumpWidget(
        _wrapWithUser(
          const PermissionGuard(
            permission: null,
            child: Text('Always visible'),
          ),
          user: _testUser(userRole: AppRoles.staff),
        ),
      );

      expect(find.text('Always visible'), findsOneWidget);
    });

    testWidgets('renders child when userRole grants permission', (tester) async {
      await tester.pumpWidget(
        _wrapWithUser(
          const PermissionGuard(
            permission: Permissions.catalogView,
            child: Text('Catalog visible'),
          ),
          user: _testUser(userRole: AppRoles.staff),
        ),
      );

      expect(find.text('Catalog visible'), findsOneWidget);
    });

    testWidgets('renders child when organizationRole grants permission', (tester) async {
      await tester.pumpWidget(
        _wrapWithUser(
          const PermissionGuard(
            permission: Permissions.customersView,
            child: Text('Customers visible'),
          ),
          user: _testUser(
            userRole: AppRoles.staff,
            orgRole: AppRoles.boutiqueStaff, // boutiqueStaff has customers:view
          ),
        ),
      );

      expect(find.text('Customers visible'), findsOneWidget);
    });

    testWidgets('renders fallback when permission is not granted', (tester) async {
      await tester.pumpWidget(
        _wrapWithUser(
          const PermissionGuard(
            permission: Permissions.settingsManage,
            fallback: Text('Access Denied'),
            child: Text('Secret Settings'),
          ),
          user: _testUser(userRole: AppRoles.staff),
        ),
      );

      expect(find.text('Secret Settings'), findsNothing);
      expect(find.text('Access Denied'), findsOneWidget);
    });

    testWidgets('renders fallback (default SizedBox.shrink) when no user signed in', (tester) async {
      await tester.pumpWidget(
        _wrapWithUser(
          const PermissionGuard(
            permission: Permissions.catalogView,
            child: Text('Catalog visible'),
          ),
          user: null,
        ),
      );

      expect(find.text('Catalog visible'), findsNothing);
    });
  });
}
