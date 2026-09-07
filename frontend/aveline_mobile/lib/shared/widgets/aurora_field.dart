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
      ..addListener(() => setState(() {}))
      ..repeat();
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
      ..addListener(() => setState(() {}))
      ..repeat();
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
