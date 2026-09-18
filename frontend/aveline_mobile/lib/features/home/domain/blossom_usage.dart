/// How many Blossoms the boutique has spent this billing cycle.
///
/// Blossoms are the assistant's credits: the unit the plan meters and the owner
/// tops up. Mirrors the shape the usage endpoint will return.
class BlossomUsage {
  const BlossomUsage({
    required this.used,
    required this.allowance,
    required this.renewsOn,
  });

  final int used;
  final int allowance;

  /// When the allowance resets, as the associate reads a date: `1 October`.
  final String renewsOn;

  int get remaining => (allowance - used).clamp(0, allowance);

  /// How much of the allowance is spent, for the meter.
  double get usedFraction =>
      allowance <= 0 ? 0 : (used / allowance).clamp(0.0, 1.0);

  /// Whether the cycle is far enough along that a top-up is worth asking for.
  bool get isRunningLow => usedFraction >= 0.8;
}
