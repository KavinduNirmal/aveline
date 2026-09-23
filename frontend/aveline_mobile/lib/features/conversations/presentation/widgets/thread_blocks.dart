import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../../shared/persona.dart';
import '../../../../shared/utils/currency_formatter.dart';
import '../../../../shared/widgets/app_toast.dart';
import '../../domain/thread_message.dart';
import '../../domain/tile_blocks.dart';
import 'message_blocks.dart';

/// A one-line summary of a content block the thread has no card for.
///
/// A block-only message must never draw an empty bubble, so every block type in
/// Aveline's vocabulary maps to something readable: a piece to its name and price,
/// a look to its name, an `at_a_glance` table to how many details it holds, a
/// payment to its amount or status, a courier to its carrier and status. A type
/// this build has not been taught falls back to its own text, and then to a plain
/// "Update", rather than to nothing.
String threadBlockSummary(ThreadBlock block) => switch (block.type) {
  'piece' => _piece(block),
  'look' => block.name ?? block.text ?? 'A new look',
  'at_a_glance' => _details(block.rows.length),
  'payment' => block.amount != null
      ? 'Payment · ${rupees(block.amount!)}'
      : 'Payment · ${block.status ?? 'pending'}',
  'courier' => _courier(block),
  'sign_off' => _signOffSummary(block),
  'attachment' => block.fileName ?? block.name ?? 'Attachment',
  'text' => block.text ?? '',
  'client_message' => block.text ?? '',
  _ => block.text ?? 'Update',
};

/// A piece card's one line: its name, and its price when the server priced it.
String _piece(ThreadBlock block) {
  final parts = <String>[block.name ?? 'A piece'];
  if (block.amount != null) {
    parts.add(rupees(block.amount!));
  }
  return parts.join(' · ');
}

/// A courier card's one line: its carrier and status, whichever the server sent.
String _courier(ThreadBlock block) {
  final parts = <String>['Courier'];
  if (block.carrier != null) {
    parts.add(block.carrier!);
  }
  if (block.status != null) {
    parts.add(block.status!);
  }
  return parts.join(' · ');
}

/// What a `sign_off` block says when the message carries no words of its own.
String _signOffSummary(ThreadBlock block) {
  if (block.reason != null) {
    return block.reason!;
  }
  if (block.amount != null) {
    return 'Approval needed for ${rupees(block.amount!)}';
  }
  return 'Approval needed';
}

String _details(int count) => count == 1 ? '1 detail' : '$count details';

/// The briefing cards a message carries.
///
/// The bubble draws the message's `text` / `client_message` block as its own words
/// and its `sign_off` block as its overline and decision row; everything else is
/// drawn here, in the order the server sent it, so a `suggestion`, a `choice` or a
/// `piece` is never silently dropped.
///
/// The switch is deliberately open: a block type this build has not been taught
/// renders its one-line summary rather than disappearing.
class ThreadMessageBlocks extends StatelessWidget {
  const ThreadMessageBlocks({
    super.key,
    required this.message,
    this.onSelectCustomer,
    this.loadAttachment,
    this.openAttachment,
  });

  final ThreadMessage message;

  /// Resolves a `choice` option. `null` leaves the options inert, which is what a
  /// preview or a test with no controller wants.
  final void Function(ThreadBlock block, Map<String, dynamic> option)?
  onSelectCustomer;

  /// Fetches an attachment's bytes through the authenticated client.
  ///
  /// The stored URL is not anonymous, so a thumbnail is never `Image.network`: the bytes are
  /// read through the same [Dio] every other call uses, and rendered from memory. A `null`
  /// loader leaves the block as its one-line summary, which is what a preview wants.
  final Future<Uint8List> Function(String attachmentId)? loadAttachment;

  /// Opens a document through the platform viewer. `null` leaves the chip inert, which is what
  /// a preview wants.
  final Future<void> Function(Uint8List bytes, String fileName)? openAttachment;

