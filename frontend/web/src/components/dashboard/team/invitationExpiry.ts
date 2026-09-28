/**
 * How long an onboarding code has left, and whether it is close enough to expiry to call out.
 *
 * Kept beside the drawer rather than inside it so the component file exports components only, and so
 * the phrasing of a remaining lifetime is testable without rendering anything.
 */

/** A code this close to expiry is worth flagging; matches the page badge's six-hour threshold. */
export const EXPIRING_SOON_MS = 6 * 60 * 60 * 1000

/**
 * How long a code has left, in the unit a person would say it in.
 *
 * An expired code is **not** called expired: the server refuses it, but this only knows the
 * timestamp it was given, so it says "expires now" rather than asserting a server decision.
 */
export function expiryLabel(expiresAt: string, now = Date.now()): string {
  const remaining = new Date(expiresAt).getTime() - now
  if (Number.isNaN(remaining) || remaining <= 0) {
    return 'expires now'
  }

  const minutes = Math.floor(remaining / 60_000)
  if (minutes < 60) {
    return `expires in ${Math.max(1, minutes)} minute${minutes === 1 ? '' : 's'}`
  }

  const hours = Math.round(minutes / 60)
  if (hours < 48) {
    return `expires in ${hours} hour${hours === 1 ? '' : 's'}`
  }

  const days = Math.round(hours / 24)
  return `expires in ${days} day${days === 1 ? '' : 's'}`
}

/** True when a code expires within [EXPIRING_SOON_MS] but has not lapsed yet. */
export function isExpiringSoon(expiresAt: string, now = Date.now()): boolean {
  const remaining = new Date(expiresAt).getTime() - now
  return remaining > 0 && remaining < EXPIRING_SOON_MS
}
