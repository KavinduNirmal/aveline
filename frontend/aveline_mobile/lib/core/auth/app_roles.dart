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

  /// The words to show for [role] in the interface.
  ///
  /// The claims carry ids - `org:boutique_owner` - and those are not what an
  /// associate should have to read on their own account. A grant this build does
  /// not know is printed exactly as it arrived rather than hidden, so a role
  /// added on the server is visible on screen before it is named here.
  static String labelFor(String role) => _labels[role] ?? role;

  static const Map<String, String> _labels = {
    staff: 'Staff',
    customerRelations: 'Customer relations',
    moderator: 'Moderator',
    admin: 'Admin',
    owner: 'Owner',
    boutiqueStaff: 'Boutique staff',
    boutiqueManager: 'Boutique manager',
    boutiqueSupervisor: 'Boutique supervisor',
    boutiqueOwner: 'Boutique owner',
  };
}
