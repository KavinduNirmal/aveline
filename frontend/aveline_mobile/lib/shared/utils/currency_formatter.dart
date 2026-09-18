/// Money, as the boutique reads it.
///
/// LKR with a hand-rolled thousands separator: one label is not worth a
/// formatting dependency, and `intl` would pull in a locale database the app has
/// no other use for.
library;

/// `Rs 24,500`. The amount is rounded to whole rupees, which is the smallest
/// unit the boutique prices in.
String rupees(num amount) => 'Rs ${groupedNumber(amount.round())}';

/// Groups a whole number in threes: `150500` reads `150,500`.
String groupedNumber(int value) {
  final digits = value.abs().toString();
  final buffer = StringBuffer(value < 0 ? '-' : '');
  for (var i = 0; i < digits.length; i++) {
    if (i > 0 && (digits.length - i) % 3 == 0) {
      buffer.write(',');
    }
    buffer.write(digits[i]);
  }
  return buffer.toString();
}
