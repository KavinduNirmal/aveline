import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:flutter_test/flutter_test.dart';

Customer _customer(String id, {String? name}) => Customer(
  id: id,
  organizationId: 'org-1',
  phoneNumber: '+94 77 000 0000',
  level: CustomerLevel.level1,
  status: CustomerStatus.fresh,
  fullName: name,
);

void main() {
  group('CustomerBook', () {
    test('knows how many clients it holds', () {
      final book = CustomerBook([
        CustomerSection(
          letter: 'A',
          customers: [_customer('1', name: 'Anura'), _customer('2', name: 'Asha')],
        ),
        CustomerSection(letter: 'B', customers: [_customer('3', name: 'Bimal')]),
      ]);

      expect(book.total, 3);
      expect(book.isEmpty, isFalse);
    });

    test('offers a letter per section, in the order they are filed', () {
      final book = CustomerBook([
        CustomerSection(letter: 'A', customers: [_customer('1', name: 'Anura')]),
        CustomerSection(letter: 'M', customers: [_customer('2', name: 'Maya')]),
        CustomerSection(letter: '#', customers: [_customer('3')]),
      ]);

      // `#` is where the index has to offer it, which is the book's business
      // rather than the index's.
      expect(book.letters, ['A', 'M', '#']);
    });

    test('offers no letter for a section that holds nobody', () {
      final book = CustomerBook([
        CustomerSection(letter: 'A', customers: [_customer('1', name: 'Anura')]),
        const CustomerSection(letter: 'B', customers: <Customer>[]),
      ]);

      expect(book.letters, ['A']);
      expect(book.total, 1);
    });

    test('starts empty, which is what an unmatched narrowing looks like', () {
      expect(CustomerBook.empty.isEmpty, isTrue);
      expect(CustomerBook.empty.total, 0);
      expect(CustomerBook.empty.letters, isEmpty);
    });
  });
}
