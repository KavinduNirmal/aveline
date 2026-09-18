import 'dart:math' as math;

import 'package:flutter/material.dart';

/// The alphabet strip down the trailing edge of the book, the way a phone's
/// contact list carries one: tap a letter to go to it, or drag along the strip to
/// scan the book.
///
/// The strip lists only the letters the book actually holds, so it is also a
/// summary of the shape of the shop's clientele.
class CustomerAlphabetIndex extends StatelessWidget {
  const CustomerAlphabetIndex({
    super.key,
    required this.letters,
    required this.activeLetter,
    required this.onLetterSelected,
  });

  /// The letters to offer, in the order the book files them.
  final List<String> letters;

  /// The letter at the top of the list, highlighted so the strip says where the
  /// associate is.
  final String? activeLetter;

  final ValueChanged<String> onLetterSelected;

  /// The width the strip reserves on the trailing edge, which the rows leave
  /// clear so a badge is never under a letter.
  static const double width = 26;

  /// One letter's cell, before the strip has to shrink to fit the viewport.
  static const double cellHeight = 15;

  /// The letter at [dy] within a strip whose cells are [cellHeight] tall.
  ///
  /// Arithmetic rather than hit-testing: the cells are uniform, so a drag is a
  /// division, and a drag that runs off the end of the strip clamps to the last
  /// letter rather than stopping at it.
  static String? letterAt(
    double dy,
    List<String> letters, {
    double cellHeight = CustomerAlphabetIndex.cellHeight,
  }) {
    if (letters.isEmpty) {
      return null;
    }
    final index = (dy / cellHeight).floor().clamp(0, letters.length - 1);
    return letters[index];
  }

  @override
  Widget build(BuildContext context) {
    if (letters.isEmpty) {
      return const SizedBox.shrink();
    }

    return LayoutBuilder(
      builder: (context, constraints) {
        // A book with many letters on a short screen would otherwise run past the
        // viewport; the cells shrink together rather than being cut off, because
        // a letter the strip cannot draw is a letter that cannot be reached.
        final cell = constraints.hasBoundedHeight
            ? math.min(cellHeight, constraints.maxHeight / letters.length)
            : cellHeight;

        return SizedBox(
          width: width,
          height: cell * letters.length,
          child: GestureDetector(
            // Opaque so a drag that starts on the strip keeps reporting even
            // when it runs past a letter's own cell.
            behavior: HitTestBehavior.opaque,
            onVerticalDragStart: (details) =>
                _select(details.localPosition.dy, cell),
            onVerticalDragUpdate: (details) =>
                _select(details.localPosition.dy, cell),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                for (final letter in letters)
                  GestureDetector(
                    behavior: HitTestBehavior.opaque,
                    onTap: () => onLetterSelected(letter),
                    child: _Letter(
                      letter: letter,
                      isActive: letter == activeLetter,
                      height: cell,
                    ),
                  ),
              ],
            ),
          ),
        );
      },
    );
  }

  void _select(double dy, double cell) {
    final letter = letterAt(dy, letters, cellHeight: cell);
    if (letter != null) {
      onLetterSelected(letter);
    }
  }
}

/// One letter of the strip.
class _Letter extends StatelessWidget {
  const _Letter({
    required this.letter,
    required this.isActive,
    required this.height,
  });

  final String letter;
  final bool isActive;
  final double height;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Semantics(
      button: true,
      selected: isActive,
      label: 'Jump to $letter',
      child: SizedBox(
        height: height,
        width: CustomerAlphabetIndex.width,
        child: Center(
          child: Text(
            letter,
            key: ValueKey('customer_index_$letter'),
            style: theme.textTheme.labelSmall?.copyWith(
              // Explicit rather than left to the token: the cell is 15dp at its
              // largest, and the strip's whole value is that a finger can land on
              // one letter rather than two.
              fontSize: 10.5,
              height: 1,
              color: isActive ? scheme.primary : scheme.onSurfaceVariant,
              fontWeight: isActive ? FontWeight.w700 : FontWeight.w500,
            ),
          ),
        ),
      ),
    );
  }
}
