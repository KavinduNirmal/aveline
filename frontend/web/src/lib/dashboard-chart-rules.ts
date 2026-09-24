/**
 * Every tenant-tree chart: a `null` bucket is a **gap**, never a line through zero.
 *
 * A bucket with no orders carries `null` rather than `0`
 * (`Aveline.Api/Modules/Commerce/DTOs/TenantDashboardDtos.cs:183-188`), so `connectNulls: true`
 * would draw a line straight through a measurement the server never produced. This mirrors
 * `lib/admin/data-quality.ts`'s `CONNECT_NULLS`, which the console enforces mechanically via
 * `test/admin-conformance.test.ts`; the tenant tree is held to the same rule by
 * `test/tenant-conformance.test.ts`.
 *
 * It lives in `lib/` rather than beside a component because every tenant chart shares one answer to
 * this question. A per-chart literal is how two charts end up disagreeing about what a blank means.
 */
export const CONNECT_NULLS = false

/**
 * The dashboard's accent for a **figure** — the theme's primary, `#8b2e42` (wine-rose, "Lina,
 * commerce") in light mode and `#ffcdd5` in dark.
 *
 * A near-black numeral is the default anywhere `text-card-foreground` is inherited, and on this
 * surface it made a screen of figures read as a spreadsheet rather than as the product's. A
 * customer's own numbers are the most important thing on the page, so they are the one thing that
 * carries the brand.
 *
 * It is a **valid Tailwind class**, not a token name: the tenant conformance gate forbids a raw
 * palette utility and a bare hex, but `text-primary` resolves to `--primary`, which `index.css`
 * defines for light and remaps in `.dark`. A card surface uses the same accent through
 * `KPI_ACCENT_VAR` — an icon tile or a wash — and both forms are written out rather than assembled
 * at runtime, because Tailwind compiles classes statically and a name built from parts would never
 * be generated.
 */
export const FIGURE_ACCENT_CLASS = 'text-primary'

/**
 * The `--kpi-accent` custom property a card publishes for its own tint and icon.
 *
 * One property, read by both, so a card's icon tint and its wash cannot disagree about the metric's
 * colour. Set it on the card's root element through `accentStyle` rather than repeating the name.
 */
export const KPI_ACCENT_VAR = '--kpi-accent'

/** The `style` that publishes an accent token to a subtree. `undefined` when there is no accent. */
export function accentStyle(token: string | undefined) {
  return token === undefined ? undefined : ({ [KPI_ACCENT_VAR]: token } as Record<string, string>)
}
