import 'package:flutter/material.dart';

/// The message input at the bottom of the Salon. Enter sends; the send button
/// is disabled while empty. Mirrors the web `Composer`.
class SalonComposer extends StatefulWidget {
  const SalonComposer({
    super.key,
    required this.onSend,
    this.placeholder = 'Message Aveline...',
  });

  final ValueChanged<String> onSend;
  final String placeholder;

  @override
  State<SalonComposer> createState() => _SalonComposerState();
}

class _SalonComposerState extends State<SalonComposer> {
  final TextEditingController _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit() {
    final text = _controller.text.trim();
    if (text.isEmpty) return;
    widget.onSend(text);
    _controller.clear();
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final canSend = _controller.text.trim().isNotEmpty;

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
                controller: _controller,
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
              onPressed: canSend ? _submit : null,
              icon: const Icon(Icons.send_rounded),
              tooltip: 'Send message',
            ),
          ],
        ),
      ),
    );
  }
}
