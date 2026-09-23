import 'dart:typed_data';

import 'package:flutter/material.dart';

import '../../../../shared/persona.dart';
import '../../../../shared/utils/currency_formatter.dart';
import '../../domain/block_actions.dart';
import '../../domain/thread_message.dart';
import 'block_action_rail.dart';
import 'client_channel_surface.dart';
import 'thread_blocks.dart';
import '../../domain/tile_blocks.dart';
import 'bubble_tone.dart';
import 'mention_text.dart';

/// Renders a Salon message's ordered content blocks, mirroring the web
/// `components/conversation/blocks.tsx`.
///
/// This is what makes the updated Salon possible: a message is not a string but a
/// list of typed blocks, and the agent's answer only reads as a curated set when a
/// run of `piece` blocks is laid out as a row of tiles rather than stacked as a wall
/// of prose.
///
/// The switch is deliberately open in one direction only: a block type this build
/// has not been taught renders its one-line summary rather than vanishing, so a
/// server that learns a new block cannot make a message go blank.
class MessageBlockList extends StatelessWidget {
  const MessageBlockList({
    super.key,
    required this.messageId,
    required this.blocks,
    this.persona,
    this.tone = BubbleTone.other,
    this.onSelectCustomer,
    this.onSignOff,
    this.loadAttachment,
    this.openAttachment,
    this.bridge,
    this.blockIndices,
  });

  /// The message the blocks belong to. It keys the interactive parts, so a
  /// rebuilt list does not re-fire a decision or re-fetch a thumbnail.
  final String messageId;

  /// The blocks, in the order the server sent them.
  final List<ThreadBlock> blocks;

  /// The persona whose accent tints Elle's styling note and Ava's suggestion.
  final Persona? persona;

  /// The bubble the blocks sit in, which fixes the inherited ink.
  final BubbleTone tone;

  /// Called when the associate picks a customer from a resolution `choice` block.
  final ValueChanged<String>? onSelectCustomer;

  /// Called with the decision when the associate releases or drops a `sign_off`.
  final ValueChanged<bool>? onSignOff;

  /// Fetches an attachment's bytes through the authenticated client. `null` leaves
  /// an image as the block's one-line summary, which is what a preview wants.
  final Future<Uint8List> Function(String attachmentId)? loadAttachment;

  /// Opens a document through the platform viewer. `null` leaves the chip a label.
  final Future<void> Function(Uint8List bytes, String fileName)? openAttachment;

  /// The action rail's wiring for the thread this list is drawn in. `null` draws no
  /// rail at all, which is what a preview or a renderer with no thread behind it wants.
  final BlockActionBridge? bridge;

  /// Each block's position in the message, parallel to [blocks].
  ///
  /// A message is rendered in groups — a run of tiles here, a lone card there — so the
  /// position *within this list* is not the block's position in the message. The caller
  /// that holds the whole message hands the real indices down; without them the rail
  /// falls back to the position here, which is correct wherever a list is a message's
  /// whole body.
  final List<int>? blockIndices;

