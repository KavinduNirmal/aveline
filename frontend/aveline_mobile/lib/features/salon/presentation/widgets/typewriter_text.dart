import 'dart:async';

import 'package:flutter/material.dart';

/// Reveals text word by word (a typewriter effect) like a streaming LLM reply. Mirrors the
/// web `TypewriterText`.
class TypewriterText extends StatefulWidget {
  const TypewriterText({
    super.key,
    required this.text,
    this.style,
    this.wordsPerTick = 1,
    this.tick = const Duration(milliseconds: 40),
    this.onProgress,
  });

  final String text;
  final TextStyle? style;

  /// Words revealed per tick.
  final int wordsPerTick;

  /// Delay between ticks.
  final Duration tick;

  /// Called whenever the visible text grows, so the thread can keep the tail in view.
  final VoidCallback? onProgress;

  @override
  State<TypewriterText> createState() => _TypewriterTextState();
}

class _TypewriterTextState extends State<TypewriterText> {
  late final List<String> _words;
  late int _count;
  Timer? _timer;

  bool get _done => _count >= _words.length;

  @override
  void initState() {
    super.initState();
    _words = widget.text.split(' ');
    _count = 0;
    _start();
  }

  void _start() {
    _timer = Timer.periodic(widget.tick, (_) {
      if (!mounted) return;
      setState(() {
        _count = (_count + widget.wordsPerTick).clamp(0, _words.length);
      });
      widget.onProgress?.call();
      if (_done) {
        _timer?.cancel();
      }
    });
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final shown = _words.take(_count).join(' ');
    return Text(shown, style: widget.style);
  }
}
