import 'package:aveline_mobile/core/auth/app_roles.dart';
import 'package:aveline_mobile/core/auth/permissions.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Permissions', () {
    test('contains all 25 canonical permissions matching backend catalog', () {
      // Derived rather than restated: the explicit assertions below name every
      // permission, so the count is the only thing that needs to move.
      expect(Permissions.all, hasLength(25));
      expect(Permissions.catalogView, 'catalog:view');
      expect(Permissions.customersView, 'customers:view');
      expect(Permissions.customersManage, 'customers:manage');
      expect(Permissions.catalogManage, 'catalog:manage');
      expect(Permissions.approvalsApprove, 'approvals:approve');
      expect(Permissions.paymentsRefund, 'payments:refund');
      expect(Permissions.reportsView, 'reports:view');
      expect(Permissions.settingsManage, 'settings:manage');
      expect(Permissions.conversationsView, 'conversations:view');
      expect(Permissions.billingView, 'billing:view');
      expect(Permissions.billingViewSelf, 'billing:view:self');
      expect(Permissions.billingManage, 'billing:manage');
      expect(Permissions.billingAdjust, 'billing:adjust');
      expect(Permissions.pricingView, 'pricing:view');
      expect(Permissions.pricingManage, 'pricing:manage');
      expect(Permissions.pricingBackdate, 'pricing:backdate');
      expect(Permissions.apiKeysView, 'apikeys:view');
      expect(Permissions.apiKeysManage, 'apikeys:manage');
      expect(Permissions.statsView, 'stats:view');
      expect(Permissions.statsViewAgent, 'stats:view:agent');
      expect(Permissions.statsSystem, 'stats:system');
      expect(Permissions.adminUsersRead, 'admin:users:read');
      expect(Permissions.adminUsersManage, 'admin:users:manage');
      expect(Permissions.adminOrgsRead, 'admin:orgs:read');
      expect(Permissions.auditView, 'audit:view');
    });

    test('every org role can read the self-service balance', () {
      for (final role in [
        AppRoles.boutiqueStaff,
        AppRoles.boutiqueManager,
        AppRoles.boutiqueSupervisor,
        AppRoles.boutiqueOwner,
      ]) {
        expect(
          Permissions.isGranted(role, Permissions.billingViewSelf),
          isTrue,
          reason: '$role should hold billing:view:self',
        );
      }
    });

    test('the self-service read does not widen billing:view', () {
      expect(Permissions.isGranted(AppRoles.boutiqueStaff, Permissions.billingView), isFalse);
      expect(Permissions.isGranted(AppRoles.boutiqueSupervisor, Permissions.billingView), isFalse);
      expect(Permissions.isGranted(AppRoles.boutiqueManager, Permissions.billingView), isTrue);
    });

    test('Staff role grants catalog:view and conversations:view only', () {
      expect(Permissions.isGranted(AppRoles.staff, Permissions.catalogView), isTrue);
      expect(Permissions.isGranted(AppRoles.staff, Permissions.conversationsView), isTrue);
      expect(Permissions.isGranted(AppRoles.staff, Permissions.customersView), isFalse);
      expect(Permissions.isGranted(AppRoles.staff, Permissions.catalogManage), isFalse);
      expect(Permissions.isGranted(AppRoles.staff, Permissions.reportsView), isFalse);
    });

    test('BoutiqueStaff role grants catalog:view, customers:view, conversations:view', () {
      expect(Permissions.isGranted(AppRoles.boutiqueStaff, Permissions.catalogView), isTrue);
      expect(Permissions.isGranted(AppRoles.boutiqueStaff, Permissions.customersView), isTrue);
      expect(Permissions.isGranted(AppRoles.boutiqueStaff, Permissions.conversationsView), isTrue);
      expect(Permissions.isGranted(AppRoles.boutiqueStaff, Permissions.catalogManage), isFalse);
    });

    test('Owner role grants all permissions', () {
      for (final permission in Permissions.all) {
        expect(Permissions.isGranted(AppRoles.owner, permission), isTrue,
            reason: 'Owner should have $permission');
      }
    });

    test('Admin role grants all permissions except pricing:backdate', () {
      for (final permission in Permissions.all) {
        if (permission == Permissions.pricingBackdate) {
          expect(Permissions.isGranted(AppRoles.admin, permission), isFalse);
        } else {
          expect(Permissions.isGranted(AppRoles.admin, permission), isTrue,
              reason: 'Admin should have $permission');
        }
      }
    });

    test('Unknown role grants no permissions', () {
      expect(Permissions.isGranted('unknown_role', Permissions.catalogView), isFalse);
      expect(Permissions.isGranted('', Permissions.catalogView), isFalse);
    });
  });

  group('AppRoles', () {
    test('classifies owner roles correctly', () {
      expect(AppRoles.isOwnerRole(AppRoles.owner), isTrue);
      expect(AppRoles.isOwnerRole(AppRoles.boutiqueOwner), isTrue);
      expect(AppRoles.isOwnerRole(AppRoles.staff), isFalse);
      expect(AppRoles.isOwnerRole(AppRoles.boutiqueStaff), isFalse);
      expect(AppRoles.isOwnerRole(AppRoles.boutiqueManager), isFalse);
    });

    test('classifies staff roles correctly', () {
      expect(AppRoles.isStaffRole(AppRoles.staff), isTrue);
      expect(AppRoles.isStaffRole(AppRoles.boutiqueStaff), isTrue);
      expect(AppRoles.isStaffRole(AppRoles.boutiqueManager), isTrue);
      expect(AppRoles.isStaffRole(AppRoles.owner), isFalse);
      expect(AppRoles.isStaffRole(AppRoles.boutiqueOwner), isFalse);
    });
  });
}
