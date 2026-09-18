import 'dart:math' as math;

import 'package:flutter/foundation.dart';
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
    this.petalReveal,
  }) : assert(
          petalReveal == null || petalReveal.length == 8,
          'petalReveal holds one value per petal, in draw order: the four base '
          'petals at 0/90/180/270, then the four at 45/135/225/315',
        );

  final double size;
  final Color? color;

  /// Optional per-petal reveal, in draw order: the four base petals at
  /// 0/90/180/270, then the four at 45/135/225/315.
  ///
  /// Each value in `0..1` grows that petal outwards from the centre, which is
  /// what makes a blossoming mark look like it is unfurling instead of fading
  /// in. Values above `1` overshoot, which reads as a petal springing open.
  /// `null` (the default) draws the mark fully open.
  final List<double>? petalReveal;

  @override
  Widget build(BuildContext context) {
    final effectiveColor = color ?? IconTheme.of(context).color ?? Colors.black;
    // A `Container(width: w, height: h, child: Blossom(...))` hands down TIGHT constraints,
    // and a bare `CustomPaint(size:)` is overridden by them: the mark then paints at the
    // container's size and `size` is ignored entirely (that is what made the dock blossom
    // fill its whole circle). UnconstrainedBox lays the mark out at exactly `size` and
    // centres it in the space the caller provided, while loose call sites (Stack, Row,
    // Positioned) keep behaving precisely as before.
    return UnconstrainedBox(
      child: SizedBox.square(
        dimension: size,
        child: CustomPaint(
          painter: _BlossomPainter(effectiveColor, petalReveal),
        ),
      ),
    );
  }
}

class _BlossomPainter extends CustomPainter {
  _BlossomPainter(this.color, this.petalReveal);

  /// Petal alphas at full reveal, matching the web mark.
  static const double _baseAlpha = 0.92;
  static const double _topAlpha = 0.98;
  static const double _pistilAlpha = 0.95;

  /// Petal angles in draw order: the base cross, then the diagonals.
  static const List<double> _basePetals = [0, 90, 180, 270];
  static const List<double> _topPetals = [45, 135, 225, 315];

  final Color color;
  final List<double>? petalReveal;

  /// How far petal [index] has opened. `1` is fully open; the upper bound
  /// leaves room for an overshooting curve without letting a bad caller draw a
  /// petal across the whole screen.
  double _revealAt(int index) {
    final reveal = petalReveal;
    if (reveal == null) {
      return 1;
    }
    return reveal[index].clamp(0.0, 1.4);
  }

  @override
  void paint(Canvas canvas, Size size) {
    // The web SVG uses a 24x24 viewBox; scale everything to the widget size.
    final scale = size.width / 24.0;
    final center = Offset(12 * scale, 12 * scale);

    final reveals = [
      for (var index = 0; index < 8; index++) _revealAt(index),
    ];
    final meanReveal = reveals.reduce((a, b) => a + b) / reveals.length;

    void drawPetal(
      double angleDeg,
      double rx,
      double ry,
      double alpha,
      double reveal,
    ) {
      if (reveal <= 0 || alpha <= 0) {
        return;
      }
      canvas.save();
      canvas.translate(center.dx, center.dy);
      canvas.rotate(angleDeg * math.pi / 180);
      // Ellipse centered at (12, 5.4) in viewBox coords, i.e. offset above
      // center. Unfurling scales that offset along with the petal, so the petal
      // slides outwards from the centre as it opens.
      final petalCenter = Offset(0, (5.4 - 12) * scale * reveal);
      canvas.drawOval(
        Rect.fromCenter(
          center: petalCenter,
          width: rx * 2 * scale * reveal,
          height: ry * 2 * scale * reveal,
        ),
        Paint()..color = color.withValues(alpha: alpha),
      );
      canvas.restore();
    }

    for (var index = 0; index < _basePetals.length; index++) {
      final reveal = reveals[index];
      if (reveal > 0) {
        drawPetal(
          _basePetals[index],
          3.1,
          5.3,
          _baseAlpha * reveal,
          reveal,
        );
      }
    }
    for (var index = 0; index < _topPetals.length; index++) {
      final reveal = reveals[_basePetals.length + index];
      if (reveal > 0) {
        drawPetal(
          _topPetals[index],
          2.95,
          5.1,
          _topAlpha * reveal,
          reveal,
        );
      }
    }

    // Center pistil, which opens last as the petals settle.
    if (meanReveal > 0) {
      canvas.drawCircle(
        center,
        2.3 * scale * (0.55 + 0.45 * meanReveal),
        Paint()..color = color.withValues(alpha: _pistilAlpha * meanReveal),
      );
    }
  }

  @override
  bool shouldRepaint(_BlossomPainter oldDelegate) {
    return oldDelegate.color != color ||
        !listEquals(oldDelegate.petalReveal, petalReveal);
  }
}
