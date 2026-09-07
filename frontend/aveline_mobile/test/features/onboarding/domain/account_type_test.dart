import 'package:aveline_mobile/features/onboarding/domain/account_type.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('AccountType', () {
    test('parses known wire values', () {
      expect(AccountType.parse('owner'), AccountType.owner);
      expect(AccountType.parse('staff'), AccountType.staff);
    });

    test('returns null for unknown or null values', () {
      expect(AccountType.parse('admin'), isNull);
      expect(AccountType.parse(null), isNull);
      expect(AccountType.parse(''), isNull);
    });

    test('round-trips wire values', () {
      for (final type in AccountType.values) {
        expect(AccountType.parse(type.wireValue), type);
      }
    });
  });
}
