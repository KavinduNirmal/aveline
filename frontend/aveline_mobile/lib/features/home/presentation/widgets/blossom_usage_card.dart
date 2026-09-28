import 'package:flutter/material.dart';

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
/// There is no "0" state. A balance that could not be read says so and offers a
/// retry, because `0` is a different and false statement about the shop.
///
/// The request action is the real purchase path: [onRequestMore] opens the
/// top-up sheet, which lists the server's packs, creates a checkout and hands
/// the customer to the provider. The card renders nothing about the outcome; the
/// sheet owns that, because only the server's polled intent knows it.
class BlossomUsageCard extends StatelessWidget {
  const BlossomUsageCard({super.key, required this.usage, this.onRequestMore})
      : isLoading = false,
        errorMessage = null,
        onRetry = null;

  /// The balance is on its way and nothing is known yet.
  const BlossomUsageCard.loading({super.key})
      : usage = null,
        isLoading = true,
        errorMessage = null,
        onRetry = null,
        onRequestMore = null;

  /// The balance could not be read.
  const BlossomUsageCard.unavailable({
    super.key,
    required this.errorMessage,
    required this.onRetry,
  })  : usage = null,
        isLoading = false,
        onRequestMore = null;

  /// The loaded balance, or `null` while loading or after a failure.
  final BlossomUsage? usage;

  final bool isLoading;
  final String? errorMessage;
  final VoidCallback? onRetry;

  /// Opens the purchase flow. `null` disables the button — a caller who may not
  /// purchase is not shown an action the server would refuse.
  final VoidCallback? onRequestMore;

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
                    _body(context),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _body(BuildContext context) {
    final loaded = usage;
    if (loaded != null) {
      return _figures(context, loaded);
    }
    if (isLoading) {
      return _loading(context);
    }
    return _error(context);
  }

  Widget _figures(BuildContext context, BlossomUsage loaded) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          children: [
            Text(
              _format(loaded.remaining),
              style: theme.textTheme.displayLarge?.copyWith(
                color: scheme.onSurface,
              ),
            ),
            const SizedBox(width: 8),
            Text(
              'of ${_format(loaded.allowance)} left',
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ),
        const SizedBox(height: 14),
        _Meter(fraction: loaded.usedFraction),
        const SizedBox(height: 10),
        Text(
          '${_format(loaded.used)} used this cycle · renews ${loaded.renewsOn}',
          style: theme.textTheme.bodySmall?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
        // The low-water line is the server's, not a hard-coded fraction, so the
        // note appears exactly when the backend would raise its own threshold
        // event.
        if (loaded.isRunningLow) ...[
          const SizedBox(height: 8),
          Text(
            'Running low on Blossoms.',
            key: const Key('blossom_low_water'),
            style: theme.textTheme.bodySmall?.copyWith(
              color: scheme.primary,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
        const SizedBox(height: 16),
        OutlinedButton.icon(
          onPressed: onRequestMore,
          icon: const Icon(Icons.add_rounded, size: 18),
          label: const Text('Request additional blossoms'),
        ),
      ],
    );
  }

  Widget _loading(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Padding(
      key: const Key('blossom_usage_loading'),
      padding: const EdgeInsets.symmetric(vertical: 8),
      child: Row(
        children: [
          const SizedBox(
            width: 22,
            height: 22,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
          const SizedBox(width: 16),
          Expanded(
            child: Text(
              "Reading the shop's Blossoms...",
              style: theme.textTheme.bodyMedium?.copyWith(
                color: scheme.onSurfaceVariant,
              ),
            ),
          ),
        ],
      ),
    );
  }

  Widget _error(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Column(
      key: const Key('blossom_usage_error'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          'Blossoms are not available right now.',
          style: theme.textTheme.titleMedium,
        ),
        const SizedBox(height: 6),
        Text(
          errorMessage ?? 'Try again in a moment.',
          style: theme.textTheme.bodySmall?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
        const SizedBox(height: 16),
        OutlinedButton.icon(
          onPressed: onRetry,
          icon: const Icon(Icons.refresh_rounded, size: 18),
          label: const Text('Try again'),
        ),
      ],
    );
  }

  /// `32`, or `31.5` when the decimal is real: a fractional Blossom is normal
  /// (the conversion rule charges a 0.1 minimum), and a trailing `.0` is noise.
  static String _format(double value) {
    if (value == value.roundToDouble()) {
      return value.toStringAsFixed(0);
    }
    return value.toStringAsFixed(1);
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
