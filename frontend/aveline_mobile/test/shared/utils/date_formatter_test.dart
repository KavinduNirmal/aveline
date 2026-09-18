import 'package:aveline_mobile/shared/utils/date_formatter.dart';
import 'package:flutter_test/flutter_test.dart';

void main() {
  group('relativeMoment', () {
    final now = DateTime.utc(2026, 9, 17, 12);

    test('something that just happened reads as just now', () {
      expect(relativeMoment(now, now: now), 'Just now');
      expect(
        relativeMoment(now.subtract(const Duration(seconds: 45)), now: now),
        'Just now',
      );
    });

    test('minutes, while the hour has not turned', () {
      expect(
        relativeMoment(now.subtract(const Duration(minutes: 1)), now: now),
        '1m ago',
      );
      expect(
        relativeMoment(now.subtract(const Duration(minutes: 59)), now: now),
        '59m ago',
      );
    });

    test('hours, while the day has not turned', () {
      expect(
        relativeMoment(now.subtract(const Duration(hours: 1)), now: now),
        '1h ago',
      );
      expect(
        relativeMoment(now.subtract(const Duration(hours: 23, minutes: 59)), now: now),
        '23h ago',
      );
    });

    test('days, up to a week', () {
      expect(
        relativeMoment(now.subtract(const Duration(days: 1)), now: now),
        '1d ago',
      );
      expect(
        relativeMoment(now.subtract(const Duration(days: 6, hours: 23)), now: now),
        '6d ago',
      );
    });

    test('past a week it names the date instead of counting', () {
      // "9d ago" is a number nobody converts; the date is what a reader wants.
      // Asserted on the month rather than the day so the label can be checked in
      // any time zone the suite happens to run in.
      expect(
        relativeMoment(now.subtract(const Duration(days: 9)), now: now),
        contains('Sep 2026'),
      );
    });

    test('a timestamp ahead of the clock reads as just now, not as negative', () {
      // A device running behind the server must not print "-3m ago".
      expect(
        relativeMoment(now.add(const Duration(minutes: 5)), now: now),
        'Just now',
      );
    });

    test('measures elapsed time rather than calendar days', () {
      // 02:00 the same calendar day is ten hours old, not "Today".
      expect(relativeMoment(DateTime.utc(2026, 9, 17, 2), now: now), '10h ago');
    });
  });
}
