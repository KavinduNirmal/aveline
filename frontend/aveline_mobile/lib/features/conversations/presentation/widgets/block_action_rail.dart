import 'package:flutter/material.dart';

import '../../../../shared/widgets/app_toast.dart';
import '../../domain/block_actions.dart';
import '../../domain/thread_message.dart';
import 'bubble_tone.dart';

/// What the rail calls, and what it must know about the thread it is drawn in.
///
/// Threaded from the screen that owns the thread rather than read from an inherited
/// widget, so every renderer stays drawable on its own — a preview, a test, the docs
/// gallery — and simply gets no rail. A handler that is absent is not a dead segment:
/// the environment still says whether the thread had a destination, and the reason is
/// drawn either way.
class BlockActionBridge {
  const BlockActionBridge({
    required this.hasCustomerDestination,
    required this.hasForwardDestination,
    required this.agentBusy,
    required this.pendingAction,
    required this.onAction,
  });

  /// Whether the open thread is bound to a client, so "send to customer" has somewhere
  /// to go.
  final bool hasCustomerDestination;

  /// Whether at least one other client thread can receive a forward.
  final bool hasForwardDestination;

  /// Whether the agent is already producing a reply for this thread.
  final bool agentBusy;

  /// The action in flight for one block, or `null`.
  final BlockActionId? Function(String messageId, int blockIndex) pendingAction;

  /// Runs an action against the block it was drawn on.
  final void Function(
    ThreadBlock block,
    String messageId,
    int blockIndex,
    BlockActionId action,
  )
  onAction;

  /// The environment one block's rail resolves against.
  BlockActionEnvironment environmentFor(String messageId, int blockIndex) =>
      BlockActionEnvironment(
        hasCustomerDestination: hasCustomerDestination,
        hasForwardDestination: hasForwardDestination,
        agentBusy: agentBusy,
        pending: pendingAction(messageId, blockIndex),
      );
}

/// The action rail that closes an AI content block.
///
/// It is the card's **bottom edge**, not a row of buttons floating under it. The
/// segments are joined — a single hairline between neighbours, no rounding of their own
/// — and the parent card's clipping decoration shapes the rail's bottom corners, so the
/// whole thing reads as the card's footer. Nothing here uses the app's shared button
/// styles, whose pill radius is exactly the floating-chip look this rail is not.
///
/// A disabled segment stays **focusable**: the reason it is unavailable is its tooltip
/// and its semantic hint, and a segment removed from the tab order would leave a
/// keyboard user with a greyed-out word and no explanation. Pressing a disabled segment
/// says the reason out loud as a toast, so the explanation is reachable by touch too.
class BlockActionRail extends StatelessWidget {
  const BlockActionRail({
    super.key,
    required this.block,
    required this.messageId,
    required this.blockIndex,
    required this.bridge,
    this.compact = false,
    this.tone = BubbleTone.other,
  });

  final ThreadBlock block;

  /// The message the block belongs to.
  final String messageId;

  /// The block's position in the message, which is what makes the pending state
  /// per-block rather than per-message: a message can carry several pieces.
  final int blockIndex;

  final BlockActionBridge bridge;

  /// True inside a tile row, where the card is around eleven rems wide and its segments
  /// share that. The printed word is dropped and the glyph carries the segment; the
  /// tooltip and the accessible name never change.
  final bool compact;

  /// The bubble the card sits in, which fixes the rail's ink.
  final BubbleTone tone;

