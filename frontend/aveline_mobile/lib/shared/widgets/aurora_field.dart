import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'blossom.dart';

/// A single soft aurora blob that slowly drifts and breathes.
class _AuroraBlob extends StatefulWidget {
  const _AuroraBlob({
    required this.colors,
    required this.left,
    required this.top,
    required this.size,
    required this.duration,
    required this.phase,
    required this.opacity,
  });

  final List<Color> colors;
  final double left;
  final double top;
  final double size;
  final Duration duration;

  /// Starting point (0..1) of the animation cycle, used to desync blobs.
  final double phase;
  final double opacity;

  @override
  State<_AuroraBlob> createState() => _AuroraBlobState();
}

class _AuroraBlobState extends State<_AuroraBlob>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(vsync: this, duration: widget.duration)
      ..value = widget.phase
      ..addListener(() => setState(() {}));
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // Ambient motion is decoration: when the platform asks for reduced motion
    // the blobs hold their starting frame instead of drifting.
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
    final t = _controller.value;
    // Gentle drift + breathing scale.
    final dx = math.sin(t * 2 * math.pi) * 70;
    final dy = math.cos(t * 2 * math.pi * 0.8) * -50;
    final scale = 1 + math.sin(t * 2 * math.pi) * 0.1;

    return Positioned(
      left: widget.left,
      top: widget.top,
      child: Transform.translate(
        offset: Offset(dx, dy),
        child: Transform.scale(
          scale: scale,
          child: Container(
            width: widget.size,
            height: widget.size,
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              gradient: RadialGradient(
                colors: widget.colors,
                stops: const [0.0, 0.6, 0.75],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// A single blossom that floats, sways and rotates.
class _DriftingBlossom extends StatefulWidget {
  const _DriftingBlossom({
    required this.left,
    required this.top,
    required this.size,
    required this.color,
    required this.drift,
    required this.duration,
    required this.phase,
    required this.opacity,
  });

  final double left;
  final double top;
  final double size;
  final Color color;
  final double drift;
  final Duration duration;

  /// Starting point (0..1) of the animation cycle, used to desync blossoms.
  final double phase;
  final double opacity;

  @override
  State<_DriftingBlossom> createState() => _DriftingBlossomState();
}

class _DriftingBlossomState extends State<_DriftingBlossom>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(vsync: this, duration: widget.duration)
      ..value = widget.phase
      ..addListener(() => setState(() {}));
  }

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    // See _AuroraBlobState: the drift is ambient, not information.
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
    final t = _controller.value;
    final angle = t * 2 * math.pi;
    final y = -math.sin(angle) * widget.drift;
    final x = math.sin(angle * 0.7) * 26;
    final rotate = math.sin(angle) * 40 * math.pi / 180;
    final scale = 1 + math.sin(angle) * 0.08;

    return Positioned(
      left: widget.left,
      top: widget.top,
      child: Transform.translate(
        offset: Offset(x, y),
        child: Transform.rotate(
          angle: rotate,
          child: Transform.scale(
            scale: scale,
            child: Opacity(
              opacity: widget.opacity,
              child: Blossom(size: widget.size, color: widget.color),
            ),
          ),
        ),
      ),
    );
  }
}

/// Colorful, animated aurora + drifting blossoms, mirroring the web
/// `AuroraField`. Layered behind content (callers place it in a `Stack`).
class AuroraField extends StatelessWidget {
  const AuroraField({super.key});

  static const _blobColors = [
    [Color(0xFFFFD9DD), Color(0xFFFFE9F0), Color(0x00FFE9F0)],
    [Color(0xFFF0EAFA), Color(0xFFE6DCF7), Color(0x00E6DCF7)],
    [Color(0xFFFFE9E2), Color(0xFFFFDCC9), Color(0x00FFDCC9)],
    [Color(0xFFFDF0D8), Color(0xFFFAE6B8), Color(0x00FAE6B8)],
    [Color(0xFFFCE3EC), Color(0xFFF7CFE0), Color(0x00F7CFE0)],
    [Color(0xFFEEF6FF), Color(0xFFE0EFFF), Color(0x00E0EFFF)],
  ];

  static const _blossomColors = [
    Color(0xFFB0566B),
    Color(0xFFC9972B),
    Color(0xFF8E7CC3),
    Color(0xFFEF7A68),
    Color(0xFFD46A8B),
  ];

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: ClipRect(
        child: LayoutBuilder(
          builder: (context, constraints) {
            final width = constraints.maxWidth;
            final height = constraints.maxHeight;
            return Stack(
              children: [
                for (var i = 0; i < _blobColors.length; i++)
                  _AuroraBlob(
                    colors: _blobColors[i],
                    left: _blobLeft(i) * width,
                    top: _blobTop(i) * height,
                    size: _blobSize(i),
                    duration: Duration(seconds: 20 + (i * 3) % 12),
                    phase: (i * 0.17) % 1,
                    opacity: 0.7 + (i % 3) * 0.1,
                  ),
                for (var i = 0; i < 14; i++)
                  _DriftingBlossom(
                    left: _flowerLeft(i) * width,
                    top: _flowerTop(i) * height,
                    size: 18 + ((i * 7) % 30).toDouble(),
                    color: _blossomColors[i % _blossomColors.length],
                    drift: 14 + ((i * 5) % 18).toDouble(),
                    duration: Duration(seconds: 20 + ((i * 3) % 18)),
                    phase: ((i * 0.13) % 1),
                    opacity: 0.5 + (i % 4) * 0.1,
                  ),
              ],
            );
          },
        ),
      ),
    );
  }

  double _blobLeft(int i) => [0, 50, 30, 70, 10, 82][i] / 100;
  double _blobTop(int i) => [-10, -4, 40, 34, 60, 58][i] / 100;
  double _blobSize(int i) => [720, 700, 640, 560, 620, 480][i].toDouble();

  double _flowerLeft(int i) => ((i * 37 + 9) % 96) / 100;
  double _flowerTop(int i) => ((i * 53 + 13) % 92) / 100;
}

