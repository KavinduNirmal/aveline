/// Sri Lankan phone numbers, as the boutique reads them.
///
/// A mirror of the web's `lib/boutique.ts`, so a number the associate typed in
/// the browser and the same number shown in the app are spaced identically.
library;

/// Sri Lanka calling code prefix shown on a phone field.
const String lkPhonePrefix = '+94';

/// Number of national (local) significant digits — excludes the +94 country code.
const int lkLocalDigits = 9;

/// Extracts up to 9 local digits, dropping a leading `0` or the `94` country code.
String _toLocalDigits(String raw) {
  var digits = raw.replaceAll(RegExp(r'\D'), '');
  if (digits.startsWith('94')) {
    digits = digits.substring(2);
  }
  if (digits.startsWith('0')) {
    digits = digits.substring(1);
  }
  return digits.length > lkLocalDigits
      ? digits.substring(0, lkLocalDigits)
      : digits;
}

/// `digits.slice(start, end)` as JavaScript does it: a range past the end is
/// shorter rather than an error, which is what lets a partially typed number
/// format as far as it goes.
String _slice(String digits, int start, int end) {
  if (start >= digits.length) {
    return '';
  }
  return digits.substring(start, end < digits.length ? end : digits.length);
}

/// Formats a raw phone value into `+94 77 12 12 123`.
///
/// Any non-digit characters are ignored (the input accepts numbers only); the
/// country code prefix is always rendered, so a half-typed number still reads as
/// a Sri Lankan one rather than as a bare digit run.
String formatLkPhone(String raw) {
  final digits = _toLocalDigits(raw);
  if (digits.isEmpty) {
    return '$lkPhonePrefix ';
  }
  final groups = <String>[
    _slice(digits, 0, 2),
    _slice(digits, 2, 4),
    _slice(digits, 4, 6),
    _slice(digits, 6, lkLocalDigits),
  ].where((group) => group.isNotEmpty).toList();
  return '$lkPhonePrefix ${groups.join(' ')}';
}
