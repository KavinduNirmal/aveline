/**
 * The tenant dashboard's one money formatter.
 *
 * Two rules, stated once here so they cannot drift between panels:
 *
 * 1. **`null` renders "not measured", never "0".** A dashboard that shows `LKR 0` for a value the
 *    server did not measure is telling the owner a fact about their shop that nobody established.
 *    Every nullable metric in this tree is typed `number | null` and passed through here.
 * 2. **The currency symbol comes from one place.** The server returns the ISO code from
 *    `Organization.Currency`; formatting is centralised so two panels cannot disagree about the
 *    symbol, the separators or the decimals.
 */

/** The label shown for a value the server did not measure. */
export const NOT_MEASURED = 'not measured'

/**
 * Formats a money value for display.
 *
 * @param value the amount, or `null` when the server did not measure it
 * @param currency the ISO-4217 code; defaults to `LKR`, the platform default
 * @param locale the locale to format in; defaults to the runtime locale
 */
export function formatMoney(
  value: number | null | undefined,
  currency = 'LKR',
  locale?: string,
): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return NOT_MEASURED
  }

  return new Intl.NumberFormat(locale, {
    style: 'currency',
    currency,
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(value)
}

/**
 * Formats a whole-unit count (Blossoms, visits, clients). A count of `0` is a real measurement, so
 * it renders as `0`; only an absent value is "not measured".
 */
export function formatCount(value: number | null | undefined, locale?: string): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return NOT_MEASURED
  }

  return new Intl.NumberFormat(locale).format(value)
}

/**
 * Formats a percentage that may not have been measurable. `null` in, "not measured" out; a measured
 * `0` is `0%`.
 */
export function formatPercent(value: number | null | undefined, locale?: string): string {
  if (value === null || value === undefined || Number.isNaN(value)) {
    return NOT_MEASURED
  }

  return new Intl.NumberFormat(locale, {
    style: 'percent',
    minimumFractionDigits: 0,
    maximumFractionDigits: 1,
  }).format(value / 100)
}
