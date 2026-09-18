/// How many Blossoms the boutique has spent this billing cycle.
///
/// Blossoms are the assistant's credits: the unit the plan meters and the owner
/// tops up. Mirrors the projection `GET /orgs/{id}/blossoms/balance` returns.
///
/// The quantities are `decimal(18,4)` on the wire, not integers: the conversion
/// rule charges a minimum of 0.1 Blossoms and rounds to one decimal, so
/// fractions are normal and must not be truncated on the way in.
class BlossomUsage {
  const BlossomUsage({
    required this.used,
    required this.allowance,
    required this.renewsOn,
    this.reportedRemaining,
    this.lowBalanceThresholdPercent = 20,
  });

  final double used;

  /// `monthlyBlossomLimit + blossomGranted - blossomAdjusted`.
  final double allowance;

  /// When the allowance resets, as the associate reads a date: `1 October`.
  final String renewsOn;

  /// The server's reconciled remaining figure, when the response carried one.
  ///
  /// Preferred over deriving `allowance - used`, because it is the projection
  /// the backend reconciles.
  final double? reportedRemaining;

  /// How far the remaining allowance must fall, as a percentage of the limit,
  /// before the cycle is worth topping up. The server's
  /// `lowBalanceThresholdPercent` (default 20).
  final double lowBalanceThresholdPercent;

  double get remaining =>
      reportedRemaining ?? (allowance - used).clamp(0, allowance);

  /// How much of the allowance is spent, for the meter.
  double get usedFraction =>
      allowance <= 0 ? 0 : (used / allowance).clamp(0.0, 1.0);

  /// Whether the remaining allowance is at or below the server's low-water line.
  bool get isRunningLow =>
      allowance > 0 && remaining <= allowance * lowBalanceThresholdPercent / 100;
}