  @override
  Widget build(BuildContext context) {
    final parsed = withoutBorrowedLookImages(blocks);
    if (parsed.isEmpty) {
      return const SizedBox.shrink();
    }

    final groups = groupTileBlocks(parsed);
    final children = <Widget>[];
    // `withoutBorrowedLookImages` and `groupTileBlocks` both preserve order and length,
    // so a cursor over the groups reconstructs each block's position exactly.
    var cursor = 0;
    for (var i = 0; i < groups.length; i += 1) {
      final group = groups[i];
      final indices = [
        for (var j = 0; j < _spanOf(group); j += 1) _indexAt(cursor + j),
      ];
      cursor += indices.length;
      children.add(
        _grouped(
          context,
          index: i,
          child: switch (group) {
            TileRun(:final blocks) => _TileGrid(
              blocks: blocks,
              indices: indices,
              render: _render,
            ),
            LoneBlock(:final block) => _render(context, block, indices.first),
          },
        ),
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: children,
    );
  }

  /// How many blocks a group consumed.
  static int _spanOf(SalonBlockGroup group) => switch (group) {
    TileRun(:final blocks) => blocks.length,
    LoneBlock() => 1,
  };

  /// The message-level index of the block at [position] in this list.
  int _indexAt(int position) {
    final indices = blockIndices;
    if (indices != null && position < indices.length) {
      return indices[position];
    }
    return position;
  }

  /// Separates two groups.
  ///
  /// Web puts a hairline at the bubble's full width between groups. A Flutter
  /// divider expands to the widest constraint it is given, which would force every
  /// multi-block bubble to the bubble's full cap; so the rule is carried by the
  /// group's own top border instead and therefore tracks that group's width. The
  /// spacing is web's own: an 8px stack gap, then 8px, a hairline, and 8px.
  Widget _grouped(
    BuildContext context, {
    required int index,
    required Widget child,
  }) {
    if (index == 0) {
      return child;
    }
    return Container(
      margin: const EdgeInsets.only(top: 8),
      padding: const EdgeInsets.only(top: 16),
      decoration: BoxDecoration(
        border: Border(
          top: BorderSide(color: Theme.of(context).colorScheme.outlineVariant),
        ),
      ),
      child: child,
    );
  }

  Widget _render(
    BuildContext context,
    ThreadBlock block,
    int blockIndex, [
    double? tileWidth,
  ]) {
    if (block.type == 'piece') {
      return _PieceTile(
        messageId: messageId,
        block: block,
        blockIndex: blockIndex,
        bridge: bridge,
        width: tileWidth,
      );
    }
    if (block.type == 'look') {
      return _LookBlock(
        messageId: messageId,
        block: block,
        blockIndex: blockIndex,
        bridge: bridge,
        persona: persona,
        tone: tone,
        width: tileWidth,
      );
    }
    if (block.type == 'client_message') {
      // The bubble routes a customer's message to the message surface before it ever
      // reaches this list; this branch only guards a hand-built mixed message.
      return ClientChannelSurface(
        text: block.text ?? '',
        handle: block.from,
      );
    }
    if (block.type == 'suggestion') {
      return _SuggestionBlock(
        messageId: messageId,
        block: block,
        blockIndex: blockIndex,
        bridge: bridge,
        persona: persona,
        tone: tone,
      );
    }
    if (block.type == 'at_a_glance') {
      return _AtAGlanceBlock(block: block);
    }
    if (block.type == 'sign_off') {
      return _SignOffBlock(
        messageId: messageId,
        block: block,
        onSignOff: onSignOff,
      );
    }
    if (block.type == 'payment') {
      return _PaymentBlock(block: block);
    }
    if (block.type == 'courier') {
      return _CourierBlock(block: block);
    }
    if (block.type == 'choice') {
      return _ChoiceBlock(
        messageId: messageId,
        block: block,
        onSelectCustomer: onSelectCustomer,
      );
    }
    if (block.type == 'attachment') {
      return ThreadAttachmentCard(
        messageId: messageId,
        block: block,
        loadAttachment: loadAttachment,
        openAttachment: openAttachment,
      );
    }
    if (block.type == 'text') {
      return _TextBlock(block: block, tone: tone);
    }
    return _SummaryChip(block: block);
  }
}

// ---------------------------------------------------------------------------
// Shared typography
// ---------------------------------------------------------------------------

/// Body text in the app's sans face, at the web Salon's `text-sm` size.
///
/// A chat surface carries more words per screen than any other part of the app, and
/// the theme's 16px body made a thread read as documents rather than as conversation.
/// Web sets its Salon prose at 14px and that is what is used here; the app's larger
/// body sizes stay for the forms and panels they suit.
TextStyle _sans(
  BuildContext context, {
  double? size = 14,
  double height = 1.55,
  FontWeight weight = FontWeight.w400,
  Color? color,
  double? letterSpacing,
  bool tabular = false,
}) {
  return (Theme.of(context).textTheme.bodyMedium ?? const TextStyle()).copyWith(
    fontSize: size,
    height: height,
    fontWeight: weight,
    color: color,
    letterSpacing: letterSpacing,
    fontFeatures: tabular ? const [FontFeature.tabularFigures()] : null,
  );
}

/// The boutique's serif, used sparingly for a piece's name.
TextStyle _serif({
  double size = 12,
  double height = 1.375,
  FontWeight weight = FontWeight.w500,
  Color? color,
}) {
  return TextStyle(
    fontFamily: 'Playfair Display',
    fontSize: size,
    height: height,
    fontWeight: weight,
    color: color,
  );
}

/// The tinted surface an editorial note wears: Elle's gold, Ava's rose, Lina's
/// wine, and the app's accent when the note is not attributed to an agent.
({Color border, Color background, Color ink}) _personaSurface(
  BuildContext context,
  Persona? persona,
) {
  final scheme = Theme.of(context).colorScheme;
  final accent = persona?.accent ?? scheme.primary;
  return (
    border: accent.withValues(alpha: 0.20),
    background: accent.withValues(alpha: 0.05),
    ink: accent,
  );
}

// ---------------------------------------------------------------------------
// Product tiles
// ---------------------------------------------------------------------------

/// The row a run of tiles sits in.
///
/// Web uses `grid-cols-[repeat(auto-fit,minmax(min(100%,11rem),1fr))]`: as many
/// 11rem tracks as fit, collapsing empty tracks so a short run fills the row. A
/// `GridView` cannot express that, because CSS grid sizes each row to its own
/// tallest item while a Flutter grid needs one `childAspectRatio` for every row.
/// Columns are therefore counted here and the tiles are chunked into rows, each row
/// as tall as its tallest card, which is what the web actually renders.
///
/// A run of one is the exception: it stays shrink-to-fit beside its own 16rem cap,
/// so a single piece is a card and not a band.
class _TileGrid extends StatelessWidget {
  const _TileGrid({
    required this.blocks,
    required this.indices,
    required this.render,
  });