/// The same brand atmosphere as [AuroraField], thinned and faded out below the
/// header, for the top of a content screen.
///
/// The auth screens can afford a full field because there is little to read on
/// them. A working screen cannot: this keeps three soft blooms and a handful of
/// blossoms in the band behind the greeting, then dissolves, so the page carries
/// the brand's weather without competing with the dockets underneath.
class AuroraVeil extends StatelessWidget {
  const AuroraVeil({super.key});

  static const _blobColors = [
    [Color(0xFFFFE1E5), Color(0xFFFFEDF2), Color(0x00FFEDF2)],
    [Color(0xFFFDF3E1), Color(0xFFFBEAC6), Color(0x00FBEAC6)],
    [Color(0xFFFCE9F0), Color(0xFFF8DAE7), Color(0x00F8DAE7)],
  ];

  static const _blossomColors = [
    Color(0xFFB0566B),
    Color(0xFFC9972B),
    Color(0xFFD46A8B),
    Color(0xFFEF7A68),
  ];

  /// Where the veil has dissolved to nothing, as a fraction of the screen.
  ///
  /// The greeting and the quick actions occupy roughly the first quarter, so the
  /// fade is complete by just over that.
  static const double _fadeEnd = 0.30;

  @override
  Widget build(BuildContext context) {
    return IgnorePointer(
      child: ClipRect(
        child: ShaderMask(
          blendMode: BlendMode.dstIn,
          shaderCallback: (bounds) => const LinearGradient(
            begin: Alignment.topCenter,
            end: Alignment.bottomCenter,
            colors: [
              Colors.transparent,
              Colors.black,
              Colors.black,
              Colors.transparent,
            ],
            // Nothing at the very top. The veil starts directly under the opaque
            // header, so blooming at full strength there drew a hard line where
            // the header's flat colour met the first blob.
            stops: [0, 0.10, 0.20, _fadeEnd],
          ).createShader(bounds),
          child: LayoutBuilder(
            builder: (context, constraints) {
              final width = constraints.maxWidth;
              final height = constraints.maxHeight;
              return Stack(
                children: [
                  for (var i = 0; i < _blobColors.length; i++)
                    _AuroraBlob(
                      colors: _blobColors[i],
                      left: _blobLeft(i) * width,
                      top: _blobTop(i) * height,
                      size: _blobSize(i),
                      duration: Duration(seconds: 26 + i * 4),
                      phase: (i * 0.21) % 1,
                      opacity: 0.8,
                    ),
                  for (var i = 0; i < 5; i++)
                    _DriftingBlossom(
                      left: _flowerLeft(i) * width,
                      top: (0.02 + i * 0.042) * height,
                      size: 12 + ((i * 4) % 8).toDouble(),
                      color: _blossomColors[i % _blossomColors.length],
                      drift: 8 + ((i * 3) % 10).toDouble(),
                      duration: Duration(seconds: 24 + ((i * 5) % 14)),
                      phase: (i * 0.19) % 1,
                      opacity: 0.34 + (i % 3) * 0.07,
                    ),
                ],
              );
            },
          ),
        ),
      ),
    );
  }

