/// Aveline claims minted by the `jwt-aveline-v1` template, which the backend
/// trusts for authorization.
class AuthClaims {
  const AuthClaims({
    this.userRole,
    this.orgRole,
    this.orgId,
    this.orgSlug,
  });

  /// Team-level role (e.g. `staff`, `customer_relations`, or `admin`).
  final String? userRole;

  /// Per-boutique role (e.g. `org:boutique_manager`).
  final String? orgRole;

  /// Current organization id.
  final String? orgId;

  /// Current organization slug.
  final String? orgSlug;

  /// Parses Aveline claims from a decoded JWT body.
  factory AuthClaims.fromBody(Map<String, dynamic> body) => AuthClaims(
        userRole: body['user_role'] as String?,
        orgRole: body['org_role'] as String?,
        orgId: body['org_id'] as String?,
        orgSlug: body['org_slug'] as String?,
      );
}