  final List<ThreadBlock> blocks;

  /// Each block's position in the message, parallel to [blocks].
  final List<int> indices;

  /// Draws one tile. It is handed the width the grid resolved for it, because a
  /// tile's plate is a fixed 4:3 box: `Image` reports no intrinsic size, so a row
  /// measured by `IntrinsicHeight` would collapse to its text if the plate's height
  /// were left to the image.
  final Widget Function(
    BuildContext context,
    ThreadBlock block,
    int blockIndex,
    double width,
  )
  render;

  /// Web's `11rem` — the track a tile is drawn at wherever it genuinely fits.
  static const double _tilePreferred = 176;

  /// `6rem` — the narrowest a tile ever gets before a card stops being readable.
  static const double _tileFloor = 96;

  /// `gap-2`.
  static const double _gap = 8;

  /// `max-w-64` — the cap a lone tile keeps.
  static const double _singleCap = 256;

  /// The ratio of a tile's plate: `aspect-4/3`.
  static const double plateRatio = 4 / 3;

  /// The narrowest a tile may get before the row drops a column.
  ///
  /// `11rem` is a wide drawer's track. A phone's bubble is around 256px, where two of
  /// them do not fit — so a curated set collapsed to a single column, which is exactly
  /// the stacking the row exists to prevent. The track therefore shrinks to whatever
  /// fits two across, and never below [_tileFloor]. It stays `11rem` wherever `11rem`
  /// genuinely fits, so a wide thread still lays out the way the web does.
  double _trackFor(double width, int count) {
    if (count < 2) {
      return _tilePreferred;
    }
    final twoAcross = (width - _gap) / 2;
    final track = twoAcross < _tilePreferred ? twoAcross : _tilePreferred;
    return track < _tileFloor ? _tileFloor : track;
  }