  @override
  Widget build(BuildContext context) {
    final blocks = message.bodyBlocks;
    if (blocks.isEmpty) {
      return const SizedBox.shrink();
    }

    // A run of consecutive tiles has to reach the renderer *together*: the grouping is
    // what lays a curated set across the bubble instead of stacking it one card per
    // line, and a per-block call could never see the row.
    return Column(
      crossAxisAlignment: message.isFromStaff
          ? CrossAxisAlignment.end
          : CrossAxisAlignment.start,
      children: [
        for (final group in groupTileBlocks(blocks))
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: switch (group) {
              TileRun(:final blocks) => MessageBlockList(
                messageId: message.id,
                blocks: blocks,
                persona: personaForAgent(message.agentKey),
              ),
              LoneBlock(:final block) => _block(context, block),
            },
          ),
      ],
    );
  }

  /// The block types the shared renderer gives a real treatment to.
  ///
  /// A run of tiles never reaches here — `groupTileBlocks` has already sent it to the
  /// renderer — but a *lone* look does, because without a photograph of its own it is
  /// Elle's styling note rather than a tile. Everything else is either drawn by the
  /// bubble itself (`text`, `client_message`, `sign_off`), carries a thread-specific
  /// card (`choice`, `attachment`, and the `suggestion` the associate copies out), or is
  /// a block this build has not been taught — which is what the open default keeps from
  /// drawing an empty bubble.
  static const Set<String> _richTypes = {
    'look',
    'at_a_glance',
    'payment',
    'courier',
  };

  Widget _block(BuildContext context, ThreadBlock block) {
    if (_richTypes.contains(block.type)) {
      return MessageBlockList(
        messageId: message.id,
        blocks: [block],
        persona: personaForAgent(message.agentKey),
      );
    }

    return switch (block.type) {
      'suggestion' => _SuggestionCard(
        messageId: message.id,
        text: block.text ?? '',
      ),
      'choice' => _ChoiceCard(
        messageId: message.id,
        block: block,
        onSelectCustomer: onSelectCustomer,
      ),
      'attachment' => ThreadAttachmentCard(
        messageId: message.id,
        block: block,
        loadAttachment: loadAttachment,
        openAttachment: openAttachment,
      ),
      _ => _SummaryChip(
        messageId: message.id,
        type: block.type,
        summary: threadBlockSummary(block),
      ),
    };
  }
}

/// A draft an agent wrote for the associate to send, with a copy action.
///
/// This is Ava's `draft_response`: "here is a message you can send them, copy it?".
class _SuggestionCard extends StatelessWidget {
  const _SuggestionCard({required this.messageId, required this.text});

  final String messageId;
  final String text;

  Future<void> _copy(BuildContext context) async {
    await Clipboard.setData(ClipboardData(text: text));
    if (context.mounted) {
      AppToast.show(context, 'Draft copied');
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: ValueKey('thread_suggestion_$messageId'),
      constraints: BoxConstraints(
        maxWidth: MediaQuery.of(context).size.width * 0.78,
      ),
      padding: const EdgeInsets.fromLTRB(12, 10, 8, 6),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'DRAFT',
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.primary,
              letterSpacing: 1.1,
              fontSize: 10,
            ),
          ),
          const SizedBox(height: 4),
          Text(text, style: theme.textTheme.bodyMedium),
          Align(
            alignment: Alignment.centerRight,
            child: TextButton.icon(
              key: ValueKey('thread_suggestion_copy_$messageId'),
              onPressed: () => _copy(context),
              icon: const Icon(Icons.copy_rounded, size: 15),
              label: const Text('Copy'),
              style: TextButton.styleFrom(
                padding: const EdgeInsets.symmetric(horizontal: 8),
                minimumSize: const Size(0, 30),
                tapTargetSize: MaterialTapTargetSize.shrinkWrap,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// A customer-resolution question whose options bind the thread to a client.
class _ChoiceCard extends StatelessWidget {
  const _ChoiceCard({
    required this.messageId,
    required this.block,
    this.onSelectCustomer,
  });

  final String messageId;
  final ThreadBlock block;
  final void Function(ThreadBlock block, Map<String, dynamic> option)?
  onSelectCustomer;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;
    final options = block.options;

    return Container(
      key: ValueKey('thread_choice_$messageId'),
      constraints: BoxConstraints(
        maxWidth: MediaQuery.of(context).size.width * 0.78,
      ),
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            block.prompt ?? 'Which client did you mean?',
            style: theme.textTheme.bodyMedium,
          ),
          for (final option in options) ...[
            const SizedBox(height: 6),
            OutlinedButton(
              key: ValueKey(
                'thread_choice_${messageId}_${option['customerId']}',
              ),
              onPressed: onSelectCustomer == null
                  ? null
                  : () => onSelectCustomer!(block, option),
              style: OutlinedButton.styleFrom(
                minimumSize: const Size.fromHeight(34),
                padding: const EdgeInsets.symmetric(horizontal: 10),
                alignment: Alignment.centerLeft,
              ),
              child: Text(
                _optionLabel(option),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
              ),
            ),
          ],
        ],
      ),
    );
  }

