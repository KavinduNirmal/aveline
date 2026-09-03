import 'package:clerk_auth/clerk_auth.dart' as clerk;

import '../domain/auth_claims.dart';
import '../domain/auth_user.dart';

/// Maps Clerk SDK models onto the app's auth domain models.
abstract final class AuthUserMapper {
  /// Maps a Clerk [clerk.User] plus decoded JWT [claims] onto an [AuthUser].
  static AuthUser fromClerk(clerk.User user, {AuthClaims? claims}) => AuthUser(
        id: user.id,
        firstName: user.firstName,
        lastName: user.lastName,
        email: user.email,
        imageUrl: user.imageUrl,
        userRole: claims?.userRole,
        orgRole: claims?.orgRole,
        orgId: claims?.orgId,
        orgSlug: claims?.orgSlug,
      );
}