  @override
  Widget build(BuildContext context) {
    return LayoutBuilder(
      builder: (context, constraints) {
        final maxWidth = constraints.maxWidth.isFinite
            ? constraints.maxWidth
            : _tilePreferred;
        final width = blocks.length == 1
            ? (maxWidth < _singleCap ? maxWidth : _singleCap)
            : maxWidth;
        final track = _trackFor(width, blocks.length);
        final columns = width <= track
            ? 1
            : ((width + _gap) / (track + _gap))
                  .floor()
                  .clamp(1, blocks.length)
                  .toInt();
        // `1fr` for every track, whether or not the last row fills it: a short row
        // keeps the row's track width and leaves the rest of the line empty, which is
        // what stops a last lone card from doubling in width.
        final tileWidth = (width - _gap * (columns - 1)) / columns;

        // The rows carry each block's message-level index with it, so the rail a tile
        // draws is keyed to the block rather than to its position in this row.
        final entries = [
          for (var i = 0; i < blocks.length; i += 1)
            (index: indices[i], block: blocks[i]),
        ];
        final rows = <List<({int index, ThreadBlock block})>>[];
        for (var i = 0; i < entries.length; i += columns) {
          rows.add(
            entries.sublist(
              i,
              (i + columns) > entries.length ? entries.length : i + columns,
            ),
          );
        }

        return SizedBox(
          width: width,
          child: Column(
            key: const Key('message_tile_grid'),
            mainAxisSize: MainAxisSize.min,
            children: [
              for (var r = 0; r < rows.length; r += 1) ...[
                if (r > 0) const SizedBox(height: _gap),
                // Stretch is what makes a row's cards share its height, so a row's
                // prices sit on one line even when one name wraps and another does not.
                IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      for (var c = 0; c < rows[r].length; c += 1) ...[
                        if (c > 0) const SizedBox(width: _gap),
                        SizedBox(
                          width: tileWidth,
                          child: render(
                            context,
                            rows[r][c].block,
                            rows[r][c].index,
                            tileWidth,
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ],
            ],
          ),
        );
      },
    );
  }
}

/// One catalogue piece as a tile, not a panel.
///
/// An answer routinely carries several pieces, and a full-width card turns each one
/// into a band that pushes the next off-screen, so a curated set reads as a single
/// recommendation. The tile is deliberately narrow: a 4:3 plate, the name, size and
/// stock, then the money.
class _PieceTile extends StatelessWidget {
  const _PieceTile({
    required this.messageId,
    required this.block,
    required this.blockIndex,
    required this.bridge,
    required this.width,
  });

  final String messageId;
  final ThreadBlock block;
  final int blockIndex;
  final BlockActionBridge? bridge;

  /// The track the grid resolved for this tile, which the plate is measured from.
  final double? width;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final name = block.name ?? 'Piece';
    final price = block.amount;
    final tileWidth = width ?? _TileGrid._singleCap;
    final rail = _rail(
      messageId: messageId,
      block: block,
      blockIndex: blockIndex,
      bridge: bridge,
      // A tile is around eleven rems wide; the word is dropped and the glyph carries
      // the segment, while the tooltip and the accessible name stay whole.
      compact: true,
    );

    return Container(
      key: ValueKey('message_piece_${block.name}'),
      // The card's clipping decoration shapes the rail's bottom corners: card and rail
      // are one box, so the rail reads as the tile's footer rather than a floating row.
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: scheme.outlineVariant.withValues(alpha: 0.7),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.max,
        children: [
          _TilePlate(
            imageUrl: block.imageUrl,
            alt: name,
            width: tileWidth,
          ),
          Expanded(
            child: Padding(
              padding: const EdgeInsets.all(10),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    name,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: _serif(color: scheme.onSurface),
                  ),
                  if (block.size != null || block.stock != null) ...[
                    const SizedBox(height: 4),
                    Wrap(
                      spacing: 6,
                      runSpacing: 4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: [
                        if (block.size != null)
                          Text(
                            'Size ${block.size}',
                            style: _sans(
                              context,
                              size: 10,
                              height: 1.4,
                              color: scheme.onSurfaceVariant,
                            ),
                          ),
                        if (block.stock != null) _StockBadge(stock: block.stock!),
                      ],
                    ),
                  ],
                  // The money sits on the floor of the tile, so a row's prices line
                  // up even when one name wraps to a second line and the others do not.
                  if (price != null) ...[
                    const Spacer(),
                    Padding(
                      padding: const EdgeInsets.only(top: 4),
                      child: Text(
                        rupees(price),
                        style: _sans(
                          context,
                          size: 12,
                          height: 1.4,
                          weight: FontWeight.w600,
                          color: scheme.primary,
                          tabular: true,
                        ),
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
          ?rail,
        ],
      ),
    );
  }
}

/// A tile's 4:3 plate, sized from the track it sits in.
///
/// A missing photograph is a state, not a broken image or a stretched blank. The
/// plate is a definite box rather than an `AspectRatio` because `Image` reports no
/// intrinsic size, and the row that gives every card one height is measured by
/// `IntrinsicHeight`.
class _TilePlate extends StatelessWidget {
  const _TilePlate({
    required this.imageUrl,
    required this.alt,
    required this.width,
  });

  final String? imageUrl;
  final String alt;
  final double width;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final url = imageUrl;
    final height = width / _TileGrid.plateRatio;

    if (url == null || url.isEmpty) {
      return Container(
        key: const Key('message_tile_no_photograph'),
        width: width,
        height: height,
        color: scheme.surfaceContainerHigh,
        alignment: Alignment.center,
        child: Text(
          'No photograph',
          style: _sans(
            context,
            size: 10,
            height: 1.4,
            color: scheme.onSurfaceVariant,
          ),
        ),
      );
    }

    Widget placeholder() =>
        Container(color: scheme.surfaceContainerHigh);

    return SizedBox(
      width: width,
      height: height,
      child: Image.network(
        url,
        key: const Key('message_tile_photograph'),
        fit: BoxFit.cover,
        semanticLabel: alt,
        // The plate holds its shape while the photograph arrives, so the bubble does
        // not jump from a text row to a band.
        frameBuilder: (context, child, frame, wasSynchronouslyLoaded) =>
            frame != null || wasSynchronouslyLoaded ? child : placeholder(),
        errorBuilder: (context, error, stack) => placeholder(),
      ),
    );
  }
}

/// The stock figure, as a quiet secondary badge.
class _StockBadge extends StatelessWidget {
  const _StockBadge({required this.stock});

  final int stock;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHigh,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Text(
        '$stock in stock',
        style: _sans(
          context,
          size: 10,
          height: 1.4,
          color: scheme.onSurfaceVariant,
          tabular: true,
        ),
      ),
    );
  }
}

/// Elle's curated look.
///
/// A look is a pairing and a rationale, and the boutique has no photograph of it —
/// only of each piece. With a picture of its own it is a tile like a piece; without
/// one it is the styling note about the row, labelled with the look's name and read
/// at the row's full width. It is deliberately never given a blank plate to fill.
class _LookBlock extends StatelessWidget {
  const _LookBlock({
    required this.messageId,
    required this.block,
    required this.blockIndex,
    required this.bridge,
    required this.persona,
    required this.tone,
    required this.width,
  });

  final String messageId;
  final ThreadBlock block;
  final int blockIndex;
  final BlockActionBridge? bridge;
  final Persona? persona;
  final BubbleTone tone;

  /// The track the grid resolved, when this look is a tile. `null` when it is the
  /// styling note beside the row.
  final double? width;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final surface = _personaSurface(context, persona);
    final text = block.text;
    // A look with its own plate is a narrow tile like a piece; one without is the
    // styling note at the row's full width, where the printed word has room.
    final asTile = block.imageUrl != null && block.imageUrl!.isNotEmpty;
    final rail = _rail(
      messageId: messageId,
      block: block,
      blockIndex: blockIndex,
      bridge: bridge,
      compact: asTile,
      tone: tone,
    );

    if (!asTile) {
      return Container(
        key: const Key('message_look_note'),
        // Card and rail are one clipped box, so the rail's bottom corners follow the
        // card's radius.
        clipBehavior: Clip.antiAlias,
        decoration: BoxDecoration(
          color: surface.background,
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: surface.border),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Padding(
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    block.name ?? 'Look',
                    style: _sans(
                      context,
                      size: 12,
                      height: 1.4,
                      weight: FontWeight.w500,
                      color: surface.ink,
                    ),
                  ),
                  if (text != null) ...[
                    const SizedBox(height: 4),
                    MentionText(
                      text: text,
                      tone: tone,
                      style: _sans(context, color: _inkFor(scheme, tone)),
                    ),
                  ],
                ],
              ),
            ),
            ?rail,
          ],
        ),
      );
    }

    return Container(
      key: const Key('message_look_tile'),
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: scheme.surfaceContainerLowest,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(
          color: scheme.outlineVariant.withValues(alpha: 0.7),
        ),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          _TilePlate(
            imageUrl: block.imageUrl,
            alt: block.name ?? 'Look',
            width: width ?? _TileGrid._singleCap,
          ),
          if (text != null)
            // The padding sits on a wrapper, not on the clamped paragraph:
            // `overflow: hidden` clips at the padding box, so padding below a line
            // clamp lets the next line show through it.
            Padding(
              padding: const EdgeInsets.all(10),
              child: Text(
                text,
                maxLines: 4,
                overflow: TextOverflow.ellipsis,
                style: _sans(
                  context,
                  size: 11,
                  height: 1.5,
                  color: scheme.onSurfaceVariant,
                ),
              ),
            ),
          ?rail,
        ],
      ),
    );
  }
}

