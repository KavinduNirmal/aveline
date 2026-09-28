/// The part of the boutique a focus task belongs to.
///
/// The domain selects the task card's chip colour, so that accent carries
/// meaning (logistics, patrons, wardrobe, commerce) rather than decorating.
enum FocusDomain {
  logistics,
  patron,
  wardrobe,
  commerce;

  /// The wire value the feed uses, or `null` when it is not a known domain.
  ///
  /// The enum is closed, so an unknown value must be dropped rather than throw:
  /// a server that adds a domain before the app knows it should not blank the
  /// whole deck.
  static FocusDomain? fromWire(String? value) => switch (value) {
        'logistics' => FocusDomain.logistics,
        'patron' => FocusDomain.patron,
        'wardrobe' => FocusDomain.wardrobe,
        'commerce' => FocusDomain.commerce,
        _ => null,
      };

  /// The wire spelling.
  String get wireValue => name;
}

/// One thing waiting on the signed-in user today.
///
/// Mirrors the shape the focus feed returns (`GET /orgs/{id}/stats/home`).
class FocusTask {
  const FocusTask({
    required this.id,
    required this.domain,
    required this.title,
    required this.detail,
    required this.timeLabel,
    required this.actionLabel,
    required this.doneMessage,
    this.sourceKey,
    this.dueAtUtc,
  });

  /// Stable identity, used for list keys and for the completion callback.
  final String id;

  /// The identifier of the **fact** behind this docket (an inventory item id, a
  /// customer-event id, an agent run id).
  ///
  /// `id` addresses the docket; `sourceKey` addresses the fact, and it is what
  /// makes a dismissal survive the feed being recomputed.
  final String? sourceKey;

  final FocusDomain domain;

  /// The ask, written as the decision itself: `Approve delivery courier for
  /// Mrs. Silva`.
  final String title;

  /// The context needed to make that decision: patron, document, quantity.
  final String detail;

  /// When it matters, as the associate reads a clock: `3:00 PM`.
  ///
  /// Optional: the feed sends [dueAtUtc] and the client formats the clock from
  /// it, so a display string is only a fallback for a docket with no timestamp.
  final String? timeLabel;

  /// When the docket is due, in UTC. The source of truth for "next".
  final DateTime? dueAtUtc;

  /// The verb on the primary button: `Sign Off`, `Approve`, `Mark ready`.
  final String actionLabel;

  /// Toast copy shown once the task is cleared, in the app's voice.
  final String doneMessage;

  /// What to print in the card's clock slot, or `null` when there is no time.
  ///
  /// Prefers an explicit label and otherwise formats [dueAtUtc] in local time.
  String? get displayTimeLabel {
    final label = timeLabel?.trim();
    if (label != null && label.isNotEmpty) {
      return label;
    }
    final due = dueAtUtc?.toLocal();
    if (due == null) {
      return null;
    }

    final hour24 = due.hour;
    final hour = hour24 % 12 == 0 ? 12 : hour24 % 12;
    final minute = due.minute.toString().padLeft(2, '0');
    final meridiem = hour24 < 12 ? 'AM' : 'PM';
    return '$hour:$minute $meridiem';
  }

  /// Minutes past midnight for the docket's time, or `null` when it has none.
  ///
  /// Prefers [dueAtUtc] (local) and otherwise parses [timeLabel], which is the
  /// compatibility path for a docket the feed sent without a timestamp.
  int? get minutesOfDay {
    final due = dueAtUtc?.toLocal();
    if (due != null) {
      return due.hour * 60 + due.minute;
    }

    final label = timeLabel;
    if (label == null) {
      return null;
    }

    final match =
        RegExp(r'^(\d{1,2}):(\d{2})\s*([AaPp][Mm])$').firstMatch(label.trim());
    if (match == null) {
      return null;
    }

    final hour = int.parse(match.group(1)!);
    final minute = int.parse(match.group(2)!);
    final isPm = match.group(3)!.toUpperCase() == 'PM';

    return (hour % 12 + (isPm ? 12 : 0)) * 60 + minute;
  }
}
