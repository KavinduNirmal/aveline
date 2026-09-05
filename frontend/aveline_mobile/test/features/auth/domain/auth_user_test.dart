import 'package:aveline_mobile/features/auth/domain/auth_claims.dart';
import 'package:aveline_mobile/features/auth/domain/auth_user.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AuthUser.displayName', () {
    test('prefers first and last name', () {
      const user = AuthUser(id: 'user_1', firstName: 'Ada', lastName: 'Lovelace');
      expect(user.displayName, 'Ada Lovelace');
    });

    test('falls back to first name only', () {
      const user = AuthUser(id: 'user_1', firstName: 'Ada');
      expect(user.displayName, 'Ada');
    });

    test('falls back to email', () {
      const user = AuthUser(id: 'user_1', email: 'ada@aveline.dev');
      expect(user.displayName, 'ada@aveline.dev');
    });

    test('falls back to id', () {
      const user = AuthUser(id: 'user_1');
      expect(user.displayName, 'user_1');
    });
  });

  group('AuthUser.withClaims', () {
    test('replaces role claims while keeping identity fields', () {
      const user = AuthUser(id: 'user_1', firstName: 'Ada', email: 'a@b.dev');
      const claims = AuthClaims(
        userRole: 'staff',
        orgRole: 'org:boutique_manager',
        orgId: 'org_9',
        orgSlug: 'aveline-colombo',
      );

      final updated = user.withClaims(claims);

      expect(updated.id, 'user_1');
      expect(updated.firstName, 'Ada');
      expect(updated.email, 'a@b.dev');
      expect(updated.userRole, 'staff');
      expect(updated.orgRole, 'org:boutique_manager');
      expect(updated.orgId, 'org_9');
      expect(updated.orgSlug, 'aveline-colombo');
    });
  });
}
