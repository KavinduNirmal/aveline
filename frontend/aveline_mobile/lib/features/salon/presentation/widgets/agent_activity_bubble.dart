import 'package:flutter/material.dart';

import '../../../../shared/widgets/blossom.dart';
import '../../domain/agent_state.dart';

/// The live "Aveline is working" bubble shown while a reply is being produced. It reflects
/// the agent's current reasoning state (Thinking…, Searching…, Working…, Using a tool…) and
/// collapses away once the real reply lands. Mirrors the web `AgentActivityBubble`.
class AgentActivityBubble extends StatelessWidget {
  const AgentActivityBubble({super.key, required this.state});

  final AgentState state;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Container(
          width: 32,
          height: 32,
          alignment: Alignment.center,
          child: const Blossom(size: 22),
        ),
        const SizedBox(width: 8),
        Flexible(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 4),
                child: Text(
                  'Aveline',
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: scheme.primary,
                    fontSize: 11,
                  ),
                ),
              ),
              const SizedBox(height: 2),
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 14,
                  vertical: 10,
                ),
                decoration: BoxDecoration(
                  color: scheme.surfaceContainerLowest,
                  borderRadius: const BorderRadius.only(
                    topLeft: Radius.circular(16),
                    topRight: Radius.circular(16),
                    bottomLeft: Radius.circular(6),
                    bottomRight: Radius.circular(16),
                  ),
                  border: Border.all(color: scheme.outlineVariant),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    const _TypingDots(),
                    const SizedBox(width: 8),
                    Text(
                      _label(state),
                      style: theme.textTheme.bodyMedium?.copyWith(
                        color: scheme.onSurfaceVariant,
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ],
    );
  }

  String _label(AgentState state) {
    return switch (state) {
      AgentState.idle => 'Idle',
      AgentState.thinking => 'Thinking…',
      AgentState.searching => 'Searching…',
      AgentState.processing => 'Working…',
      AgentState.toolCall => 'Using a tool…',
      AgentState.waiting => 'Awaiting your decision…',
      AgentState.success => 'Done',
      AgentState.error => 'Something went wrong',
      AgentState.response => '',
    };
  }
}

/// Three bouncing dots used as a typing indicator.
class _TypingDots extends StatefulWidget {
  const _TypingDots();

  @override
  State<_TypingDots> createState() => _TypingDotsState();
}

class _TypingDotsState extends State<_TypingDots>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller;

  @override
  void initState() {
    super.initState();
    _controller = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 900),
    )..repeat();
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        return Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (var i = 0; i < 3; i++) ...[
              if (i > 0) const SizedBox(width: 3),
              _dot(scheme.primary, i),
            ],
          ],
        );
      },
    );
  }

  Widget _dot(Color color, int index) {
    // Stagger each dot's bounce by offsetting the phase.
    final phase = (_controller.value - index * 0.15) % 1.0;
    final offset = -4.0 * (1 - (phase * 2 - 1).abs());
    return Transform.translate(
      offset: Offset(0, offset),
      child: Container(
        width: 6,
        height: 6,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          color: color,
        ),
      ),
    );
  }
}