// ---------------------------------------------------------------------------
// The customer's channel message
// ---------------------------------------------------------------------------

// ---------------------------------------------------------------------------
// The remaining block vocabulary
// ---------------------------------------------------------------------------

/// A message's own words, with its entity mentions lifted into pills.
class _TextBlock extends StatelessWidget {
  const _TextBlock({required this.block, required this.tone});

  final ThreadBlock block;
  final BubbleTone tone;

  @override
  Widget build(BuildContext context) {
    return MentionText(
      text: block.text ?? '',
      tone: tone,
      style: _sans(context, color: _inkFor(Theme.of(context).colorScheme, tone)),
    );
  }
}

/// A note an agent wrote for the associate, on the persona's own tint.
class _SuggestionBlock extends StatelessWidget {
  const _SuggestionBlock({
    required this.messageId,
    required this.block,
    required this.blockIndex,
    required this.bridge,
    required this.persona,
    required this.tone,
  });

  final String messageId;
  final ThreadBlock block;
  final int blockIndex;
  final BlockActionBridge? bridge;
  final Persona? persona;
  final BubbleTone tone;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final surface = _personaSurface(context, persona);
    final text = block.text;
    final rail = _rail(
      messageId: messageId,
      block: block,
      blockIndex: blockIndex,
      bridge: bridge,
      compact: false,
      tone: tone,
    );

