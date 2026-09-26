/**
 * Which content blocks are product tiles, when a message carries a row of them, and which look
 * photographs are worth repeating.
 *
 * This lives apart from `blocks.tsx` because both the renderer there and `MessageBubble` need the
 * answers, and a component module that also exports plain functions cannot be fast-refreshed.
 */

/**
 * The block types that can render as a product tile.
 *
 * A run of them is one recommendation set, and the set belongs on a row: an agent emits `piece`
 * blocks back to back (and its `look` blocks after them), so stacking them vertically buried the
 * second piece under the first and made a curated answer read as a single suggestion.
 */
export const TILE_BLOCK_TYPES = new Set(['piece', 'look'])

/**
 * True when a block renders as a product tile, which is what puts it in a row.
 *
 * A piece always is. A look is one only while it has a photograph: without one it is Elle's styling
 * note about the row — prose, not a card — and it belongs at the row's full width.
 */
export function isTileBlock(block: unknown): boolean {
  if (!block || typeof block !== 'object') return false
  const candidate = block as { type?: string; imageUrl?: string }
  if (!TILE_BLOCK_TYPES.has(candidate.type ?? '')) return false
  return candidate.type === 'piece' || Boolean(candidate.imageUrl)
}

/** The two fields the rules below read, which every content block carries or does not. */
interface BlockLike {
  type?: string
  imageUrl?: string
}

/**
 * Drops a look's photograph when it is one of the pieces' own.
 *
 * Elle composes a look around the pieces it matched, and the composer used to hand the look the
 * first matched piece's photo as its own. The Salon then showed that photograph twice: once on the
 * piece, once on the look. New answers no longer borrow one, but every message already stored in a
 * thread still carries the copy, so it is dropped on the way to the renderer. The piece keeps its
 * photograph; the look becomes the styling note it always was.
 *
 * The cast mirrors how `blocks.tsx` already treats the stored payload: the block shapes come from
 * the server, and only the fields read here are assumed.
 */
export function withoutBorrowedLookImages(blocks: unknown[]): unknown[] {
  const rows = blocks as BlockLike[]
  const pieceImages = new Set(
    rows.filter((row) => row.type === 'piece' && row.imageUrl).map((row) => row.imageUrl),
  )
  if (pieceImages.size === 0) return blocks

  return rows.map((row) =>
    row.type === 'look' && row.imageUrl && pieceImages.has(row.imageUrl)
      ? { ...row, imageUrl: undefined }
      : row,
  )
}

/**
 * True when a message carries two tiles side by side.
 *
 * `MessageBubble` spans the bubble's full width for exactly these messages: the row needs a
 * definite width to count its columns against, and a message with a photograph in it already
 * reached that width through the image's own intrinsic size. A lone tile is left to a
 * shrink-to-fit bubble, where the tile's own cap keeps it a card rather than a full-width band.
 *
 * The blocks are normalised first so this answer cannot disagree with what `BlockList` renders: a
 * look whose photograph was borrowed is a note, not a tile, and must not widen the bubble as one.
 */
export function hasTileRow(blocks: unknown[] | null | undefined): boolean {
  const parsed = withoutBorrowedLookImages(blocks ?? [])
  return parsed.some((block, i) => isTileBlock(block) && isTileBlock(parsed[i + 1]))
}
