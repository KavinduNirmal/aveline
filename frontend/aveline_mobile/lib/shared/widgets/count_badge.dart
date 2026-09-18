import 'package:flutter/material.dart';

/// A small filled count, worn on an icon or at the end of a row.
///
/// Extracted so the header's notification badge and the message inbox's unread
/// counts are the same mark: both wear the brand's wine, both cap at a single
/// digit, and neither drifts from the other as the app grows.
class CountBadge extends StatelessWidget {
  const CountBadge({
    super.key,
    required this.count,
    this.ringColor,
    this.semanticLabel,
    this.maxCount = 9,
  });

  /// The number to wear. Anything above [maxCount] is printed as `9+`, and a
  /// count below one is printed as `1` rather than as nothing, because a caller
  /// that drew a badge at all means to show one.
  final int count;

  /// The colour of the ring around the badge, or `null` for no ring.
  ///
  /// A badge that overlaps something - an icon, an avatar - needs a ring in the
  /// colour of the surface behind it to stay legible. A badge sitting at the end
  /// of its own row does not.
  final Color? ringColor;

  /// What a screen reader says instead of the bare number.
  final String? semanticLabel;

  /// The largest count spelled out before it becomes `9+`.
  ///
  /// Past a single digit the exact number stops being useful, and a wider badge
  /// would crowd whatever it hangs off.
  final int maxCount;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final shown = count < 1 ? 1 : count;
    final label = shown > maxCount ? '$maxCount+' : '$shown';

    final badge = Container(
      constraints: const BoxConstraints(minWidth: 16),
      height: 16,
      padding: const EdgeInsets.symmetric(horizontal: 4),
      decoration: BoxDecoration(
        color: scheme.primary,
        borderRadius: BorderRadius.circular(8),
        border: ringColor == null
            ? null
            : Border.all(color: ringColor!, width: 1.5),
      ),
      alignment: Alignment.center,
      child: Text(
        label,
        style: TextStyle(
          color: scheme.onPrimary,
          fontSize: 10,
          height: 1,
          fontWeight: FontWeight.w700,
        ),
      ),
    );

    if (semanticLabel == null) {
      return badge;
    }
    return Semantics(label: semanticLabel, excludeSemantics: true, child: badge);
  }
}
