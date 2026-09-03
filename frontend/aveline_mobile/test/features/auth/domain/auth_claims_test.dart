import 'package:aveline_mobile/features/auth/domain/auth_claims.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AuthClaims.fromBody', () {
    test('extracts user_role, org_role, org_id and org_slug', () {
      final claims = AuthClaims.fromBody({
        'user_role': 'associate',
        'org_role': 'admin',
        'org_id': 'org_123',
        'org_slug': 'aveline-colombo',
      });

      expect(claims.userRole, 'associate');
      expect(claims.orgRole, 'admin');
      expect(claims.orgId, 'org_123');
      expect(claims.orgSlug, 'aveline-colombo');
    });

    test('leaves missing claims null', () {
      final claims = AuthClaims.fromBody(const {});

      expect(claims.userRole, isNull);
      expect(claims.orgRole, isNull);
      expect(claims.orgId, isNull);
      expect(claims.orgSlug, isNull);
    });
  });
}