    return Container(
      key: const Key('message_suggestion'),
      // Card and rail are one clipped box, so the rail's bottom corners follow the
      // card's radius.
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: surface.background,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: surface.border),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Padding(
            padding: const EdgeInsets.all(12),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  'Suggestion',
                  style: _sans(
                    context,
                    size: 12,
                    height: 1.4,
                    weight: FontWeight.w500,
                    color: surface.ink,
                  ),
                ),
                if (text != null) ...[
                  const SizedBox(height: 4),
                  MentionText(
                    text: text,
                    tone: tone,
                    style: _sans(context, color: _inkFor(scheme, tone)),
                  ),
                ],
              ],
            ),
          ),
          ?rail,
        ],
      ),
    );
  }
}

/// The action rail for a block, or `null` when the renderer has no thread behind it.
///
/// A renderer with no bridge — a preview, a test, the docs gallery — draws the card it
/// always drew rather than an inert row of segments.
Widget? _rail({
  required String messageId,
  required ThreadBlock block,
  required int blockIndex,
  required BlockActionBridge? bridge,
  required bool compact,
  BubbleTone tone = BubbleTone.other,
}) {
  if (bridge == null || !hasBlockActions(block)) {
    return null;
  }
  return BlockActionRail(
    block: block,
    messageId: messageId,
    blockIndex: blockIndex,
    bridge: bridge,
    compact: compact,
    tone: tone,
  );
}

