import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../client_thread_controller.dart';
import '../attachment_picker.dart';
import 'attachment_tray.dart';

/// The message input at the foot of a client thread.
///
/// Its own widget rather than the Salon's, because the two say different things:
/// the Salon's placeholder asks for a note to Aveline, and this one asks for a
/// message to the person at the top of the screen. The attachments they hold are the
/// same thing, so the tray and the source sheet are shared.
class ThreadComposer extends StatefulWidget {
  const ThreadComposer({
    super.key,
    required this.onSend,
    required this.placeholder,
    this.enabled = true,
    this.onAttach,
    this.attachments = const [],
    this.sendReady = true,
    this.onRemoveAttachment,
    this.onRetryAttachment,
  });

  final ValueChanged<String> onSend;
  final String placeholder;

  /// `false` while the thread is still arriving, so nothing is sent into a
  /// conversation the screen has not read yet.
  final bool enabled;

  /// Opens the picker. `null` leaves the paperclip undrawn, which is what a preview wants.
  final Future<void> Function(AttachmentSource source)? onAttach;

  /// The files picked and not yet sent.
  final List<PendingThreadAttachment> attachments;

  /// Whether every held file is stored. A send waits for its uploads rather than naming bytes
  /// the server has not written.
  final bool sendReady;

  final void Function(String localId)? onRemoveAttachment;
  final void Function(String localId)? onRetryAttachment;

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

  /// Offers the gallery or the camera, so the source is a deliberate choice rather than a
  /// hidden long-press.
  Future<void> _chooseSource() async {
    final onAttach = widget.onAttach;
    if (onAttach == null) {
      return;
    }

    final source = await chooseAttachmentSource(context);
    if (source != null) {
      await onAttach(source);
    }
  }

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final canSend =
        widget.enabled &&
        widget.sendReady &&
        _controller.text.trim().isNotEmpty;

    return Container(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
      decoration: BoxDecoration(
        color: scheme.surface,
        border: Border(top: BorderSide(color: scheme.outlineVariant)),
      ),
      child: SafeArea(
        top: false,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            if (widget.attachments.isNotEmpty)
              AttachmentTray(
                attachments: widget.attachments,
                onRemove: widget.onRemoveAttachment,
                onRetry: widget.onRetryAttachment,
              ),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                if (widget.onAttach != null && widget.enabled)
                  IconButton(
                    key: const Key('thread_attach'),
                    onPressed: _chooseSource,
                    icon: const Icon(Icons.attach_file_rounded),
                    tooltip: 'Attach a photo or a document',
                  ),
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
          ],
        ),
      ),
    );
  }
}
