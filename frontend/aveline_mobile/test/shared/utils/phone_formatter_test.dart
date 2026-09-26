import 'package:aveline_mobile/shared/utils/phone_formatter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('formatLkPhone', () {
    test('adds the country code and groups a national number', () {
      expect(formatLkPhone('771212123'), '+94 77 12 12 123');
    });

    test('understands a number that already carries the country code', () {
      expect(formatLkPhone('94771212123'), '+94 77 12 12 123');
    });

    test('drops a leading zero before grouping', () {
      expect(formatLkPhone('0771212123'), '+94 77 12 12 123');
    });

    test('ignores every non-digit, as the field it came from only accepts digits', () {
      expect(formatLkPhone('+94 77 1a2b1c2d1e2f3g9x'), '+94 77 12 12 123');
    });

    test('renders the prefix alone for a value with no digits yet', () {
      expect(formatLkPhone(''), '+94 ');
    });

    test('formats as far as a partial number goes rather than throwing', () {
      expect(formatLkPhone('77'), '+94 77');
      expect(formatLkPhone('7712'), '+94 77 12');
    });

    test('keeps only the nine national digits', () {
      expect(formatLkPhone('947712121239999'), '+94 77 12 12 123');
    });
  });
}
