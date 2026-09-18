import '../domain/customer_book.dart';

/// Where the alphabet index can jump to, in the scroll view's own coordinates.
///
/// A contact list is a uniform grid: every letter header and every row is a
/// fixed height, which is what makes this arithmetic exact. That matters because
/// the sections are laid out lazily — a letter far down the book has no rendered
/// position to ask for, so its offset has to be computed rather than measured.
abstract final class CustomerSectionOffsets {
  /// The height of a letter's header, matching `CustomerSectionHeader`.
  static const double headerHeight = 32;

  /// The height of one client's row, matching `CustomerTile`.
  static const double rowHeight = 72;

  /// The offset each section's header starts at, in order.
  ///
  /// [listStart] is where the sections begin: everything above them — the title,
  /// the search field and the level row — measured once from the rendered
  /// header block, because that block's height depends on the typeface.
  static List<double> all(
    List<CustomerSection> sections, {
    required double listStart,
  }) {
    final offsets = <double>[];
    var offset = listStart;
    for (final section in sections) {
      offsets.add(offset);
      offset += headerHeight + section.customers.length * rowHeight;
    }
    return offsets;
  }

  /// The section sitting at the top of the viewport when it is scrolled to
  /// [offset].
  ///
  /// Used to keep the index's highlight on the letter the associate is actually
  /// looking at, including after a jump that ran out of scroll before it reached
  /// the letter that was asked for.
  static int sectionAt(
    List<CustomerSection> sections,
    double offset, {
    required double listStart,
  }) {
    if (sections.isEmpty) {
      return 0;
    }

    final offsets = all(sections, listStart: listStart);
    var index = 0;
    for (var i = 0; i < offsets.length; i++) {
      // A pixel of slack: a jump lands on the header's exact offset, and float
      // arithmetic on the way there must not leave the highlight one letter
      // behind.
      if (offsets[i] <= offset + 1) {
        index = i;
      } else {
        break;
      }
    }
    return index;
  }
}
