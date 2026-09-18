import 'package:aveline_mobile/features/customers/domain/customer.dart';
import 'package:aveline_mobile/features/customers/domain/customer_book.dart';
import 'package:aveline_mobile/features/customers/domain/customer_level.dart';
import 'package:aveline_mobile/features/customers/presentation/customer_section_offsets.dart';
import 'package:flutter_test/flutter_test.dart';

Customer _customer(String id) => Customer(
  id: id,
  organizationId: 'org-1',
  phoneNumber: '+94 77 000 0000',
  level: CustomerLevel.level1,
  status: CustomerStatus.fresh,
  fullName: 'Client $id',
);

/// `A` holds two clients, `B` one, `C` three.
List<CustomerSection> _sections() => [
  CustomerSection(
    letter: 'A',
    customers: [_customer('a1'), _customer('a2')],
  ),
  CustomerSection(letter: 'B', customers: [_customer('b1')]),
  CustomerSection(
    letter: 'C',
    customers: [_customer('c1'), _customer('c2'), _customer('c3')],
  ),
];

void main() {
  group('CustomerSectionOffsets', () {
    test('starts counting from the block above the book', () {
      final offsets = CustomerSectionOffsets.all(_sections(), listStart: 212);

      // The first letter begins where the header block ends, and each letter
      // after it is its predecessor's height plus its own header.
      expect(offsets.first, 212);
      expect(
        offsets[1],
        212 +
            CustomerSectionOffsets.headerHeight +
            2 * CustomerSectionOffsets.rowHeight,
      );
      expect(
        offsets[2],
        offsets[1] +
            CustomerSectionOffsets.headerHeight +
            1 * CustomerSectionOffsets.rowHeight,
      );
    });

    test('steps by the header plus every row it holds', () {
      final offsets = CustomerSectionOffsets.all(_sections(), listStart: 0);

      expect(offsets, [0, 32 + 144, 32 + 144 + 32 + 72]);
    });

    test('has nothing to say about an empty book', () {
      expect(CustomerSectionOffsets.all(const [], listStart: 0), isEmpty);
      expect(CustomerSectionOffsets.sectionAt(const [], 0, listStart: 0), 0);
    });
  });

  group('CustomerSectionOffsets.sectionAt', () {
    test('names the letter at the top of the viewport', () {
      final sections = _sections();
      final offsets = CustomerSectionOffsets.all(sections, listStart: 100);

      for (var i = 0; i < sections.length; i++) {
        expect(
          CustomerSectionOffsets.sectionAt(
            sections,
            offsets[i],
            listStart: 100,
          ),
          i,
          reason: 'expected ${sections[i].letter} at its own offset',
        );
      }
    });

    test('keeps the last letter while scrolling through it', () {
      final sections = _sections();
      final offsets = CustomerSectionOffsets.all(sections, listStart: 0);

      // Halfway through C's rows, C is still the letter at the top.
      expect(
        CustomerSectionOffsets.sectionAt(
          sections,
          offsets[2] + 2 * CustomerSectionOffsets.rowHeight,
          listStart: 0,
        ),
        2,
      );
    });

    test('holds the first letter above the book', () {
      // The rubber band a short list allows at the top must not walk the
      // highlight backwards off the alphabet.
      expect(
        CustomerSectionOffsets.sectionAt(_sections(), -40, listStart: 100),
        0,
      );
    });
  });
}