  /// The words on an option: the client's name, and their state when the server named one.
  static String _optionLabel(Map<String, dynamic> option) {
    final name = option['fullName'];
    final status = option['status'];
    final label = name is String && name.isNotEmpty ? name : 'This client';
    return status is String && status.isNotEmpty && status != 'active'
        ? '$label · $status'
        : label;
  }
}

/// The one-line stand-in for a block the thread has no card for.
class _SummaryChip extends StatelessWidget {
  const _SummaryChip({
    required this.messageId,
    required this.type,
    required this.summary,
  });

  final String messageId;
  final String type;
  final String summary;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return Container(
      key: ValueKey('thread_block_${messageId}_$type'),
      constraints: BoxConstraints(
        maxWidth: MediaQuery.of(context).size.width * 0.78,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHigh,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Text(
        summary,
        style: theme.textTheme.labelMedium?.copyWith(
          color: scheme.onSurfaceVariant,
        ),
      ),
    );
  }
}


/// A file attached to a message: an image renders as a thumbnail that opens full screen, and
/// anything else as a document chip with its name and size.
///
/// Public because the Salon draws the same block from its own renderer: a Salon attachment and
/// a client-thread attachment are the same wire block and must look the same in both places.
/// The bytes are fetched through the authenticated client rather than an `<Image.network>` of
/// the stored route, which cannot carry the bearer token.
class ThreadAttachmentCard extends StatefulWidget {
  const ThreadAttachmentCard({
    super.key,
    required this.messageId,
    required this.block,
    this.loadAttachment,
    this.openAttachment,
  });

  final String messageId;
  final ThreadBlock block;
  final Future<Uint8List> Function(String attachmentId)? loadAttachment;
  final Future<void> Function(Uint8List bytes, String fileName)? openAttachment;

  @override
  State<ThreadAttachmentCard> createState() => _ThreadAttachmentCardState();
}

class _ThreadAttachmentCardState extends State<ThreadAttachmentCard> {
  Uint8List? _bytes;
  bool _loading = false;

