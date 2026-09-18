/// Time-of-day greeting helpers for the personalized Home heading.
///
/// Pure Dart (no Flutter imports) so the salutation can be verified for any
/// hour of the day without touching the device clock.
abstract final class Greeting {
  /// The salutation that matches [time] in the device's local zone.
  ///
  /// Morning runs until noon, afternoon until 17:00, and evening from 17:00
  /// onward — so late-night sessions still read as an evening welcome rather
  /// than a farewell.
  static String salutationFor(DateTime time) {
    final hour = time.hour;
    if (hour < 12) return 'Good morning';
    if (hour < 17) return 'Good afternoon';
    return 'Good evening';
  }

  /// [salutation] addressed to [firstName], e.g. `Good morning, Nadia.`
  ///
  /// Falls back to a bare `Good morning.` when [firstName] is null, empty, or
  /// only whitespace, so the heading never reads `Good morning, .`
  static String forName(String salutation, String? firstName) {
    final name = firstName?.trim();
    if (name == null || name.isEmpty) return '$salutation.';
    return '$salutation, $name.';
  }
}
