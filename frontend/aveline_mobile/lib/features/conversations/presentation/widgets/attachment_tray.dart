import 'package:flutter/material.dart';

import '../client_thread_controller.dart';

/// What is about to be sent, with a remove action and a retry for one that failed.
///
/// Shared by the client thread's composer and the Salon's, because both hold the same
/// thing: bytes the associate picked, uploading to the same route and bound by the
/// message that eventually names them.
class AttachmentTray extends StatelessWidget {
  const AttachmentTray({
    super.key,
    required this.attachments,
    this.onRemove,
    this.onRetry,
    this.keyPrefix = 'thread',
  });

  final List<PendingThreadAttachment> attachments;
  final void Function(String localId)? onRemove;
  final void Function(String localId)? onRetry;

  /// Namespaces the widget keys, so a test can say which composer it is asserting on
  /// when both are reachable.
  final String keyPrefix;

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
          key: ValueKey('${keyPrefix}_attachment_tray'),
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
                    key: ValueKey('${keyPrefix}_pending_${attachment.localId}'),
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
                            '${keyPrefix}_retry_upload_${attachment.localId}',
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
                      key: ValueKey(
                        '${keyPrefix}_remove_${attachment.localId}',
                      ),
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
            key: ValueKey('${keyPrefix}_attachment_error'),
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
