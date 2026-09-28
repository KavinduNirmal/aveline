import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../../../shared/widgets/chat_input.dart';

import '../../../conversations/presentation/attachment_picker.dart';
import '../../../conversations/presentation/client_thread_controller.dart';
import '../../../conversations/presentation/widgets/attachment_tray.dart';

/// The message input at the bottom of the Salon. Enter sends; the send button
/// is disabled while empty. Mirrors the web `Composer`.
///
/// Attachments work the way a client thread's do: the paperclip picks from the gallery or
/// the camera, each file uploads on pick, and the send waits for every upload rather than
/// naming bytes the server has not stored. The web draws its Salon drawer with the same
/// `Composer` as its threads, so this is the parity that matters.
class SalonComposer extends StatefulWidget {
  const SalonComposer({
    super.key,
    required this.onSend,
    this.placeholder = 'Message Aveline...',
    this.enabled = true,
    this.onAttach,
    this.attachments = const [],
    this.sendReady = true,
    this.onRemoveAttachment,
    this.onRetryAttachment,
  });

  final ValueChanged<String> onSend;
  final String placeholder;

  /// `false` while the Salon is still resolving its conversation, so nothing is sent into
  /// a conversation the screen has not opened yet.
  final bool enabled;

  /// Opens the picker. `null` leaves the paperclip undrawn, which is what a preview wants.
  final Future<void> Function(AttachmentSource source)? onAttach;

  /// The files picked and not yet sent.
  final List<PendingThreadAttachment> attachments;

  /// Whether every held file is stored. A send waits for its uploads rather than naming
  /// bytes the server has not written.
  final bool sendReady;

  final void Function(String localId)? onRemoveAttachment;
  final void Function(String localId)? onRetryAttachment;

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

  /// Offers the gallery or the camera, so the source is a deliberate choice rather than a
  /// hidden long-press.
  Future<void> _chooseSource() async {
    final onAttach = widget.onAttach;
    if (onAttach == null) {
      return;
    }

    final source = await chooseAttachmentSource(context, keyPrefix: 'salon');
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
                keyPrefix: 'salon',
              ),
            Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                if (widget.onAttach != null && widget.enabled)
                  IconButton(
                    key: const Key('salon_attach'),
                    onPressed: _chooseSource,
                    icon: const Icon(Icons.attach_file_rounded),
                    tooltip: 'Attach a photo or a document',
                  ),
                Expanded(
                  child: TextField(
                    controller: _controller,
                    enabled: widget.enabled,
                    minLines: 1,
                    maxLines: 4,
                    textCapitalization: TextCapitalization.sentences,
                    onChanged: (_) => setState(() {}),
                    onSubmitted: (_) => _submit(),
                    style: chatInputStyle(context),
                    decoration: chatInputDecoration(
                      context,
                      hintText: widget.placeholder,
                    ),
                  ),
                ),
                const SizedBox(width: 8),
                IconButton.filled(
                  onPressed: canSend ? _submit : null,
                  // The paper-plane glyph points straight right, which reads as off-centre
                  // inside a circular button; tilting it 45° gives the familiar "sent" pose.
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
