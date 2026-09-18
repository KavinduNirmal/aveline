import 'dart:math' as math;

import 'package:flutter/material.dart';

/// The message input at the foot of a client thread.
///
/// Its own widget rather than the Salon's, because the two say different things:
/// the Salon's placeholder asks for a note to Aveline, and this one asks for a
/// message to the person at the top of the screen.
class ThreadComposer extends StatefulWidget {
  const ThreadComposer({
    super.key,
    required this.onSend,
    required this.placeholder,
    this.enabled = true,
  });

  final ValueChanged<String> onSend;
  final String placeholder;

  /// `false` while the thread is still arriving, so nothing is sent into a
  /// conversation the screen has not read yet.
  final bool enabled;

  @override
  State<ThreadComposer> createState() => _ThreadComposerState();
}

class _ThreadComposerState extends State<ThreadComposer> {
  final TextEditingController _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit() {
    final text = _controller.text.trim();
    if (text.isEmpty) {
      return;
    }
    widget.onSend(text);
    _controller.clear();
    setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final canSend =
        widget.enabled && _controller.text.trim().isNotEmpty;

    return Container(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
      decoration: BoxDecoration(
        color: scheme.surface,
        border: Border(top: BorderSide(color: scheme.outlineVariant)),
      ),
      child: SafeArea(
        top: false,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            Expanded(
              child: TextField(
                key: const Key('thread_composer_field'),
                controller: _controller,
                enabled: widget.enabled,
                minLines: 1,
                maxLines: 4,
                textCapitalization: TextCapitalization.sentences,
                onChanged: (_) => setState(() {}),
                onSubmitted: (_) => _submit(),
                decoration: InputDecoration(
                  hintText: widget.placeholder,
                  isDense: true,
                ),
              ),
            ),
            const SizedBox(width: 8),
            IconButton.filled(
              key: const Key('thread_send'),
              onPressed: canSend ? _submit : null,
              // The paper-plane glyph points straight right, which reads as
              // off-centre inside a circular button; tilting it 45° gives the
              // familiar "sent" pose.
              icon: Transform.rotate(
                angle: -math.pi / 4,
                child: const Icon(Icons.send_rounded),
              ),
              tooltip: 'Send message',
            ),
          ],
        ),
      ),
    );
  }
}
