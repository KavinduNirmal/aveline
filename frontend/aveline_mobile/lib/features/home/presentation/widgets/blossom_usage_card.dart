import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../../../shared/widgets/aurora_field.dart';
import '../../../../shared/widgets/section_overline.dart';
import '../../domain/blossom_usage.dart';

/// How many Blossoms the boutique has left this cycle, and the way to ask for
/// more.
///
/// The card wears the brand atmosphere — the same drifting blobs as the veil
/// behind Home, clipped to its own corners — because this is the one figure on
/// the page about the assistant itself rather than about the floor.
///
/// The request action is UI only: it states what would happen and sends nothing.
/// Wiring it needs an approval record the owner can act on, which does not exist
/// yet.
class BlossomUsageCard extends StatelessWidget {
  const BlossomUsageCard({super.key, required this.usage});

  final BlossomUsage usage;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 20),
      child: Container(
        decoration: BoxDecoration(
          color: scheme.surfaceContainerLowest,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(
            color: scheme.outlineVariant.withValues(alpha: 0.5),
          ),
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(15),
          child: Stack(
            children: [
              const Positioned.fill(child: BlossomWash()),
              // Holds the blobs back far enough that the figures below keep
              // their contrast: this is a card with atmosphere, not a poster.
              Positioned.fill(
                child: ColoredBox(
                  color: scheme.surfaceContainerLowest.withValues(alpha: 0.45),
                ),
              ),
              Padding(
                padding: const EdgeInsets.all(20),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    const SectionOverline('Blossom usage'),
                    const SizedBox(height: 10),
                    Row(
                      crossAxisAlignment: CrossAxisAlignment.baseline,
                      textBaseline: TextBaseline.alphabetic,
                      children: [
                        Text(
                          '${usage.remaining}',
                          style: theme.textTheme.displayLarge?.copyWith(
                            color: scheme.onSurface,
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          'of ${usage.allowance} left',
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: scheme.onSurfaceVariant,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 14),
                    _Meter(fraction: usage.usedFraction),
                    const SizedBox(height: 10),
                    Text(
                      '${usage.used} used this cycle · renews ${usage.renewsOn}',
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                    const SizedBox(height: 16),
                    OutlinedButton.icon(
                      onPressed: () => _requestMore(context),
                      icon: const Icon(Icons.add_rounded, size: 18),
                      label: const Text('Request additional blossoms'),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  void _requestMore(BuildContext context) {
    AppToast.show(context, 'Request sent to the owner for approval.');
  }
}

/// How much of the cycle's allowance is spent.
class _Meter extends StatelessWidget {
  const _Meter({required this.fraction});

  final double fraction;

  static const double _height = 6;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final filled = fraction.clamp(0.0, 1.0);

    return SizedBox(
      height: _height,
      child: LayoutBuilder(
        builder: (context, constraints) => Stack(
          children: [
            Container(
              width: constraints.maxWidth,
              height: _height,
              decoration: BoxDecoration(
                color: scheme.surfaceContainerHigh,
                borderRadius: BorderRadius.circular(999),
              ),
            ),
            Container(
              width: constraints.maxWidth * filled,
              height: _height,
              decoration: BoxDecoration(
                color: scheme.primary,
                borderRadius: BorderRadius.circular(999),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
