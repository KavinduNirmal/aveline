import 'dart:math' as math;

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../core/auth/clerk_bootstrap.dart';
import 'blossom.dart';

/// Aveline's opening screen.
///
/// One orchestrated entrance: loose petals drift down a cream field while the
/// blossom mark unfurls, petal by petal, and turns slowly on the spot.
/// [status] drives what the screen says, so the same screen covers waiting,
/// arriving, and failing to start.
class AvelineLoadingScreen extends StatefulWidget {
  const AvelineLoadingScreen({
    super.key,
    this.status = AvelineBootStatus.preparing,
    this.failure,
    this.onRetry,
  });

  /// What the app is currently doing.
  final AvelineBootStatus status;

  /// Why startup failed. Only read when [status] is
  /// [AvelineBootStatus.failed].
  final ClerkBootstrapFailure? failure;

  /// Invoked by the failure state's retry button.
  final VoidCallback? onRetry;

  @override
  State<AvelineLoadingScreen> createState() => _AvelineLoadingScreenState();
}

class _AvelineLoadingScreenState extends State<AvelineLoadingScreen>
    with TickerProviderStateMixin {
  /// Size of the blossom mark itself; the glow around it is larger.
  static const double _markSize = 132;

  /// How much taller the glow container is than the mark.
  static const double _glowScale = 1.7;

  /// Orchestrated once: the mark fading up and its petals unfurling.
  late final AnimationController _entrance;

  /// Ambient loop: the petal drift, the mark's slow turn, the glow breathing.
  late final AnimationController _ambient;

  bool _reduceMotion = false;

  @override
  void initState() {
    super.initState();
    _entrance = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 1400),
    )..forward();
    _ambient = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 12000),
    )..repeat();
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _reduceMotion = MediaQuery.of(context).disableAnimations;
    if (_reduceMotion) {
      // Show the finished frame instead of animating to it.
      _ambient.stop();
      _ambient.value = 0;
      _entrance.value = 1;
    } else if (!_ambient.isAnimating) {
      _ambient.repeat();
    }
  }

  @override
  void didUpdateWidget(AvelineLoadingScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    // A retry after a failure replays the bloom, so the screen visibly starts
    // over instead of looking unchanged.
    final retrying = oldWidget.status == AvelineBootStatus.failed &&
        widget.status == AvelineBootStatus.preparing;
    if (retrying && !_reduceMotion) {
      _entrance.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _entrance.dispose();
    _ambient.dispose();
    super.dispose();
  }

  /// How far each of the eight petals has opened at entrance time [t].
  ///
  /// Petals spring open one after another, starting with the base cross and
  /// finishing with the diagonals, which is what makes the mark read as
  /// blooming rather than scaling up.
  List<double> _petalReveal(double t) {
    if (_reduceMotion) {
      return List<double>.filled(8, 1);
    }
    const firstPetalAt = 0.08;
    const stagger = 0.075;
    const petalDuration = 0.34;
    return List<double>.generate(8, (index) {
      final start = firstPetalAt + index * stagger;
      final progress = ((t - start) / petalDuration).clamp(0.0, 1.0);
      return Curves.easeOutBack.transform(progress);
    });
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Scaffold(
      backgroundColor: scheme.surface,
      body: Stack(
        children: [
          // Falling petals, full bleed and never interactive.
          Positioned.fill(
            child: IgnorePointer(
              child: AnimatedBuilder(
                animation: _ambient,
                builder: (context, _) => CustomPaint(
                  painter: _PetalFieldPainter(
                    progress: _ambient.value,
                    color: scheme.primaryContainer,
                  ),
                ),
              ),
            ),
          ),
          SafeArea(
            child: Center(
              child: Column(
                mainAxisSize: MainAxisSize.min,
                children: [
                  _buildMark(scheme),
                  const SizedBox(height: 44),
                  ConstrainedBox(
                    constraints: const BoxConstraints(maxWidth: 300),
                    child: Semantics(
                      liveRegion: true,
                      child: AnimatedSwitcher(
                        duration: const Duration(milliseconds: 420),
                        switchInCurve: Curves.easeOutCubic,
                        transitionBuilder: (child, animation) => FadeTransition(
                          opacity: animation,
                          child: SlideTransition(
                            position: Tween<Offset>(
                              begin: const Offset(0, 0.25),
                              end: Offset.zero,
                            ).animate(animation),
                            child: child,
                          ),
                        ),
                        child: _buildCopy(theme),
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  /// The blossom mark: a breathing glow, then the mark slowly turning.
  ///
  /// The mark has eight-fold symmetry, so a quarter turn per ambient loop reads
  /// as one calm step every few seconds. A full turn per loop would look eight
  /// times faster than intended and far too busy.
  Widget _buildMark(ColorScheme scheme) {
    return SizedBox.square(
      dimension: _markSize * _glowScale,
      child: Stack(
        alignment: Alignment.center,
        children: [
          Positioned.fill(
            child: AnimatedBuilder(
              animation: _ambient,
              builder: (context, _) {
                final breath = 0.5 + 0.5 * math.sin(_ambient.value * 4 * math.pi);
                return DecoratedBox(
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    gradient: RadialGradient(
                      colors: [
                        scheme.primary.withValues(alpha: 0.10 + 0.05 * breath),
                        scheme.primary.withValues(alpha: 0),
                      ],
                    ),
                  ),
                );
              },
            ),
          ),
          AnimatedBuilder(
            animation: Listenable.merge([_entrance, _ambient]),
            // The mark is rebuilt every frame on purpose: its petals track
            // `_entrance`, so it cannot be hoisted into `child`.
            builder: (context, _) {
              final fade = Curves.easeOut.transform(
                (_entrance.value / 0.2).clamp(0.0, 1.0),
              );
              return Opacity(
                opacity: fade,
                child: Transform.rotate(
                  angle: _reduceMotion ? 0 : _ambient.value * math.pi / 2,
                  child: Blossom(
                    size: _markSize,
                    color: scheme.primaryContainer,
                    petalReveal: _petalReveal(_entrance.value),
                  ),
                ),
              );
            },
          ),
        ],
      ),
    );
  }

  Widget _buildCopy(ThemeData theme) {
    switch (widget.status) {
      case AvelineBootStatus.preparing:
        return Text(
          'Preparing your salon',
          key: const ValueKey('preparing'),
          textAlign: TextAlign.center,
          style: theme.textTheme.labelMedium?.copyWith(
            color: theme.colorScheme.onSurfaceVariant,
            letterSpacing: 1.6,
          ),
        );
      case AvelineBootStatus.ready:
        return Text(
          'Aveline is ready',
          key: const ValueKey('ready'),
          textAlign: TextAlign.center,
          style: theme.textTheme.headlineSmall?.copyWith(
            color: theme.colorScheme.onSurface,
          ),
        );
      case AvelineBootStatus.failed:
        return _buildFailure(theme);
    }
  }

  Widget _buildFailure(ThemeData theme) {
    final scheme = theme.colorScheme;
    final failure = widget.failure;
    final isNetwork = failure?.isNetwork ?? false;

    return Column(
      key: const ValueKey('failed'),
      mainAxisSize: MainAxisSize.min,
      children: [
        Text(
          isNetwork ? "Aveline can't reach the salon" : "Aveline couldn't start",
          textAlign: TextAlign.center,
          style: theme.textTheme.headlineSmall?.copyWith(
            color: scheme.onSurface,
          ),
        ),
        const SizedBox(height: 10),
        Text(
          isNetwork
              ? 'Check your connection, then try again.'
              : 'Something went wrong while preparing the salon.',
          textAlign: TextAlign.center,
          style: theme.textTheme.bodyMedium?.copyWith(
            color: scheme.onSurfaceVariant,
          ),
        ),
        if (kDebugMode && failure != null) ...[
          const SizedBox(height: 12),
          Text(
            failure.detail,
            textAlign: TextAlign.center,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.outline,
            ),
          ),
        ],
        if (widget.onRetry != null) ...[
          const SizedBox(height: 22),
          FilledButton(
            key: const Key('loading_retry_button'),
            onPressed: widget.onRetry,
            child: const Text('Try again'),
          ),
        ],
      ],
    );
  }
}

/// One drifting petal.
@immutable
class _Petal {
  const _Petal({
    required this.x,
    required this.phase,
    required this.size,
    required this.sway,
    required this.tilt,
    required this.turns,
    required this.opacity,
  });

  /// Horizontal position across the screen, `0..1`.
  final double x;

  /// Offset into the fall, `0..1`, so petals do not fall in a rank.
  final double phase;

  /// Petal length in logical pixels.
  final double size;

  /// Horizontal drift in logical pixels.
  final double sway;

  /// Base rotation in radians.
  final double tilt;

  /// Extra turns over one fall.
  final double turns;

  /// Peak opacity.
  final double opacity;
}

/// The petal field.
///
/// Positions come from the R2 low-discrepancy sequence rather than a random
/// source, so the field looks the same on every launch and in tests. Both axes
/// need their own sequence: driving `x` and the fall phase from the same
/// golden-ratio step would correlate them and the petals would drift down in
/// visible diagonal chains instead of scattering.
final List<_Petal> _petals = List<_Petal>.generate(18, (index) {
  return _Petal(
    x: (index * 0.7548776662466927) % 1.0,
    phase: (index * 0.5698402909980532) % 1.0,
    size: 8 + (index % 4) * 2,
    sway: 4 + (index % 3) * 3,
    tilt: (index % 5) * 0.7,
    turns: 0.4 + (index % 3) * 0.25,
    opacity: 0.16 + (index % 4) * 0.07,
  );
});

/// Paints the petals falling down the screen, forever, from a looping `0..1`
/// [progress].
class _PetalFieldPainter extends CustomPainter {
  const _PetalFieldPainter({required this.progress, required this.color});

  final double progress;
  final Color color;

  /// Screens crossed per loop. Independent of the mark's rotation, which shares
  /// the same loop, so the drift stays visibly slower than a full turn.
  static const double _fallSpeed = 2.4;

  /// How far off-screen petals start and finish, so none pop into view.
  static const double _padding = 40;

  @override
  void paint(Canvas canvas, Size size) {
    final travel = size.height + _padding * 2;
    final paint = Paint();

    for (final petal in _petals) {
      final fall = (progress * _fallSpeed + petal.phase) % 1.0;
      final dy = fall * travel - _padding;
      final dx = petal.x * size.width +
          math.sin((fall + petal.phase) * 2 * math.pi) * petal.sway;

      // Fade in below the top edge and out above the bottom one.
      final edge = dy / size.height;
      final envelope = math
          .min(edge / 0.12, (1 - edge) / 0.18)
          .clamp(0.0, 1.0);

      canvas.save();
      canvas.translate(dx, dy);
      canvas.rotate(petal.tilt + fall * petal.turns * 2 * math.pi);
      paint.color = color.withValues(alpha: petal.opacity * envelope);
      // One petal, at the mark's own petal proportions (its petals are about
      // 0.59 as wide as they are long), so the drift reads as blossom rather
      // than as seed.
      canvas.drawOval(
        Rect.fromCenter(
          center: Offset.zero,
          width: petal.size * 0.88,
          height: petal.size * 1.5,
        ),
        paint,
      );
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(_PetalFieldPainter oldDelegate) =>
      oldDelegate.progress != progress || oldDelegate.color != color;
}