  double _blobLeft(int i) => [-16, 44, 16][i] / 100;
  double _blobTop(int i) => [-20, -24, -8][i] / 100;
  double _blobSize(int i) => [420, 400, 380][i].toDouble();

  double _flowerLeft(int i) => ((i * 43 + 11) % 92) / 100;
}

/// The brand atmosphere as a working screen's backdrop.
///
/// A whisper of warmth down the page with [AuroraVeil] held behind the top band:
/// the landing page's weather, thinned so it never competes with what the screen
/// has to say. Callers place it at the bottom of a [Stack] behind their content.
class BrandBackdrop extends StatelessWidget {
  const BrandBackdrop({super.key});

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    // Derived from the tokens rather than a magic hex: a 4.5% rose wash over
    // the surface, enough to stop the page reading as one flat field.
    final wash = Color.alphaBlend(
      scheme.primary.withValues(alpha: 0.045),
      scheme.surface,
    );

    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: [scheme.surface, scheme.surface, wash],
          stops: const [0, 0.42, 1],
        ),
      ),
      child: const AuroraVeil(),
    );
  }
}

/// The brand atmosphere sized to a container rather than to the screen.
///
/// The same drifting blobs and blossoms as the full-page veil, placed as
/// fractions of the box they fill so they read the same inside a card as they do
/// behind Home. It paints outside its own bounds, so the caller is expected to
/// clip it — that is what lets a card keep its own corners.
class BlossomWash extends StatelessWidget {
  const BlossomWash({super.key});

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final width = constraints.maxWidth;
        final height = constraints.maxHeight;

        return Stack(
          clipBehavior: Clip.none,
          children: [
            for (var i = 0; i < AuroraVeil._blobColors.length; i++)
              _AuroraBlob(
                colors: AuroraVeil._blobColors[i],
                left: _blobLeft(i) * width,
                top: _blobTop(i) * height,
                size: width * _blobScale(i),
                duration: Duration(seconds: 26 + i * 5),
                phase: (i * 0.27) % 1,
                opacity: 0.75,
              ),
            for (var i = 0; i < 3; i++)
              _DriftingBlossom(
                left: _flowerLeft(i) * width,
                top: (0.12 + i * 0.32) * height,
                size: 11 + ((i * 3) % 5).toDouble(),
                color: AuroraVeil
                    ._blossomColors[i % AuroraVeil._blossomColors.length],
                drift: 7 + ((i * 4) % 8).toDouble(),
                duration: Duration(seconds: 22 + ((i * 6) % 12)),
                phase: (i * 0.23) % 1,
                opacity: 0.30 + (i % 3) * 0.06,
              ),
          ],
        );
      },
    );
  }

  /// Blobs sit mostly above the box so only their soft lower half shows.
  double _blobLeft(int i) => [-22, 46, 12][i] / 100;
  double _blobTop(int i) => [-46, -60, -30][i] / 100;
  double _blobScale(int i) => [0.86, 0.78, 0.70][i];

  double _flowerLeft(int i) => ((i * 37 + 9) % 88) / 100;
}
