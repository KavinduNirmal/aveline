/// The part of the boutique a focus task belongs to.
///
/// The domain selects the task card's chip colour, so that accent carries
/// meaning (logistics, patrons, wardrobe, commerce) rather than decorating.
enum FocusDomain { logistics, patron, wardrobe, commerce }

/// One thing waiting on the signed-in user today.
///
/// Mirrors the shape Home expects the API to return for the focus deck;
/// `demoFocusTasks` stands in until the commerce and intelligence slices land.
class FocusTask {
  const FocusTask({
    required this.id,
    required this.domain,
    required this.title,
    required this.detail,
    required this.timeLabel,
    required this.actionLabel,
    required this.doneMessage,
  });

  /// Stable identity, used for list keys and for the completion callback.
  final String id;

  final FocusDomain domain;

  /// The ask, written as the decision itself: `Approve delivery courier for
  /// Mrs. Silva`.
  final String title;

  /// The context needed to make that decision: patron, document, quantity.
  final String detail;

  /// When it matters, as the associate reads a clock: `3:00 PM`.
  final String timeLabel;

  /// The verb on the primary button: `Sign Off`, `Approve`, `Mark ready`.
  final String actionLabel;

  /// Toast copy shown once the task is cleared, in the app's voice.
  final String doneMessage;
}