  @override
  Widget build(BuildContext context) {
    final actions = resolveBlockActions(block, bridge.environmentFor(messageId, blockIndex));
    if (actions.isEmpty) {
      return const SizedBox.shrink();
    }

    final scheme = Theme.of(context).colorScheme;
    final own = tone == BubbleTone.own;
    // The staff bubble fills with `primary`, so a rail painted for the neutral agent
    // bubble reads as a pale band punched across the message. Each surface states its own
    // ink, the same way the attachment card does.
    final divider = own
        ? scheme.onPrimary.withValues(alpha: 0.20)
        : scheme.outlineVariant;
    final fill = own
        ? scheme.onPrimary.withValues(alpha: 0.06)
        : scheme.surfaceContainerHigh.withValues(alpha: 0.35);
    final ink = own
        ? scheme.onPrimary.withValues(alpha: 0.85)
        : scheme.onSurfaceVariant;

    return Semantics(
      container: true,
      // The rail is one control group; every segment is its own node inside it.
      child: Container(
        key: ValueKey('block_action_rail_${messageId}_$blockIndex'),
        // A definite height, because the rail is also measured by the tile grid's
        // `IntrinsicHeight`: a stretched row with no height of its own is an infinite
        // constraint there, and the height is what makes every rail the same band.
        height: railHeight,
        decoration: BoxDecoration(
          color: fill,
          // The single divider between the card's body and its rail. The hairlines
          // *between* segments are the boxes below, so no neighbour is doubled.
          border: Border(top: BorderSide(color: divider)),
        ),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            for (var i = 0; i < actions.length; i += 1) ...[
              if (i > 0) SizedBox(width: 1, child: ColoredBox(color: divider)),
              Expanded(
                child: _Segment(
                  key: ValueKey(
                    'block_action_${actions[i].id.name}_${messageId}_$blockIndex',
                  ),
                  action: actions[i],
                  title: blockTitle(block),
                  compact: compact,
                  ink: ink,
                  focusInk: own ? scheme.onPrimary : scheme.primary,
                  onPressed: () => _invoke(context, actions[i]),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  /// The band's height. Tall enough for a fourteen-pixel glyph and an eleven-pixel
  /// word, and fixed so every rail reads the same under a card.
  static const double railHeight = 32;

  /// Runs a live action, or says why the block cannot run it.
  ///
  /// A busy segment swallows the press outright: the action is already running, and a
  /// second would race the first.
  void _invoke(BuildContext context, ResolvedBlockAction action) {
    if (action.busy) {
      return;
    }
    if (!action.enabled) {
      AppToast.show(
        context,
        action.reason ?? '${action.label} is not available here.',
      );
      return;
    }
    bridge.onAction(block, messageId, blockIndex, action.id);
  }
}

/// One joined segment of the rail.
class _Segment extends StatefulWidget {
  const _Segment({
    super.key,
    required this.action,
    required this.title,
    required this.compact,
    required this.ink,
    required this.focusInk,
    required this.onPressed,
  });

  final ResolvedBlockAction action;

  /// The card this rail closes, so the accessible name is qualified by it.
  final String title;

  final bool compact;
  final Color ink;
  final Color focusInk;
  final VoidCallback onPressed;

  @override
  State<_Segment> createState() => _SegmentState();
}

class _SegmentState extends State<_Segment> {
  /// True while this segment holds the keyboard focus, so it can ring itself.
  bool _focused = false;

  @override
  Widget build(BuildContext context) {
    final action = widget.action;
    final scheme = Theme.of(context).colorScheme;
    final word = action.busy ? blockActionBusyLabels[action.id]! : action.label;
    final tooltip = action.enabled || action.busy
        ? word
        : (action.reason ?? '${action.label} is not available here.');
    // The printed word shortens on a narrow rail; the accessible name never does.
    final printed = widget.compact
        ? null
        : (action.busy ? word : action.shortLabel);

    return Tooltip(
      message: tooltip,
      // The label below is the one semantics should read; a tooltip's own copy would
      // otherwise be spoken twice.
      excludeFromSemantics: true,
      child: Semantics(
        button: true,
        enabled: action.enabled,
        label: word,
        hint: action.reason,
        // The icon and the printed word are decoration beside the label above.
        excludeSemantics: true,
        child: InkWell(
          // Focusable even when unavailable, so its explanation is reachable from the
          // keyboard rather than removed with the segment.
          canRequestFocus: true,
          onTap: widget.onPressed,
          onFocusChange: (focused) => setState(() => _focused = focused),
          focusColor: widget.focusInk.withValues(alpha: 0.16),
          hoverColor: scheme.onSurface.withValues(alpha: 0.06),
          child: Container(
            // An inset ring, so a focused segment does not spill a halo outside the card.
            decoration: _focused
                ? BoxDecoration(
                    border: Border.all(color: widget.focusInk, width: 1.5),
                  )
                : null,
            padding: const EdgeInsets.symmetric(horizontal: 4, vertical: 7),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.center,
              mainAxisSize: MainAxisSize.min,
              children: [
                if (action.busy)
                  SizedBox(
                    width: 12,
                    height: 12,
                    child: CircularProgressIndicator(
                      strokeWidth: 1.6,
                      color: widget.ink,
                    ),
                  )
                else
                  Icon(
                    _iconFor(action.id),
                    size: 14,
                    color: action.enabled
                        ? widget.ink
                        : widget.ink.withValues(alpha: 0.45),
                  ),
                if (printed != null) ...[
                  const SizedBox(width: 5),
                  Flexible(
                    child: Text(
                      printed,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        fontSize: 11,
                        fontWeight: FontWeight.w500,
                        color: action.enabled || action.busy
                            ? widget.ink
                            : widget.ink.withValues(alpha: 0.45),
                      ),
                    ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ),
    );
  }

  /// The glyph each action wears.
  static IconData _iconFor(BlockActionId id) => switch (id) {
    BlockActionId.copy => Icons.copy_rounded,
    BlockActionId.forward => Icons.forward_rounded,
    BlockActionId.sendToCustomer => Icons.send_rounded,
    BlockActionId.regenerate => Icons.refresh_rounded,
  };
}