  bool get _isImage => widget.block.isImage;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(covariant ThreadAttachmentCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.block.attachmentId != widget.block.attachmentId) {
      _bytes = null;
      _load();
    }
  }

  Future<void> _load() async {
    final id = widget.block.attachmentId;
    final loader = widget.loadAttachment;
    // A document's bytes are needed only to hand them to the platform viewer; without one the
    // chip stays a label.
    final needed = _isImage || widget.openAttachment != null;
    if (!needed || id == null || loader == null) {
      return;
    }

    setState(() => _loading = true);
    try {
      final bytes = await loader(id);
      if (!mounted) {
        return;
      }
      setState(() {
        _bytes = bytes;
        _loading = false;
      });
    } catch (_) {
      if (!mounted) {
        return;
      }
      // A thumbnail that will not load falls back to the one-line summary rather than to a
      // broken image glyph.
      setState(() => _loading = false);
    }
  }

  void _openFullScreen() {
    final bytes = _bytes;
    if (bytes == null) {
      return;
    }
    Navigator.of(context).push(
      MaterialPageRoute<void>(
        builder: (_) => _AttachmentViewer(
          bytes: bytes,
          title: widget.block.fileName ?? 'Attachment',
        ),
      ),
    );
  }

  /// Opens a document once its bytes have arrived.
  Future<void> _openDocument() async {
    final bytes = _bytes;
    final opener = widget.openAttachment;
    if (bytes == null || opener == null) {
      return;
    }
    await opener(bytes, widget.block.fileName ?? 'attachment');
  }

  @override
  Widget build(BuildContext context) {
    final block = widget.block;
    final key = ValueKey(
      'thread_attachment_${widget.messageId}_${block.attachmentId}',
    );

    if (!_isImage) {
      return _DocumentChip(
        key: key,
        block: block,
        onOpen: widget.openAttachment == null || _bytes == null
            ? null
            : _openDocument,
      );
    }

    final bytes = _bytes;
    if (bytes == null) {
      return Container(
        key: key,
        width: 180,
        height: _loading ? 120 : 44,
        decoration: BoxDecoration(
          color: Theme.of(context).colorScheme.surfaceContainerHigh,
          borderRadius: BorderRadius.circular(12),
        ),
        alignment: Alignment.center,
        child: _loading
            ? const SizedBox(
                width: 18,
                height: 18,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : Text(threadBlockSummary(block)),
      );
    }

    return GestureDetector(
      key: key,
      onTap: _openFullScreen,
      child: ClipRRect(
        borderRadius: BorderRadius.circular(12),
        child: Image.memory(
          bytes,
          width: 180,
          height: 180,
          fit: BoxFit.cover,
          // The bytes came from the authenticated route; a decode failure is a bad file, not a
          // network problem, so it falls back rather than retrying.
          errorBuilder: (context, error, stack) => SizedBox(
            width: 180,
            height: 44,
            child: Center(child: Text(threadBlockSummary(block))),
          ),
        ),
      ),
    );
  }
}

/// A non-image attachment: its name and size, which is what the platform viewer will open.
class _DocumentChip extends StatelessWidget {
  const _DocumentChip({super.key, required this.block, this.onOpen});

  final ThreadBlock block;

  /// Opens the bytes through the platform viewer. `null` leaves the chip a label.
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final scheme = theme.colorScheme;

    return InkWell(
      onTap: onOpen,
      borderRadius: BorderRadius.circular(10),
      child: Container(
      constraints: BoxConstraints(
        maxWidth: MediaQuery.of(context).size.width * 0.78,
      ),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 8),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHigh,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(Icons.description_outlined, size: 18, color: scheme.primary),
          const SizedBox(width: 8),
          Flexible(
            child: Text(
              block.fileName ?? 'Attachment',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.labelMedium,
            ),
          ),
          const SizedBox(width: 8),
          Text(
            humanFileSize(block.sizeBytes ?? 0),
            style: theme.textTheme.labelSmall?.copyWith(
              color: scheme.onSurfaceVariant,
            ),
          ),
          if (onOpen != null) ...[
            const SizedBox(width: 4),
            Icon(Icons.open_in_new_rounded, size: 14, color: scheme.primary),
          ],
        ],
      ),
      ),
    );
  }
}

/// The full-screen view of an image attachment.
class _AttachmentViewer extends StatelessWidget {
  const _AttachmentViewer({required this.bytes, required this.title});

  final Uint8List bytes;
  final String title;

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      key: const Key('thread_attachment_viewer'),
      backgroundColor: Colors.black,
      appBar: AppBar(
        backgroundColor: Colors.black,
        foregroundColor: Colors.white,
        title: Text(title, maxLines: 1, overflow: TextOverflow.ellipsis),
      ),
      body: Center(
        child: InteractiveViewer(
          minScale: 0.5,
          maxScale: 4,
          child: Image.memory(bytes),
        ),
      ),
    );
  }
}

/// A file size the way a person reads it: `2.4 MB`, `640 KB`.
String humanFileSize(int bytes) {
  if (bytes <= 0) {
    return '—';
  }
  if (bytes < 1024) {
    return '$bytes B';
  }
  if (bytes < 1024 * 1024) {
    return '${(bytes / 1024).round()} KB';
  }
  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}
