import 'package:aveline_mobile/features/customers/data/customer_repository.dart';
import 'package:aveline_mobile/features/customers/data/demo_customer_repository.dart';
import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:flutter_test/flutter_test.dart';

DemoCustomerRepository _repository() =>
    DemoCustomerRepository(bookDelay: Duration.zero);

/// Every client in the book, in the order the book files them.
List<Customer> _everyone(CustomerBook book) => [
  for (final section in book.sections) ...section.customers,
];

void main() {
  group('DemoCustomerRepository', () {
    test('holds a book big enough to need an alphabet index', () async {
      final book = await _repository().fetchBook();

      expect(book.total, greaterThan(30));
      expect(book.letters.length, greaterThan(15));
    });

    test('files A to Z with the unnamed last', () async {
      final book = await _repository().fetchBook();

      final letters = book.letters;
      final named = letters.where((letter) => letter != '#').toList();
      expect(named, orderedEquals([...named]..sort()));
      if (letters.contains('#')) {
        expect(letters.last, '#');
      }
      // The demo book deliberately carries clients the boutique has no name for,
      // which is the only way the `#` section gets exercised.
      expect(letters, contains('#'));
    });

    test('sorts each section by the name the row prints', () async {
      final book = await _repository().fetchBook();

      for (final section in book.sections) {
        final names = [
          for (final customer in section.customers)
            customer.displayName.toLowerCase(),
        ];
        expect(names, orderedEquals([...names]..sort()));
      }
    });

    test('files every client under their own letter', () async {
      final book = await _repository().fetchBook();

      for (final section in book.sections) {
        for (final customer in section.customers) {
          expect(customer.sectionLetter, section.letter);
        }
      }
    });

    test('narrows to what the search field holds', () async {
      final book = await _repository().fetchBook(
        query: const CustomerQuery(search: 'nadia'),
      );

      expect(book.total, 1);
      expect(_everyone(book).single.displayName, 'Nadia Rahman');
    });

    test('matches a nickname, not only a full name', () async {
      final book = await _repository().fetchBook(
        query: const CustomerQuery(search: 'chooti'),
      );

      expect(book.total, 1);
      expect(_everyone(book).single.displayName, 'Chooti');
      expect(_everyone(book).single.isKnownByNickname, isTrue);
    });

    test('matches a number, so a client can be found without their name', () async {
      final book = await _repository().fetchBook(
        query: const CustomerQuery(search: '214 8890'),
      );

      expect(book.total, 1);
      expect(_everyone(book).single.fullName, isNull);
    });

    test('narrows to one level at a time', () async {
      final all = await _repository().fetchBook();
      final vip = await _repository().fetchBook(
        query: const CustomerQuery(level: CustomerLevel.vip),
      );

      expect(vip.total, greaterThan(0));
      expect(vip.total, lessThan(all.total));
      for (final customer in _everyone(vip)) {
        expect(customer.level, CustomerLevel.vip);
      }
    });

    test('combines the search and the level', () async {
      final book = await _repository().fetchBook(
        query: const CustomerQuery(
          search: 'silva',
          level: CustomerLevel.vip,
        ),
      );

      expect(book.total, 1);
      expect(_everyone(book).single.displayName, 'Chamari Silva');
    });

    test('says so with an empty book when nothing matches', () async {
      final book = await _repository().fetchBook(
        query: const CustomerQuery(search: 'nobody at all'),
      );

      expect(book.isEmpty, isTrue);
      expect(book.total, 0);
      expect(book.letters, isEmpty);
    });

    test('derives the status the way the loyalty service does', () async {
      final everyone = _everyone(await _repository().fetchBook());

      Customer named(String name) =>
          everyone.firstWhere((customer) => customer.displayName == name);

      // A client who has not been in for months is dormant whatever they spent.
      expect(named('Zahra Nazeer').status, CustomerStatus.dormant);
      // Spend alone is not a VIP: the service also asks for the visits.
      expect(named('Chamari Silva').status, CustomerStatus.vip);
      expect(named('Sophia Liyanage').status, CustomerStatus.fresh);
    });

    test('keeps a level that is not simply the status', () async {
      final everyone = _everyone(await _repository().fetchBook());

      // The level is the boutique's own grade, so a client the API would call a
      // VIP can still be graded below VIP. The two fields exist for that.
      expect(
        everyone.any(
          (customer) =>
              customer.status == CustomerStatus.vip &&
              customer.level != CustomerLevel.vip,
        ),
        isTrue,
      );
    });
  });
}
