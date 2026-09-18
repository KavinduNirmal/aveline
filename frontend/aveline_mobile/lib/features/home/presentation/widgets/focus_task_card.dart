import 'package:flutter/material.dart';

import '../../domain/focus_task.dart';

/// One docket in the Home focus pile.
///
/// The card earns its depth from [prominence]: at `1` it is the docket in hand
/// — white on tinted paper, shadowed and fully legible. As it eases towards `0`
/// its surface steps down to the recessed container tone and the copy fades out,
/// leaving a solid rounded slab behind the docket in front.
///
/// The layer behind is tinted, not merely outlined. `surfaceContainerLow` is
/// within a hair of the page colour, so a ghost built from it plus a 45%
/// hairline measured 1.00:1 against the background and read as a stray arc
/// rather than as a card. The recessed tone plus an `outline` border is what
/// makes the pile legible as a pile.
class FocusTaskCard extends StatelessWidget {
  const FocusTaskCard({
    super.key,
    required this.task,
    required this.onAction,
    this.onNext,
    this.prominence = 1,
  });

  final FocusTask task;

  /// Clears the task (the filled button).
  final VoidCallback onAction;

  /// Sends the docket to the bottom of the pile. Hidden when there is only one
  /// docket, because there would be nothing behind it to bring forward.
  final VoidCallback? onNext;

  /// `1` while the docket is in hand, easing to `0` as it becomes a layer
  /// behind the one in front.
  final double prominence;

  /// `DESIGN.md` sets 16 as the default radius for cards and primary
  /// containers, which is also what `cardTheme` uses.
  static const double _radius = 16;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final domain = _DomainChipStyle.of(task.domain);
    final inHand = prominence.clamp(0.0, 1.0);

    return Container(
      // `DESIGN.md`: every card takes 24dp padding, which is what makes the
      // information read as considered rather than crammed.
      padding: const EdgeInsets.all(24),
      decoration: BoxDecoration(
        // In hand the docket is the lightest surface on the page; behind it the
        // tone steps down, so the stack reads as depth.
        color: Color.lerp(
          scheme.surfaceContainerHigh,
          scheme.surfaceContainerLowest,
          inHand,
        ),
        borderRadius: BorderRadius.circular(_radius),
        border: Border.all(
          // Half the outline tone holds the shape at roughly 1.8:1 against the
          // recessed fill, and fades out as the docket comes forward and its
          // shadow takes over.
          color: scheme.outline.withValues(alpha: 0.5 * (1 - inHand)),
        ),
        boxShadow: [
          BoxShadow(
            color: const Color(0xFF8B2E42).withValues(alpha: 0.10 * inHand),
            blurRadius: 18,
            offset: const Offset(0, 5),
          ),
        ],
      ),
      child: Opacity(
        opacity: inHand,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                _Chip(
                  label: domain.label,
                  foreground: domain.foreground,
                  background: domain.background,
                  uppercase: true,
                ),
                const SizedBox(width: 10),
                // Expanded rather than Spacer: on a narrow phone, or with the
                // accessibility font sizes, the time yields before the row.
                //
                // The pile cycles, so a position (`2 of 4`) would read as
                // progress that never advances. How many are left is a fact
                // about the list, and it now sits in the section header.
                Expanded(
                  child: Text(
                    task.displayTimeLabel ?? '',
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    textAlign: TextAlign.end,
                    style: theme.textTheme.labelMedium?.copyWith(
                      color: scheme.onSurfaceVariant,
                    ),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 14),
            Text(
              task.title,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.headlineSmall?.copyWith(height: 1.25),
            ),
            const SizedBox(height: 10),
            Text(
              task.detail,
              maxLines: 3,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurfaceVariant,
                height: 1.5,
              ),
            ),
            const SizedBox(height: 18),
            Container(
              height: 1,
              color: scheme.outlineVariant.withValues(alpha: 0.6),
            ),
            const SizedBox(height: 16),
            Row(
              children: [
                if (onNext != null) _NextButton(onTap: onNext!),
                const Spacer(),
                _ActionButton(
                  label: task.actionLabel,
                  onTap: onAction,
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// A tonal pill. The fill carries the domain, the text carries the meaning.
///
/// `3:00 PM`-style data stays as written; only the domain label is shouted.
class _Chip extends StatelessWidget {
  const _Chip({
    required this.label,
    required this.foreground,
    required this.background,
    this.uppercase = false,
  });

  final String label;
  final Color foreground;
  final Color background;
  final bool uppercase;

  @override
  Widget build(BuildContext context) {
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 6),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(100),
      ),
      child: Text(
        uppercase ? label.toUpperCase() : label,
        style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: foreground,
              fontWeight: FontWeight.w700,
              letterSpacing: 0.6,
            ),
      ),
    );
  }
}

/// Cycles the pile without taking a decision: the same motion as a swipe, for
/// anyone who would rather tap.
class _NextButton extends StatelessWidget {
  const _NextButton({required this.onTap});

  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Semantics(
      button: true,
      label: 'Send to the back of the pile',
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          onTap: onTap,
          customBorder: const CircleBorder(),
          child: Container(
            width: 46,
            height: 46,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              border: Border.all(color: scheme.outlineVariant),
            ),
            child: Icon(
              Icons.keyboard_double_arrow_down_rounded,
              size: 22,
              color: scheme.primary,
            ),
          ),
        ),
      ),
    );
  }
}

/// The decisive action: solid wine fill, per the "highest state of importance"
/// guidance for interactive elements.
class _ActionButton extends StatelessWidget {
  const _ActionButton({required this.label, required this.onTap});

  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return FilledButton(
      onPressed: onTap,
      style: FilledButton.styleFrom(
        backgroundColor: scheme.primary,
        foregroundColor: scheme.onPrimary,
        padding: const EdgeInsets.symmetric(horizontal: 22, vertical: 14),
        shape: const StadiumBorder(),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(label),
          const SizedBox(width: 8),
          const Icon(Icons.check_rounded, size: 18),
        ],
      ),
    );
  }
}

/// The chip colour for each part of the boutique.
///
/// These are the agent-state accents from `DESIGN.md` (memory magenta, visual
/// brass, commerce wine) plus the logistics mint, used at low fill opacity so
/// they read as tonal pills rather than stickers.
class _DomainChipStyle {
  const _DomainChipStyle({
    required this.label,
    required this.foreground,
    required this.background,
  });

  final String label;
  final Color foreground;
  final Color background;

  static _DomainChipStyle of(FocusDomain domain) => switch (domain) {
        FocusDomain.logistics => const _DomainChipStyle(
            label: 'Logistics',
            foreground: Color(0xFF2E6B58),
            background: Color(0xFFE4F2EC),
          ),
        FocusDomain.patron => const _DomainChipStyle(
            label: 'Patron',
            foreground: Color(0xFF8E3A5C),
            background: Color(0xFFF7E6EE),
          ),
        FocusDomain.wardrobe => const _DomainChipStyle(
            label: 'Wardrobe',
            // Darkened from #8A6A1F, which measured 4.37:1 on this fill and
            // failed WCAG 1.4.3 for the chip's small text. This holds 6.1:1.
            foreground: Color(0xFF6E5416),
            background: Color(0xFFF6EEDC),
          ),
        FocusDomain.commerce => const _DomainChipStyle(
            label: 'Commerce',
            foreground: Color(0xFF8B2E42),
            background: Color(0xFFF7E4E8),
          ),
      };
}
