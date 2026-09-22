/**
 * Builds what a piece's floor-tag QR encodes.
 *
 * A code that names only a piece id is tenant-less: the same id in another boutique is a different
 * piece, so a scanner cannot tell which shop's catalog the code belongs to. The URL therefore names
 * the boutique — the piece's own tenant route, `/app/b/{slug}/catalog/{itemId}` — and when no slug
 * is in hand it carries the organisation id in the query rather than dropping the shop entirely.
 */

export interface ItemQrTarget {
  itemId: string
  sku?: string | null
  organizationId?: string | null
  organizationSlug?: string | null
}

/** The origin a code should open against. Falls back to the public host off the browser. */
function resolveOrigin(origin?: string): string {
  if (origin) return origin
  return typeof window !== 'undefined' ? window.location.origin : 'https://aveline.app'
}

/**
 * The absolute, org-identified URL a scanner should open for the piece.
 */
export function buildItemQrUrl(target: ItemQrTarget, origin?: string): string {
  const base = resolveOrigin(origin)
  const slug = target.organizationSlug?.trim()
  if (slug) {
    return `${base}/app/b/${encodeURIComponent(slug)}/catalog/${target.itemId}`
  }

  const organizationId = target.organizationId?.trim()
  const query = organizationId ? `?org=${encodeURIComponent(organizationId)}` : ''
  return `${base}/catalog/items/${target.itemId}${query}`
}

/**
 * The structured encoding: everything a scanner needs in one payload, including the organisation.
 */
export function buildItemQrJson(target: ItemQrTarget, origin?: string): string {
  return JSON.stringify({
    type: 'aveline_inventory_item',
    // Omitted, never a placeholder: an absent organisation stays absent rather than naming another
    // shop.
    orgId: target.organizationId || undefined,
    itemId: target.itemId,
    sku: target.sku || 'AVL-000',
    url: buildItemQrUrl(target, origin),
    v: 1,
  })
}
