import 'auth_claims.dart';

/// The signed-in user, independent of the identity provider behind it.
class AuthUser {
  const AuthUser({
    required this.id,
    this.firstName,
    this.lastName,
    this.email,
    this.imageUrl,
    this.userRole,
    this.orgRole,
    this.orgId,
    this.orgSlug,
  });

  final String id;
  final String? firstName;
  final String? lastName;
  final String? email;
  final String? imageUrl;

  /// Aveline claims from the JWT, used for role-aware UI.
  final String? userRole;
  final String? orgRole;
  final String? orgId;
  final String? orgSlug;

  /// Best available display name.
  String get displayName {
    if (firstName != null && lastName != null) {
      return '$firstName $lastName';
    }
    if (firstName != null) {
      return firstName!;
    }
    if (email != null) {
      return email!;
    }
    return id;
  }

  /// Copy with a different set of [AuthClaims] (used when a fresh JWT arrives).
  AuthUser withClaims(AuthClaims claims) => AuthUser(
        id: id,
        firstName: firstName,
        lastName: lastName,
        email: email,
        imageUrl: imageUrl,
        userRole: claims.userRole,
        orgRole: claims.orgRole,
        orgId: claims.orgId,
        orgSlug: claims.orgSlug,
      );
}
