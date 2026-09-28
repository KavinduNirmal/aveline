import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../../../shared/widgets/blossom.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../domain/focus_task.dart';

/// What is inside today's focus, by kind of work, plus the next one due.
///
/// `DESIGN.md` gives `display-lg` to "personalized greetings and key numbers",
/// and the page had no number about the work on it at all. Every figure here is
/// counted from the deck Home already holds, so the card cannot disagree with
/// the pile underneath it: signing a docket off moves the counts too.
///
/// The three columns are the work that *arrives* during a shift — people,
/// deliveries and intake. Approvals and sign-offs are not a queue of arrivals,
/// so they are not forced into a column; the deck's own header carries how many
/// are left. Each column carries an icon so the card can be read at a glance
/// without parsing the labels.
class TodayStrip extends StatelessWidget {
  const TodayStrip({super.key, required this.tasks});

  final List<FocusTask> tasks;

  /// The three columns, in reading order.
  static const List<FocusDomain> _kinds = [
    FocusDomain.patron,
    FocusDomain.logistics,
    FocusDomain.wardrobe,
  ];

  int _countOf(FocusDomain domain) =>
      tasks.where((task) => task.domain == domain).length;

  /// The earliest docket still on the deck.
  ///
  /// The pile cycles, so the docket in hand is not necessarily the next thing
  /// due. The server's `dueAtUtc` is the source of truth; a docket the feed sent
  /// without a timestamp falls back to its clock label, which is the
  /// compatibility path for older fixtures.
  FocusTask? get _nextUp {
    FocusTask? earliestDue;
    for (final task in tasks) {
      final due = task.dueAtUtc;
      if (due == null) {
        continue;
      }
      if (earliestDue == null || due.isBefore(earliestDue.dueAtUtc!)) {
        earliestDue = task;
      }
    }
    if (earliestDue != null) {
      return earliestDue;
    }

    FocusTask? earliest;
    for (final task in tasks) {
      final minutes = task.minutesOfDay;
      if (minutes == null) {
        continue;
      }
      if (earliest == null || minutes < earliest.minutesOfDay!) {
        earliest = task;
      }
    }
    return earliest;
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final nextUp = _nextUp;

    // Nothing to summarise. The focus section's own empty state carries the
    // message; a card counting zeroes above it would only repeat it.
    if (tasks.isEmpty) {
      return const SizedBox.shrink();
    }

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Container(
        // Clips the corner blossom to the card's own corners.
        clipBehavior: Clip.antiAlias,
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
          // The warm soft shadow from `DESIGN.md` level 1: a card earns its
          // place with light, not with a heavier border.
          boxShadow: [
            BoxShadow(
              color: const Color(0xFF8B2E42).withValues(alpha: 0.06),
              blurRadius: 20,
              offset: const Offset(0, 4),
            ),
          ],
        ),
        child: Stack(
          children: [
            // A watermark, not content: far enough into the corner that only a
            // quadrant of it shows, and behind everything else.
            const Positioned(
              right: -30,
              bottom: -34,
              child: _CornerBlossom(size: 108),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 16),
              child: Column(
                // Hugs its content: with the default `max` the card stretches to
                // whatever height it is given and leaves a band of dead space.
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  const SectionOverline('Today at a glance'),
                  const SizedBox(height: 16),
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      for (final domain in _kinds)
                        Expanded(
                          child: _KindCell(
                            icon: _kindIcon(domain),
                            count: _countOf(domain),
                            label: _kindLabel(domain),
                          ),
                        ),
                    ],
                  ),
                  // Only when something is actually timed. A row reading "no times
                  // left" beside a dash is noise, and when the deck empties the
                  // focus section's own empty state says it better than a row of
                  // zeroes would.
                  if (nextUp != null && nextUp.displayTimeLabel != null) ...[
                    const SizedBox(height: 16),
                    Container(
                      height: 1,
                      color: scheme.outlineVariant.withValues(alpha: 0.6),
                    ),
                    const SizedBox(height: 12),
                    Row(
                      children: [
                        const _IconEnclosure(
                          icon: Icons.schedule_rounded,
                          size: 26,
                        ),
                        const SizedBox(width: 10),
                        Expanded(
                          child: Text(
                            _nextLabel(nextUp.domain),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: scheme.onSurfaceVariant,
                              fontWeight: FontWeight.w500,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          nextUp.displayTimeLabel!,
                          style: theme.textTheme.headlineSmall,
                        ),
                      ],
                    ),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// What each kind of work is called when it is counted, and what the next one of
/// that kind is called when it is the next thing due.
String _kindLabel(FocusDomain domain) => switch (domain) {
      FocusDomain.patron => 'clients arriving',
      FocusDomain.logistics => 'deliveries today',
      FocusDomain.wardrobe => 'intake pieces',
      FocusDomain.commerce => 'to sign off',
    };

String _nextLabel(FocusDomain domain) => switch (domain) {
      FocusDomain.patron => 'Next arrival',
      FocusDomain.logistics => 'Next delivery',
      FocusDomain.wardrobe => 'Next intake',
      FocusDomain.commerce => 'Next sign-off',
    };

IconData _kindIcon(FocusDomain domain) => switch (domain) {
      FocusDomain.patron => Icons.person_outline_rounded,
      FocusDomain.logistics => Icons.local_shipping_outlined,
      FocusDomain.wardrobe => Icons.checkroom_rounded,
      FocusDomain.commerce => Icons.receipt_long_outlined,
    };

/// One column: the icon, the count at display size, and what is being counted.
class _KindCell extends StatelessWidget {
  const _KindCell({
    required this.icon,
    required this.count,
    required this.label,
  });

  final IconData icon;
  final int count;
  final String label;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            _IconEnclosure(icon: icon, size: 26),
            const SizedBox(width: 8),
            Flexible(
              child: FittedBox(
                fit: BoxFit.scaleDown,
                alignment: Alignment.centerLeft,
                child: Text(
                  '$count',
                  maxLines: 1,
                  style: theme.textTheme.displayLarge?.copyWith(
                    color: scheme.onSurface,
                  ),
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          label,
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: theme.textTheme.labelSmall?.copyWith(
            color: scheme.onSurfaceVariant,
            fontWeight: FontWeight.w500,
          ),
        ),
      ],
    );
  }
}

/// The soft-circle icon holder from `DESIGN.md`: icons sit in rounded containers
/// the way a stone sits in a setting.
class _IconEnclosure extends StatelessWidget {
  const _IconEnclosure({required this.icon, required this.size});

  final IconData icon;
  final double size;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: scheme.primary.withValues(alpha: 0.10),
      ),
      child: Icon(icon, size: size * 0.55, color: scheme.primary),
    );
  }
}

/// A slow-turning, faded blossom watermark for the card's corner.
///
/// Ambient like the veil's blobs, so it holds its frame under reduced motion.
class _CornerBlossom extends StatefulWidget {
  const _CornerBlossom({required this.size});

  final double size;

  @override
  State<_CornerBlossom> createState() => _CornerBlossomState();
}

class _CornerBlossomState extends State<_CornerBlossom>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 60),
  )..addListener(() => setState(() {}));

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (MediaQuery.disableAnimationsOf(context)) {
      _controller.stop();
    } else if (!_controller.isAnimating) {
      _controller.repeat();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: ExcludeSemantics(
        child: Opacity(
          opacity: 0.14,
          child: Transform.rotate(
            angle: _controller.value * 2 * math.pi,
            child: Blossom(
              size: widget.size,
              color: Theme.of(context).colorScheme.primary,
            ),
          ),
        ),
      ),
    );
  }
}
