import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../../../shared/widgets/blossom.dart';
import '../../domain/agent_state.dart';

/// Aveline's animated blossom avatar, reflecting her current agentic-workflow state.
///
/// Mirrors the web `AvelineAvatar`. Each [AgentState] maps to an animation primitive
/// (breathing, pulse, ripple, rotation, sway, bloom, shake) so the flower behaviour is
/// consistent across the web and Flutter chats.
class AgentAvatar extends StatefulWidget {
  const AgentAvatar({
    super.key,
    this.state = AgentState.idle,
    this.size = 24,
    this.color,
  });

  final AgentState state;
  final double size;
  final Color? color;

  @override
  State<AgentAvatar> createState() => _AgentAvatarState();
}

class _AgentAvatarState extends State<AgentAvatar>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late AgentState _state;

  @override
  void initState() {
    super.initState();
    _state = widget.state;
    _controller = AnimationController(vsync: this);
    _configureForState(_state);
  }

  @override
  void didUpdateWidget(AgentAvatar oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.state != widget.state) {
      _configureForState(widget.state);
    }
  }

  /// Configures the controller for the given state: looping primitives repeat, while
  /// one-shot bloom/shake play once and settle back to idle.
  void _configureForState(AgentState state) {
    _state = state;
    _controller.stop();
    _controller.reset();

    switch (state) {
      case AgentState.idle:
        _controller
          ..duration = const Duration(seconds: 3)
          ..repeat(reverse: true);
      case AgentState.thinking:
        _controller
          ..duration = const Duration(milliseconds: 800)
          ..repeat(reverse: true);
      case AgentState.searching:
        _controller
          ..duration = const Duration(milliseconds: 1800)
          ..repeat();
      case AgentState.processing:
        _controller
          ..duration = const Duration(seconds: 4)
          ..repeat();
      case AgentState.toolCall:
        _controller
          ..duration = const Duration(milliseconds: 250)
          ..repeat(reverse: true);
      case AgentState.waiting:
        _controller
          ..duration = const Duration(milliseconds: 2400)
          ..repeat(reverse: true);
      case AgentState.success:
      case AgentState.response:
        _controller
          ..duration = const Duration(milliseconds: 900)
          ..forward().whenComplete(() {
            if (mounted) _configureForState(AgentState.idle);
          });
      case AgentState.error:
        _controller
          ..duration = const Duration(milliseconds: 500)
          ..forward().whenComplete(() {
            if (mounted) _configureForState(AgentState.idle);
          });
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final effectiveColor = widget.color ?? IconTheme.of(context).color;
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        return _AgentAvatarPainter(
          state: _state,
          progress: _controller.value,
          size: widget.size,
          color: effectiveColor,
        );
      },
    );
  }
}

/// Paints the blossom with the transform/ripple driven by the current state + progress.
class _AgentAvatarPainter extends StatelessWidget {
  const _AgentAvatarPainter({
    required this.state,
    required this.progress,
    required this.size,
    required this.color,
  });

  final AgentState state;
  final double progress;
  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final scale = _scaleFor(state, progress);
    final rotation = _rotationFor(state, progress);
    final showRipple = state == AgentState.searching;

    return SizedBox(
      width: size,
      height: size,
      child: Transform.rotate(
        angle: rotation,
        child: Transform.scale(
          scale: scale,
          child: Stack(
            alignment: Alignment.center,
            children: [
              if (showRipple) _RippleRings(progress: progress, size: size, color: color),
              Blossom(size: size, color: color),
            ],
          ),
        ),
      ),
    );
  }

  double _scaleFor(AgentState state, double t) {
    switch (state) {
      case AgentState.idle:
        // Breathing: 1 -> 1.05 -> 1.
        return 1 + 0.05 * math.sin(t * math.pi);
      case AgentState.thinking:
        // Slow contraction/pulse: 1 -> 0.92 -> 1.
        return 1 - 0.08 * math.sin(t * math.pi);
      case AgentState.toolCall:
        // Mechanical pulse: quick snap down and back.
        return 1 - 0.12 * math.sin(t * math.pi);
      case AgentState.success:
      case AgentState.response:
        // Bloom: 0.7 -> 1.08 -> 1.
        if (t < 0.6) return 0.7 + (1.08 - 0.7) * (t / 0.6);
        return 1.08 - (1.08 - 1.0) * ((t - 0.6) / 0.4);
      case AgentState.error:
        // Brief contraction then settle.
        return 1 - 0.15 * math.sin(t * math.pi);
      default:
        return 1;
    }
  }

  double _rotationFor(AgentState state, double t) {
    switch (state) {
      case AgentState.processing:
        return t * 2 * math.pi;
      case AgentState.toolCall:
        // Stepped mechanical rotation: 12 discrete steps per cycle.
        return (t * 12).floor() * (2 * math.pi / 12);
      case AgentState.waiting:
        // Slow sway: rock ±12 degrees.
        return math.sin(t * math.pi) * (12 * math.pi / 180);
      default:
        return 0;
    }
  }
}

/// Expanding ripple rings shown while Aveline is searching.
class _RippleRings extends StatelessWidget {
  const _RippleRings({required this.progress, required this.size, required this.color});

  final double progress;
  final double size;
  final Color? color;

  @override
  Widget build(BuildContext context) {
    final effectiveColor = color ?? Colors.black;
    return CustomPaint(
      size: Size.square(size),
      painter: _RipplePainter(progress, effectiveColor),
    );
  }
}

class _RipplePainter extends CustomPainter {
  _RipplePainter(this.progress, this.color);

  final double progress;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    final center = Offset(size.width / 2, size.height / 2);
    final baseRadius = size.width * 0.42;
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = size.width * 0.03
      ..color = color;

    // Three staggered rings expanding outward and fading.
    for (var i = 0; i < 3; i++) {
      final phase = (progress + i / 3) % 1.0;
      final radius = baseRadius * (0.4 + phase * 1.2);
      final opacity = (1 - phase) * 0.6;
      paint.color = color.withValues(alpha: opacity);
      canvas.drawCircle(center, radius, paint);
    }
  }

  @override
  bool shouldRepaint(_RipplePainter oldDelegate) =>
      oldDelegate.progress != progress || oldDelegate.color != color;
}