/// The `at_a_glance` briefing table.
class _AtAGlanceBlock extends StatelessWidget {
  const _AtAGlanceBlock({required this.block});

  final ThreadBlock block;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final columns = block.columns;
    final rows = block.rows;
    if (columns.isEmpty) {
      return const SizedBox.shrink();
    }

    // A row that carries fewer cells than the header still has to lay out, so it is
    // padded to the header's width rather than throwing at build time.
    List<Widget> cells(List<String> values, TextStyle style) => [
      for (var i = 0; i < columns.length; i += 1)
        Padding(
          padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
          child: Text(i < values.length ? values[i] : '', style: style),
        ),
    ];

    return Container(
      key: const Key('message_at_a_glance'),
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Table(
        defaultColumnWidth: const FlexColumnWidth(),
        border: TableBorder(
          horizontalInside: BorderSide(color: scheme.outlineVariant),
        ),
        children: [
          TableRow(
            decoration: BoxDecoration(color: scheme.surfaceContainerHigh),
            children: cells(
              columns,
              _sans(
                context,
                size: 12,
                height: 1.4,
                weight: FontWeight.w500,
                color: scheme.onSurfaceVariant,
              ),
            ),
          ),
          for (final row in rows)
            TableRow(
              children: cells(
                row,
                _sans(context, size: 12, height: 1.4, color: scheme.onSurface),
              ),
            ),
        ],
      ),
    );
  }
}

/// A staged decision the associate has to make.
class _SignOffBlock extends StatelessWidget {
  const _SignOffBlock({
    required this.messageId,
    required this.block,
    this.onSignOff,
  });

  final String messageId;
  final ThreadBlock block;
  final ValueChanged<bool>? onSignOff;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final reason = block.reason;
    final amount = block.amount;

