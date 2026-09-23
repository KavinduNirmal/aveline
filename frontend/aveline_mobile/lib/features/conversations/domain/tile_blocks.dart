/// Which content blocks are product tiles, when a message carries a row of them,
/// and which look photographs are worth repeating.
///
/// A mirror of the web's `components/conversation/tileBlocks.ts`. It lives apart
/// from the renderer because both the row layout and the bubble width need the
/// same answers, and the answers are pure enough to pin without pumping a widget.
library;

import 'thread_message.dart';

/// The block types that can render as a product tile.
///
/// A run of them is one recommendation set, and the set belongs on a row: an agent
/// emits `piece` blocks back to back (and its `look` blocks after them), so stacking
/// them vertically buried the second piece under the first and made a curated answer
/// read as a single suggestion.
const Set<String> tileBlockTypes = {'piece', 'look'};

/// True when [block] renders as a product tile, which is what puts it in a row.
///
/// A piece always is. A look is one only while it has a photograph: without one it
/// is Elle's styling note about the row — prose, not a card — and it belongs at the
/// row's full width.
bool isTileBlock(ThreadBlock? block) {
  if (block == null || !tileBlockTypes.contains(block.type)) {
    return false;
  }
  return block.type == 'piece' || (block.imageUrl?.isNotEmpty ?? false);
}

/// Drops a look's photograph when it is one of the pieces' own.
///
/// The renderer normalises with this before it draws anything, so the row and the
/// bubble width cannot disagree about what the message carries.
List<ThreadBlock> withoutBorrowedLookImages(List<ThreadBlock> blocks) {
  final pieceImages = <String>{
    for (final block in blocks)
      if (block.type == 'piece' && block.imageUrl != null) block.imageUrl!,
  };
  if (pieceImages.isEmpty) {
    return blocks;
  }

  return [
    for (final block in blocks)
      if (block.type == 'look' &&
          block.imageUrl != null &&
          pieceImages.contains(block.imageUrl))
        block.withoutImageUrl()
      else
        block,
  ];
}

/// True when a message carries two tiles side by side.
///
/// The bubble spans its full cap width for exactly these messages: the row needs a
/// definite width to count its columns against, and a message with a photograph in
/// it already reached that width through the image's own intrinsic size. A lone tile
/// is left to a shrink-to-fit bubble, where the tile's own cap keeps it a card rather
/// than a full-width band.
///
/// The blocks are normalised first so this answer cannot disagree with what the
/// renderer draws: a look whose photograph was borrowed is a note, not a tile, and
/// must not widen the bubble as one.
bool hasTileRow(List<ThreadBlock>? blocks) {
  final parsed = withoutBorrowedLookImages(blocks ?? const []);
  for (var i = 0; i < parsed.length - 1; i += 1) {
    if (isTileBlock(parsed[i]) && isTileBlock(parsed[i + 1])) {
      return true;
    }
  }
  return false;
}

/// A run of consecutive tiles, or one block that has to keep the full bubble width.
sealed class SalonBlockGroup {
  const SalonBlockGroup();
}

/// Tiles that sit together in one row of the set.
final class TileRun extends SalonBlockGroup {
  const TileRun(this.blocks);

  final List<ThreadBlock> blocks;

  @override
  String toString() => 'TileRun(${blocks.length})';
}

/// A block drawn on its own: prose, a table, a card, a note.
final class LoneBlock extends SalonBlockGroup {
  const LoneBlock(this.block);

  final ThreadBlock block;

  @override
  String toString() => 'LoneBlock(${block.type})';
}

/// Groups consecutive tiles together; every other block stands on its own.
///
/// Prose between two pieces starts a new run, which is what keeps a caption with its
/// own tile instead of letting it join the row above.
List<SalonBlockGroup> groupTileBlocks(List<ThreadBlock> blocks) {
  final groups = <SalonBlockGroup>[];
  for (final block in blocks) {
    if (isTileBlock(block)) {
      final last = groups.isEmpty ? null : groups.last;
      if (last is TileRun) {
        last.blocks.add(block);
      } else {
        groups.add(TileRun([block]));
      }
      continue;
    }
    groups.add(LoneBlock(block));
  }
  return groups;
}
