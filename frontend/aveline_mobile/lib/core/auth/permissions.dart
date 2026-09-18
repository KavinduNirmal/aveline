import 'app_roles.dart';

/// Permission names and canonical role-to-permission grants matching the backend
/// `Aveline.Api/Authorization/Permissions.cs`.
abstract final class Permissions {
  // Catalog & customers
  static const String catalogView = 'catalog:view';
  static const String customersView = 'customers:view';
  static const String catalogManage = 'catalog:manage';
  static const String approvalsApprove = 'approvals:approve';
  static const String paymentsRefund = 'payments:refund';
  static const String reportsView = 'reports:view';
  static const String settingsManage = 'settings:manage';
  static const String conversationsView = 'conversations:view';

  // Billing and pricing
  static const String billingView = 'billing:view';

  /// The self-service read of the shop's Blossom position, held by every org
  /// role. Distinct from [billingView], which reaches usage statements and
  /// burn-rate and is held only by managers and owners.
  static const String billingViewSelf = 'billing:view:self';
  static const String billingManage = 'billing:manage';
  static const String billingAdjust = 'billing:adjust';
  static const String pricingView = 'pricing:view';
  static const String pricingManage = 'pricing:manage';
  static const String pricingBackdate = 'pricing:backdate';

  // API access
  static const String apiKeysView = 'apikeys:view';
  static const String apiKeysManage = 'apikeys:manage';

  // Statistics
  static const String statsView = 'stats:view';
  static const String statsViewAgent = 'stats:view:agent';
  static const String statsSystem = 'stats:system';

  // Aveline-team administration
  static const String adminUsersRead = 'admin:users:read';
  static const String adminUsersManage = 'admin:users:manage';
  static const String adminOrgsRead = 'admin:orgs:read';
  static const String auditView = 'audit:view';

  /// Complete set of all 24 registered permissions.
  static const Set<String> all = {
    catalogView,
    customersView,
    catalogManage,
    approvalsApprove,
    paymentsRefund,
    reportsView,
    settingsManage,
    conversationsView,
    billingView,
    billingViewSelf,
    billingManage,
    billingAdjust,
    pricingView,
    pricingManage,
    pricingBackdate,
    apiKeysView,
    apiKeysManage,
    statsView,
    statsViewAgent,
    statsSystem,
    adminUsersRead,
    adminUsersManage,
    adminOrgsRead,
    auditView,
  };

  static final Map<String, Set<String>> _rolePermissions = {
    AppRoles.staff: {
      catalogView,
      conversationsView,
    },
    AppRoles.customerRelations: {
      catalogView,
      customersView,
      conversationsView,
    },
    AppRoles.moderator: {
      catalogView,
      customersView,
      approvalsApprove,
      conversationsView,
      billingView,
      statsView,
      statsViewAgent,
      adminOrgsRead,
    },
    AppRoles.admin: all.where((p) => p != pricingBackdate).toSet(),
    AppRoles.owner: all,

    AppRoles.boutiqueStaff: {
      catalogView,
      customersView,
      conversationsView,
      billingViewSelf,
    },
    AppRoles.boutiqueManager: {
      catalogView,
      customersView,
      catalogManage,
      reportsView,
      conversationsView,
      billingView,
      billingViewSelf,
      pricingView,
      statsView,
    },
    AppRoles.boutiqueSupervisor: {
      catalogView,
      customersView,
      catalogManage,
      approvalsApprove,
      reportsView,
      conversationsView,
      billingViewSelf,
      statsView,
    },
    AppRoles.boutiqueOwner: {
      catalogView,
      customersView,
      catalogManage,
      approvalsApprove,
      paymentsRefund,
      reportsView,
      settingsManage,
      conversationsView,
      billingView,
      billingViewSelf,
      billingManage,
      pricingView,
      apiKeysView,
      apiKeysManage,
      statsView,
      statsViewAgent,
    },
  };

  /// Returns whether a given canonical role has been granted [permission].
  static bool isGranted(String role, String permission) {
    final grants = _rolePermissions[role];
    return grants != null && grants.contains(permission);
  }

  /// Returns whether any of the provided [roles] grants [permission].
  static bool anyGranted(Iterable<String?> roles, String permission) {
    for (final role in roles) {
      if (role != null && isGranted(role, permission)) {
        return true;
      }
    }
    return false;
  }
}
