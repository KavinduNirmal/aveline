import type { Permission } from "@/lib/admin/permissions"

/**
 * The recompute capability.
 *
 * `POST /admin/pricing/rules/{id}/recompute` returns `200` with a `PricingRecomputeResult` and is
 * guarded by `pricing:backdate` (`PricingEndpoints.cs:203-204,211`). The `admin` role is
 * deliberately denied that permission (`Permissions.cs:107`), so an `admin` holding
 * `pricing:manage` is still `403` on recompute and on any backdated write. The console therefore
 * gates the control on the **capability**, not on the page's permission.
 */
export const RECOMPUTE_PERMISSION: Permission = 'pricing:backdate'

export const RECOMPUTE_DISABLED_REASON =
  'Requires the pricing:backdate grant, which the admin role does not hold.'

export function canRecompute(can: (permission: Permission) => boolean): boolean {
  return can(RECOMPUTE_PERMISSION)
}

/** `Pricing:UseLegacyFormula` makes every pricing write inert: it succeeds and changes nothing. */
export const LEGACY_FORMULA_BANNER =
  'The legacy formula flag is enabled: pricing writes are recorded, but they do not change what customers are charged.'

/** Mirrors `Permissions.All` for the pricing surface, in the order the page renders them. */
export const PRICING_READ_PERMISSION: Permission = 'pricing:view'
export const PRICING_WRITE_PERMISSION: Permission = 'pricing:manage'
