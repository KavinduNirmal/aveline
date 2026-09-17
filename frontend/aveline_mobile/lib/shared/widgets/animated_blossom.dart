import 'package:flutter/material.dart';

import '../../features/salon/presentation/screens/salon_screen.dart';
import 'blossom.dart';

/// Floating, gently animated Aveline Blossom mark.
///
/// Serves as the primary brand centerpiece and launcher for the concierge Salon.
/// Features a continuous subtle breathing pulse and glowing radial shadow.
class AnimatedBlossom extends StatefulWidget {
  const AnimatedBlossom({
    super.key,
    this.onTap,
    this.size = 56,
  });

  /// Optional override for the tap action.
  /// When `null`, navigates to [SalonScreen].
  final VoidCallback? onTap;

  /// Overall dimension of the circular blossom button.
  final double size;

  @override
  State<AnimatedBlossom> createState() => _AnimatedBlossomState();
}

class _AnimatedBlossomState extends State<AnimatedBlossom>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;
  late final Animation<double> _scaleAnimation;
  late final Animation<double> _glowAnimation;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2400),
    )..repeat(reverse: true);

    _scaleAnimation = Tween<double>(begin: 1.0, end: 1.06).animate(
      CurvedAnimation(parent: _controller, curve: Curves.easeInOutSine),
    );

    _glowAnimation = Tween<double>(begin: 12.0, end: 22.0).animate(
      CurvedAnimation(parent: _controller, curve: Curves.easeInOutSine),
    );
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _handleTap() {
    if (widget.onTap != null) {
      widget.onTap!();
    } else {
      Navigator.of(context).push(
        MaterialPageRoute<void>(
          builder: (_) => const SalonScreen(),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    return Semantics(
      button: true,
      label: 'Open Salon',
      child: GestureDetector(
        key: const Key('animated_blossom_button'),
        onTap: _handleTap,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, child) {
            return Transform.scale(
              scale: _scaleAnimation.value,
              child: Container(
                width: widget.size,
                height: widget.size,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  gradient: LinearGradient(
                    begin: Alignment.topLeft,
                    end: Alignment.bottomRight,
                    colors: [
                      scheme.primary,
                      const Color(0xFFC05267),
                    ],
                  ),
                  boxShadow: [
                    BoxShadow(
                      color: scheme.primary.withValues(alpha: 0.35),
                      blurRadius: _glowAnimation.value,
                      offset: const Offset(0, 6),
                    ),
                  ],
                ),
                child: child,
              ),
            );
          },
          child: Blossom(
            size: widget.size * 0.54,
            color: Colors.white,
          ),
        ),
      ),
    );
  }
}
