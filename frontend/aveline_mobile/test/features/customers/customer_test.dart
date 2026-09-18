import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:flutter_test/flutter_test.dart';

Customer _customer({
  String id = 'CUS-1001',
  String? name,
  String? nickname,
  String phone = '+94 77 111 2222',
  String? email,
  Set<String> tags = const <String>{},
}) {
  return Customer(
    id: id,
    organizationId: 'org-1',
    phoneNumber: phone,
    level: CustomerLevel.level1,
    status: CustomerStatus.fresh,
    fullName: name,
    nickname: nickname,
    email: email,
    tags: tags,
  );
}

void main() {
  group('Customer names', () {
    test('prints the full name the boutique has', () {
      final customer = _customer(name: 'Eleanor Vane', nickname: 'Ellie');

      expect(customer.displayName, 'Eleanor Vane');
      expect(customer.isKnownByNickname, isFalse);
      expect(customer.initials, 'EV');
    });

    test('falls back to the nickname when there is no full name', () {
      final customer = _customer(nickname: 'Shan');

      expect(customer.displayName, 'Shan');
      expect(customer.isKnownByNickname, isTrue);
      expect(customer.initials, 'S');
    });

    test('falls back to the number for a client with no name at all', () {
      final customer = _customer(phone: '+94 77 214 8890');

      expect(customer.displayName, '+94 77 214 8890');
      expect(customer.isKnownByNickname, isFalse);
      // A row's circle reads `#` rather than the first character of a number.
      expect(customer.initials, '#');
      expect(customer.sectionLetter, '#');
    });

    test('takes initials from the first and last name', () {
      expect(_customer(name: 'Anjali Kumari Perera').initials, 'AP');
      expect(_customer(name: 'Anjali').initials, 'A');
    });
  });

  group('Customer sections', () {
    test('files a client under the first letter of their name', () {
      expect(_customer(name: 'anjali perera').sectionLetter, 'A');
      expect(_customer(nickname: 'shan').sectionLetter, 'S');
    });

    test('files a client whose name is not a letter under #', () {
      expect(_customer(name: 'Ægir Olsen').sectionLetter, '#');
    });

    test('ignores surrounding space when filing', () {
      expect(_customer(name: '  Zahra Nazeer  ').sectionLetter, 'Z');
    });
  });

  group('Customer id label', () {
    test('leaves an id a row can read alone', () {
      expect(_customer(id: 'CUS-1001').idLabel, 'CUS-1001');
    });

    test('shortens the GUID the API keys customers by', () {
      const guid = '3f9a2b1c-8d4e-4f6a-9b0c-1d2e3f4a5b6c';

      final label = _customer(id: guid).idLabel;

      expect(label, '3f9a2b1c…');
      expect(label.length, lessThan(guid.length));
    });
  });

  group('Customer search', () {
    test('matches on name, nickname, number, email and id', () {
      final customer = _customer(
        id: 'CUS-1042',
        name: 'Nadia Rahman',
        nickname: 'Nads',
        phone: '+94 77 445 9917',
        email: 'nadia@example.com',
        tags: const {'bridal'},
      );

      for (final needle in [
        'nadia',
        'nads',
        '445 9917',
        'nadia@example.com',
        'cus-1042',
        'bridal',
      ]) {
        expect(
          customer.searchHaystack.contains(needle),
          isTrue,
          reason: 'expected the haystack to match "$needle"',
        );
      }
    });
  });

  group('CustomerStatus', () {
    test('reads the API wire values, including its reserved `new`', () {
      expect(CustomerStatus.parse('new'), CustomerStatus.fresh);
      expect(CustomerStatus.parse('returning'), CustomerStatus.returning);
      expect(CustomerStatus.parse('vip'), CustomerStatus.vip);
      expect(CustomerStatus.parse('dormant'), CustomerStatus.dormant);
    });

    test('falls back to the entry status for anything unknown', () {
      expect(CustomerStatus.parse(null), CustomerStatus.fresh);
      expect(CustomerStatus.parse('nonsense'), CustomerStatus.fresh);
    });
  });
}
