/// Role values matching the backend `Roles.cs` and Clerk JWT claims:
///   `user_role` = {{user.public_metadata.role}} (Aveline team role)
///   `org_role`  = {{org.role}}                 (per-store owner/staff role)
abstract final class AppRoles {
  // Aveline team roles
  static const String staff = 'staff';
  static const String customerRelations = 'customer_relations';
  static const String moderator = 'moderator';
  static const String admin = 'admin';
  static const String owner = 'owner';

  // Per-boutique roles
  static const String boutiqueStaff = 'org:boutique_staff';
  static const String boutiqueManager = 'org:boutique_manager';
  static const String boutiqueSupervisor = 'org:boutique_supervisor';
  static const String boutiqueOwner = 'org:boutique_owner';

  /// True for roles that load the Owner app shell.
  static bool isOwnerRole(String role) =>
      role == owner || role == boutiqueOwner;

  /// True for any authenticated role that loads the Staff app shell.
  static bool isStaffRole(String role) => !isOwnerRole(role);
}