    return Container(
      key: ValueKey('message_sign_off_$messageId'),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.05),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.20)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'Approval needed',
            style: _sans(
              context,
              size: 14,
              height: 1.4,
              weight: FontWeight.w600,
              color: scheme.onSurface,
            ),
          ),
          if (reason != null) ...[
            const SizedBox(height: 2),
            Text(
              reason,
              style: _sans(
                context,
                size: 12,
                height: 1.4,
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
          if (amount != null) ...[
            const SizedBox(height: 8),
            Text(
              rupees(amount),
              style: _sans(
                context,
                size: 14,
                height: 1.4,
                weight: FontWeight.w600,
                color: scheme.primary,
                tabular: true,
              ),
            ),
          ],
          if (onSignOff != null) ...[
            const SizedBox(height: 10),
            Row(
              children: [
                FilledButton(
                  key: ValueKey('message_sign_off_approve_$messageId'),
                  onPressed: () => onSignOff!(true),
                  child: const Text('Approve'),
                ),
                const SizedBox(width: 8),
                OutlinedButton(
                  key: ValueKey('message_sign_off_reject_$messageId'),
                  onPressed: () => onSignOff!(false),
                  child: const Text('Reject'),
                ),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

/// A payment the commerce agent is tracking.
class _PaymentBlock extends StatelessWidget {
  const _PaymentBlock({required this.block});

  final ThreadBlock block;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final amount = block.amount;
    final status = block.status;

    return Container(
      key: const Key('message_payment'),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.primary.withValues(alpha: 0.05),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: scheme.primary.withValues(alpha: 0.20)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'Payment',
            style: _sans(
              context,
              size: 12,
              height: 1.4,
              weight: FontWeight.w500,
              color: scheme.primary,
            ),
          ),
          if (amount != null) ...[
            const SizedBox(height: 4),
            Text(
              rupees(amount),
              style: _sans(
                context,
                size: 14,
                height: 1.4,
                weight: FontWeight.w600,
                color: scheme.onSurface,
                tabular: true,
              ),
            ),
          ],
          if (status != null) ...[
            const SizedBox(height: 6),
            Text(
              status,
              style: _sans(
                context,
                size: 12,
                height: 1.4,
                color: scheme.onSurfaceVariant,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// A delivery the commerce agent is tracking.
class _CourierBlock extends StatelessWidget {
  const _CourierBlock({required this.block});

  final ThreadBlock block;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final status = block.status;

    return Container(
      key: const Key('message_courier'),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHigh.withValues(alpha: 0.4),
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: scheme.outlineVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: [
          Text(
            'Delivery',
            style: _sans(
              context,
              size: 12,
              height: 1.4,
              weight: FontWeight.w500,
              color: scheme.onSurfaceVariant,
            ),
          ),
          if (status != null) ...[
            const SizedBox(height: 4),
            Text(
              status,
              style: _sans(context, size: 14, color: scheme.onSurface),
            ),
          ],
        ],
      ),
    );
  }
}

/// A customer-resolution question whose options bind the Salon to a client.
class _ChoiceBlock extends StatelessWidget {
  const _ChoiceBlock({
    required this.messageId,
    required this.block,
    this.onSelectCustomer,
  });

  final String messageId;
  final ThreadBlock block;
  final ValueChanged<String>? onSelectCustomer;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final options = block.options;
    if (options.isEmpty) {
      return const SizedBox.shrink();
    }
    final prompt = block.prompt;

    return Column(
      key: ValueKey('message_choice_$messageId'),
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: [
        if (prompt != null) ...[
          Text(
            prompt,
            style: _sans(context, color: scheme.onSurface),
          ),
          const SizedBox(height: 8),
        ],
        for (final option in options) ...[
          _ChoiceOption(
            messageId: messageId,
            option: option,
            onSelectCustomer: onSelectCustomer,
          ),
          const SizedBox(height: 6),
        ],
      ],
    );
  }
}

/// One candidate client in a resolution `choice`.
class _ChoiceOption extends StatelessWidget {
  const _ChoiceOption({
    required this.messageId,
    required this.option,
    this.onSelectCustomer,
  });

  final String messageId;
  final Map<String, dynamic> option;
  final ValueChanged<String>? onSelectCustomer;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final customerId = option['customerId']?.toString() ?? '';
    final fullName = option['fullName']?.toString();
    final status = option['status']?.toString();

    return InkWell(
      key: ValueKey('message_choice_${messageId}_$customerId'),
      onTap: onSelectCustomer == null
          ? null
          : () => onSelectCustomer!(customerId),
      borderRadius: BorderRadius.circular(8),
      child: Container(
        width: double.infinity,
        padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
        decoration: BoxDecoration(
          borderRadius: BorderRadius.circular(8),
          border: Border.all(color: scheme.outlineVariant),
        ),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              (fullName == null || fullName.isEmpty) ? 'Customer' : fullName,
              style: _sans(
                context,
                weight: FontWeight.w500,
                color: scheme.onSurface,
              ),
            ),
            if (status != null && status.isNotEmpty)
              Text(
                status,
                style: _sans(
                  context,
                  size: 12,
                  height: 1.4,
                  color: scheme.onSurfaceVariant,
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// The one-line stand-in for a block this build has not been taught.
///
/// A block-only message must never draw an empty bubble, so an unknown type says
/// what it is rather than disappearing.
class _SummaryChip extends StatelessWidget {
  const _SummaryChip({required this.block});

  final ThreadBlock block;

  @override
  Widget build(BuildContext context) {
    final scheme = Theme.of(context).colorScheme;
    final summary = block.text ?? block.name ?? 'Update';
    return Container(
      key: ValueKey('message_block_summary_${block.type}'),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 7),
      decoration: BoxDecoration(
        color: scheme.surfaceContainerHigh,
        borderRadius: BorderRadius.circular(10),
      ),
      child: Text(
        summary,
        style: _sans(
          context,
          size: 13,
          height: 1.4,
          color: scheme.onSurfaceVariant,
        ),
      ),
    );
  }
}

/// The ink a paragraph takes from the bubble it sits in.
Color _inkFor(ColorScheme scheme, BubbleTone tone) =>
    tone == BubbleTone.own ? scheme.onPrimary : scheme.onSurface;
