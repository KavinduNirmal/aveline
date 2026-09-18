import 'package:aveline_mobile/shared/utils/greeting.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('Greeting.salutationFor', () {
    test('wishes good morning from midnight until noon', () {
      expect(Greeting.salutationFor(DateTime(2026, 1, 1)), 'Good morning');
      expect(
        Greeting.salutationFor(DateTime(2026, 1, 1, 9, 30)),
        'Good morning',
      );
      expect(
        Greeting.salutationFor(DateTime(2026, 1, 1, 11, 59)),
        'Good morning',
      );
    });

    test('wishes good afternoon from noon until 17:00', () {
      expect(
        Greeting.salutationFor(DateTime(2026, 1, 1, 12)),
        'Good afternoon',
      );
      expect(
        Greeting.salutationFor(DateTime(2026, 1, 1, 16, 59)),
        'Good afternoon',
      );
    });

    test('wishes good evening from 17:00 onward', () {
      expect(Greeting.salutationFor(DateTime(2026, 1, 1, 17)), 'Good evening');
      expect(
        Greeting.salutationFor(DateTime(2026, 1, 1, 23, 59)),
        'Good evening',
      );
    });
  });

  group('Greeting.forName', () {
    test('addresses the user by first name', () {
      expect(
        Greeting.forName('Good morning', 'Nadia'),
        'Good morning, Nadia.',
      );
    });

    test('trims surrounding whitespace from the name', () {
      expect(
        Greeting.forName('Good evening', '  Nadia '),
        'Good evening, Nadia.',
      );
    });

    test('drops the addressee when there is no usable name', () {
      expect(Greeting.forName('Good afternoon', null), 'Good afternoon.');
      expect(Greeting.forName('Good afternoon', ''), 'Good afternoon.');
      expect(Greeting.forName('Good afternoon', '   '), 'Good afternoon.');
    });
  });
}
