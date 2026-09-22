import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../client_thread_controller.dart';
import '../attachment_picker.dart';

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

    final source = await showModalBottomSheet<AttachmentSource>(
      context: context,
      builder: (context) => SafeArea(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            ListTile(
              key: const Key('thread_attach_gallery'),
              leading: const Icon(Icons.photo_library_outlined),
              title: const Text('Photo library'),
              onTap: () => Navigator.of(context).pop(AttachmentSource.gallery),
            ),
            ListTile(
              key: const Key('thread_attach_camera'),
              leading: const Icon(Icons.photo_camera_outlined),
              title: const Text('Camera'),
              onTap: () => Navigator.of(context).pop(AttachmentSource.camera),
            ),
          ],
        ),
      ),
    );

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
              _AttachmentTray(
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

/// What is about to be sent, with a remove action and a retry for one that failed.
class _AttachmentTray extends StatelessWidget {
  const _AttachmentTray({
    required this.attachments,
    this.onRemove,
    this.onRetry,
  });

  final List<PendingThreadAttachment> attachments;
  final void Function(String localId)? onRemove;
  final void Function(String localId)? onRetry;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;

    // The first failure's reason, if any. The tray used to say a file failed with a red border
    // and a retry icon alone; those stay, and this gives them words so the associate knows
    // which file failed and can decide whether to retry it.
    String? failure;
    for (final attachment in attachments) {
      if (attachment.isFailed) {
        failure = attachment.error;
        if (failure != null) {
          break;
        }
      }
    }

    return Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          key: const Key('thread_attachment_tray'),
          height: 76,
          child: ListView.separated(
            scrollDirection: Axis.horizontal,
            itemCount: attachments.length,
            separatorBuilder: (context, index) => const SizedBox(width: 8),
            itemBuilder: (context, index) {
              final attachment = attachments[index];
              return Stack(
                clipBehavior: Clip.none,
                children: [
                  Container(
                    key: ValueKey('thread_pending_${attachment.localId}'),
                    width: 64,
                    height: 64,
                    decoration: BoxDecoration(
                      color: scheme.surfaceContainerHigh,
                      borderRadius: BorderRadius.circular(10),
                      border: Border.all(
                        color: attachment.isFailed
                            ? scheme.error
                            : scheme.outlineVariant,
                      ),
                    ),
                    clipBehavior: Clip.antiAlias,
                    child: attachment.contentType.startsWith('image/')
                        ? Image.memory(attachment.bytes, fit: BoxFit.cover)
                        : Icon(
                            Icons.description_outlined,
                            color: scheme.primary,
                          ),
                  ),
                  if (attachment.uploading)
                    const Positioned.fill(
                      child: Center(
                        child: SizedBox(
                          width: 18,
                          height: 18,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        ),
                      ),
                    ),
                  if (attachment.isFailed)
                    Positioned.fill(
                      child: Center(
                        child: IconButton(
                          key: ValueKey(
                            'thread_retry_upload_${attachment.localId}',
                          ),
                          onPressed: onRetry == null
                              ? null
                              : () => onRetry!(attachment.localId),
                          icon: Icon(Icons.refresh_rounded, color: scheme.error),
                          tooltip: 'Upload again',
                        ),
                      ),
                    ),
                  Positioned(
                    top: -6,
                    right: -6,
                    child: IconButton(
                      key: ValueKey('thread_remove_${attachment.localId}'),
                      onPressed: onRemove == null
                          ? null
                          : () => onRemove!(attachment.localId),
                      iconSize: 16,
                      visualDensity: VisualDensity.compact,
                      style: IconButton.styleFrom(
                        backgroundColor: scheme.surfaceContainerHighest,
                      ),
                      icon: const Icon(Icons.close_rounded),
                      tooltip: 'Remove',
                    ),
                  ),
                ],
              );
            },
          ),
        ),
        if (failure != null)
          Padding(
            key: const Key('thread_attachment_error'),
            padding: const EdgeInsets.only(top: 2, left: 4, right: 4),
            child: Row(
              children: [
                Icon(Icons.error_outline, size: 16, color: scheme.error),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    failure,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(
                      context,
                    ).textTheme.bodySmall?.copyWith(color: scheme.error),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}
