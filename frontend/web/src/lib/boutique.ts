/**
 * Boutique details constraints for the owner onboarding step. Kept in one place so the
 * step's live validation and the wizard's submit-time validation never drift.
 */

/** Max characters allowed for the boutique name (backend: MaxLength(200)). */
export const BOUTIQUE_NAME_MAX = 200

/** Max characters allowed for the physical address (backend: MaxLength(500)). */
export const ADDRESS_MAX = 500

/** Max characters allowed for the boutique description. */
export const BOUTIQUE_DESCRIPTION_MAX = 500

/** Max characters allowed for the logo URL (backend: MaxLength(1000)). */
export const LOGO_URL_MAX = 1000

/** Sri Lanka calling code prefix shown on the phone field. */
export const LK_PREFIX = '+94'

/** Number of national (local) significant digits — excludes the +94 country code. */
export const LK_LOCAL_DIGITS = 9

/**
 * Matches a fully formatted Sri Lankan number: `+94 77 12 12 123`.
 * Local part is 9 digits grouped as 2-2-2-3.
 */
export const LK_PHONE_REGEX = /^\+94 \d{2} \d{2} \d{2} \d{3}$/

/** Extracts up to 9 local digits, dropping a leading `0` or the `94` country code. */
function toLocalDigits(raw: string): string {
  let digits = raw.replace(/\D/g, '')
  if (digits.startsWith('94')) digits = digits.slice(2)
  if (digits.startsWith('0')) digits = digits.slice(1)
  return digits.slice(0, LK_LOCAL_DIGITS)
}

/**
 * Formats a raw phone value into `+94 77 12 12 123`. Any non-digit characters are ignored
 * (the input accepts numbers only); the country code prefix is always rendered.
 */
export function formatLkPhone(raw: string): string {
  const digits = toLocalDigits(raw)
  if (!digits) return `${LK_PREFIX} `
  const groups = [
    digits.slice(0, 2),
    digits.slice(2, 4),
    digits.slice(4, 6),
    digits.slice(6, LK_LOCAL_DIGITS),
  ].filter(Boolean)
  return `${LK_PREFIX} ${groups.join(' ')}`
}

/** Whether a phone value is a complete, valid Sri Lankan number (9 local digits). */
export function isValidLkPhone(value: string): boolean {
  return LK_PHONE_REGEX.test(value.trim())
}
