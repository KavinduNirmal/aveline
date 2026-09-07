import 'dart:math' as math;

import 'package:flutter/material.dart';

/// Aveline's 8-petal blossom mark, matching the web `Blossom.tsx` design.
///
/// Composed of two 4-petal layers offset by 45° plus a center pistil. Rendered
/// with a [CustomPainter] so no SVG dependency is required. The petal color is
/// [color] (defaults to the ambient icon color).
class Blossom extends StatelessWidget {
  const Blossom({
    super.key,
    this.size = 24,
    this.color,
  });

  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final effectiveColor = color ?? IconTheme.of(context).color ?? Colors.black;
    return CustomPaint(
      size: Size.square(size),
      painter: _BlossomPainter(effectiveColor),
    );
  }
}

class _BlossomPainter extends CustomPainter {
  _BlossomPainter(this.color);

  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    // The web SVG uses a 24x24 viewBox; scale everything to the widget size.
    final scale = size.width / 24.0;
    final center = Offset(12 * scale, 12 * scale);

    final basePaint = Paint()..color = color.withValues(alpha: 0.92);
    final topPaint = Paint()..color = color.withValues(alpha: 0.98);
    final pistilPaint = Paint()..color = color.withValues(alpha: 0.95);

    void drawPetal(double angleDeg, double rx, double ry, Paint paint) {
      canvas.save();
      canvas.translate(center.dx, center.dy);
      canvas.rotate(angleDeg * math.pi / 180);
      // Ellipse centered at (12, 5.4) in viewBox coords, i.e. offset above center.
      final petalCenter = Offset(0, (5.4 - 12) * scale);
      canvas.drawOval(
        Rect.fromCenter(
          center: petalCenter,
          width: rx * 2 * scale,
          height: ry * 2 * scale,
        ),
        paint,
      );
      canvas.restore();
    }

    // Base 4 petals at 0/90/180/270.
    for (final angle in [0, 90, 180, 270]) {
      drawPetal(angle.toDouble(), 3.1, 5.3, basePaint);
    }
    // Top 4 petals offset by 45°.
    for (final angle in [45, 135, 225, 315]) {
      drawPetal(angle.toDouble(), 2.95, 5.1, topPaint);
    }
    // Center pistil.
    canvas.drawCircle(center, 2.3 * scale, pistilPaint);
  }

  @override
  bool shouldRepaint(_BlossomPainter oldDelegate) => oldDelegate.color != color;
}
